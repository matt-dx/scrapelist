using System.Threading.Channels;
using Scrapelist.Cli;
using Scrapelist.Models;

namespace Scrapelist.Services;

public class DownloadManager
{
    private readonly CliOptions _options;
    private readonly YouTubeService _youtube;
    private readonly StreamDownloader _downloader;
    private readonly FfmpegService _ffmpeg;
    private readonly FileNamingService _naming;
    private readonly PlaylistWriter _playlistWriter;
    private readonly DebugLogger _log;

    private readonly List<DownloadItem> _allItems = [];
    private readonly Lock _lock = new();

    private readonly Channel<DownloadItem> _transcodeChannel = Channel.CreateUnbounded<DownloadItem>();
    private Task? _transcodeWorkerTask;

    private PlaylistInfo? _playlistInfo;
    private bool _isCompleted;
    public IReadOnlyList<DownloadItem> AllItems => _allItems;
    public PlaylistInfo? Playlist => _playlistInfo;
    public bool IsCompleted => _isCompleted;

    public string SourceUri => _options.Uri.ToString();
    public DownloadType DownloadType => _options.Type;

    public DownloadManager(
        CliOptions options,
        YouTubeService youtube,
        StreamDownloader downloader,
        FfmpegService ffmpeg,
        FileNamingService naming,
        PlaylistWriter playlistWriter,
        DebugLogger log)
    {
        _options = options;
        _youtube = youtube;
        _downloader = downloader;
        _ffmpeg = ffmpeg;
        _naming = naming;
        _playlistWriter = playlistWriter;
        _log = log;
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        _playlistInfo = await _youtube.TryResolvePlaylistAsync(_options.Uri, _options.Retries, ct);

        if (_playlistInfo is not null)
        {
            _allItems.AddRange(_playlistInfo.Items);
            _log.Log("Init", $"Playlist '{_playlistInfo.Title}' — {_allItems.Count} items");
        }
        else
        {
            var item = await _youtube.ResolveVideoAsync(_options.Uri, _options.Retries, ct);
            _allItems.Add(item);
            _log.Log("Init", $"Single video: {item.Title}");
        }

        var outputDir = Path.GetFullPath(_options.OutputDirectory);
        Directory.CreateDirectory(outputDir);

        foreach (var item in _allItems)
            AssignFilePaths(item);

        // Mark items that already exist as skipped
        foreach (var item in _allItems)
        {
            var needsAudio = _options.Type is DownloadType.Audio or DownloadType.Both;
            var needsVideo = _options.Type is DownloadType.Video or DownloadType.Both;

            var audioSatisfied = !needsAudio
                || (item.AudioFilePath is not null && File.Exists(Path.Combine(outputDir, item.AudioFilePath)));
            var videoSatisfied = !needsVideo
                || (item.VideoFilePath is not null && File.Exists(Path.Combine(outputDir, item.VideoFilePath)));

            if (audioSatisfied && videoSatisfied)
            {
                item.Status = DownloadStatus.Skipped;
                _log.Log("Init", $"Skipping (exists): {item.Title}");
            }
        }

    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        // Start the background transcoding worker
        _transcodeWorkerTask = RunTranscodeWorkerAsync(ct);

        var downloadSemaphore = new SemaphoreSlim(_options.MaxParallel);
        var downloadTasks = new List<Task>();

        while (!ct.IsCancellationRequested)
        {
            var item = DequeueNextItem();
            if (item is null)
            {
                if (downloadTasks.Count > 0)
                {
                    var completed = await Task.WhenAny(downloadTasks);
                    downloadTasks.Remove(completed);
                    await completed;
                    continue;
                }

                // No pending items and no active downloads.
                // Check if transcoding failures might requeue items.
                if (HasItemsInState(DownloadStatus.Transcoding))
                {
                    await Task.Delay(500, ct);
                    continue;
                }

                break;
            }

            await downloadSemaphore.WaitAsync(ct);
            var task = Task.Run(async () =>
            {
                try
                {
                    await DownloadItemAsync(item, ct);
                }
                finally
                {
                    downloadSemaphore.Release();
                }
            }, ct);

            downloadTasks.Add(task);
        }

        if (downloadTasks.Count > 0)
            await Task.WhenAll(downloadTasks);

        // Signal no more transcode items and wait for all transcodes to finish
        _transcodeChannel.Writer.Complete();
        if (_transcodeWorkerTask is not null)
            await _transcodeWorkerTask;

        // Final playlist write
        await WritePlaylistAsync();

        _isCompleted = true;
        _log.Log("Status", "All downloads and transcodes complete");
    }

    // --- Transcoding Worker ---

    private async Task RunTranscodeWorkerAsync(CancellationToken ct)
    {
        var semaphore = new SemaphoreSlim(2); // Max 2 parallel transcodes (CPU-heavy)
        var tasks = new List<Task>();

        await foreach (var item in _transcodeChannel.Reader.ReadAllAsync(ct))
        {
            await semaphore.WaitAsync(ct);
            var task = Task.Run(async () =>
            {
                try
                {
                    await TranscodeItemAsync(item, ct);
                }
                finally
                {
                    semaphore.Release();
                }
            }, ct);
            tasks.Add(task);

            // Clean up completed tasks
            tasks.RemoveAll(t => t.IsCompleted);
        }

        if (tasks.Count > 0)
            await Task.WhenAll(tasks);
    }

    private async Task TranscodeItemAsync(DownloadItem item, CancellationToken ct)
    {
        try
        {
            var task = item.PendingTranscode
                ?? throw new InvalidOperationException("No transcode task assigned");

            _log.Log("Transcode", $"Starting: {item.Title}");

            item.StartedAt = DateTime.UtcNow;
            Action<double> onProgress = progress =>
            {
                lock (_lock) { item.Progress = progress; }
            };

            if (task.Type is TranscodeType.AudioOnly or TranscodeType.Both)
            {
                if (!File.Exists(task.AudioFinalPath))
                {
                    try
                    {
                        await _ffmpeg.TranscodeAudioAsync(task.AudioPartPath!, task.AudioRawPath!,
                            item.Duration, onProgress, ct);
                    }
                    catch
                    {
                        TryDelete(task.AudioPartPath!);
                        TryDelete(task.AudioRawPath!);
                        throw;
                    }
                    TryDelete(task.AudioPartPath!);
                    File.Move(task.AudioRawPath!, task.AudioFinalPath!, overwrite: true);
                }
            }

            if (task.Type is TranscodeType.VideoOnly or TranscodeType.Both)
            {
                if (!File.Exists(task.VideoFinalPath))
                {
                    try
                    {
                        await _ffmpeg.MuxAsync(task.VideoPartPath!, task.VideoAudioPartPath!,
                            task.VideoMuxPath!, task.Codec, task.SubtitlePath,
                            item.Duration, onProgress, ct);
                    }
                    catch
                    {
                        TryDelete(task.VideoPartPath!);
                        TryDelete(task.VideoAudioPartPath!);
                        TryDelete(task.VideoMuxPath!);
                        if (task.SubtitlePath is not null) TryDelete(task.SubtitlePath);
                        throw;
                    }
                    TryDelete(task.VideoPartPath!);
                    TryDelete(task.VideoAudioPartPath!);
                    if (task.SubtitlePath is not null) TryDelete(task.SubtitlePath);
                    File.Move(task.VideoMuxPath!, task.VideoFinalPath!, overwrite: true);
                }
            }

            lock (_lock)
            {
                item.Status = DownloadStatus.Completed;
                item.Progress = 1.0;
                item.PendingTranscode = null;
            }
            _log.Log("Status", $"{item.Title} -> Completed");

            await WritePlaylistAsync();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleTranscodeFailure(item, ex);
        }
    }

    // --- Download Phase ---

    private DownloadItem? DequeueNextItem()
    {
        lock (_lock)
        {
            var item = _allItems
                .Where(i => i.Status == DownloadStatus.Pending)
                .OrderBy(i => i.FailedAttempts)
                .ThenBy(i => i.PlaylistIndex)
                .FirstOrDefault();

            if (item is not null)
            {
                // Atomically mark as Downloading so it won't be dequeued again
                item.Status = DownloadStatus.Downloading;
                item.StartedAt = DateTime.UtcNow;
                _log.Log("Status", $"{item.Title} -> Downloading");
            }

            return item;
        }
    }

    private async Task DownloadItemAsync(DownloadItem item, CancellationToken ct)
    {
        var outputDir = Path.GetFullPath(_options.OutputDirectory);

        try
        {
            var manifest = await _youtube.GetStreamManifestAsync(item.VideoId, ct);

            var transcodeType = _options.Type switch
            {
                DownloadType.Audio => TranscodeType.AudioOnly,
                DownloadType.Video => TranscodeType.VideoOnly,
                DownloadType.Both => TranscodeType.Both,
                _ => TranscodeType.AudioOnly
            };

            var transcodeTask = new TranscodeTask { Type = transcodeType, Codec = _options.Codec };

            if (_options.Type is DownloadType.Audio or DownloadType.Both)
            {
                var audioPaths = await DownloadAudioRawAsync(item, manifest, outputDir, ct);
                transcodeTask = transcodeTask with
                {
                    AudioPartPath = audioPaths.PartPath,
                    AudioRawPath = audioPaths.RawPath,
                    AudioFinalPath = audioPaths.FinalPath
                };
            }

            if (_options.Type is DownloadType.Video or DownloadType.Both)
            {
                var videoPaths = await DownloadVideoRawAsync(item, manifest, outputDir, ct);
                transcodeTask = transcodeTask with
                {
                    VideoPartPath = videoPaths.VideoPartPath,
                    VideoAudioPartPath = videoPaths.AudioPartPath,
                    VideoMuxPath = videoPaths.MuxPath,
                    VideoFinalPath = videoPaths.FinalPath
                };

                // Download subtitles (non-blocking — skip if unavailable or on error)
                try
                {
                    var captionTrack = await _youtube.GetBestCaptionTrackAsync(item.VideoId, ct);
                    if (captionTrack is not null)
                    {
                        var subtitlePath = Path.Combine(outputDir,
                            _naming.GetPartFileName($"{item.VideoFilePath}.srt"));
                        await _youtube.DownloadCaptionAsync(captionTrack, subtitlePath, ct);
                        transcodeTask = transcodeTask with { SubtitlePath = subtitlePath };
                        _log.Log("Download", $"Subtitles downloaded ({captionTrack.Language.Name}): {item.Title}");
                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    _log.Log("Download", $"Subtitle download failed (skipping): {item.Title} — {ex.Message}");
                }
            }

            // Transition to Transcoding and enqueue
            item.PendingTranscode = transcodeTask;
            lock (_lock)
            {
                item.Status = DownloadStatus.Transcoding;
                item.ResetProgress();
            }
            _log.Log("Status", $"{item.Title} -> Transcoding");

            await _transcodeChannel.Writer.WriteAsync(item, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            HandleDownloadFailure(item, ex);
        }
    }

    private async Task<(string PartPath, string RawPath, string FinalPath)> DownloadAudioRawAsync(
        DownloadItem item,
        YoutubeExplode.Videos.Streams.StreamManifest manifest,
        string outputDir,
        CancellationToken ct)
    {
        var audioStream = _youtube.GetBestAudioStream(manifest);
        var totalBytes = _youtube.GetStreamSize(audioStream);
        var url = _youtube.GetStreamUrl(audioStream);
        var sourceContainer = _youtube.GetStreamContainer(audioStream);

        var finalPath = Path.Combine(outputDir, item.AudioFilePath!);
        // Use proper .m4a extension for transcode output so FFmpeg can determine format
        var rawPath = Path.Combine(outputDir, $"~transcoding~{Path.GetFileName(item.AudioFilePath!)}");
        var partPath = Path.Combine(outputDir, _naming.GetPartFileName(item.AudioFilePath!));

        if (!File.Exists(finalPath))
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            try
            {
                await _downloader.DownloadAsync(url, partPath, totalBytes,
                    (downloaded, total) =>
                    {
                        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                        UpdateProgress(item, downloaded, total);
                    },
                    timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Download timed out after {_options.TimeoutSeconds}s");
            }
        }

        return (partPath, rawPath, finalPath);
    }

    private async Task<(string VideoPartPath, string AudioPartPath, string MuxPath, string FinalPath)> DownloadVideoRawAsync(
        DownloadItem item,
        YoutubeExplode.Videos.Streams.StreamManifest manifest,
        string outputDir,
        CancellationToken ct)
    {
        var videoStream = _youtube.GetBestVideoStream(manifest);
        var audioStream = _youtube.GetBestAudioStream(manifest);

        var finalPath = Path.Combine(outputDir, item.VideoFilePath!);
        var videoPartPath = Path.Combine(outputDir, _naming.GetPartFileName($"{item.VideoFilePath}.video"));
        var audioPartPath = Path.Combine(outputDir, _naming.GetPartFileName($"{item.VideoFilePath}.audio"));
        // Use proper .m4v extension for mux output so FFmpeg can determine format
        var muxPath = Path.Combine(outputDir, $"~transcoding~{Path.GetFileName(item.VideoFilePath!)}");

        if (!File.Exists(finalPath))
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));

            try
            {
                var videoUrl = _youtube.GetStreamUrl(videoStream);
                var audioUrl = _youtube.GetStreamUrl(audioStream);
                var videoSize = _youtube.GetStreamSize(videoStream);
                var audioSize = _youtube.GetStreamSize(audioStream);
                var totalSize = (videoSize ?? 0) + (audioSize ?? 0);

                long videoDownloaded = 0;
                long audioDownloaded = 0;

                // Download video and audio streams in parallel
                await Task.WhenAll(
                    _downloader.DownloadAsync(videoUrl, videoPartPath, videoSize,
                        (downloaded, _) =>
                        {
                            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                            Interlocked.Exchange(ref videoDownloaded, downloaded);
                            UpdateProgress(item, Interlocked.Read(ref videoDownloaded) + Interlocked.Read(ref audioDownloaded), totalSize);
                        },
                        timeoutCts.Token),
                    _downloader.DownloadAsync(audioUrl, audioPartPath, audioSize,
                        (downloaded, _) =>
                        {
                            timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.TimeoutSeconds));
                            Interlocked.Exchange(ref audioDownloaded, downloaded);
                            UpdateProgress(item, Interlocked.Read(ref videoDownloaded) + Interlocked.Read(ref audioDownloaded), totalSize);
                        },
                        timeoutCts.Token));
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException($"Download timed out after {_options.TimeoutSeconds}s");
            }
        }

        return (videoPartPath, audioPartPath, muxPath, finalPath);
    }

    // --- Progress & State ---

    private void UpdateProgress(DownloadItem item, long downloaded, long total)
    {
        lock (_lock)
        {
            var newProgress = total > 0 ? (double)downloaded / total : 0;
            if (newProgress <= item.Progress && item.Progress > 0)
                return;

            item.DownloadedBytes = downloaded;
            item.TotalBytes = total;
            item.Progress = newProgress;
        }
    }

    private void HandleDownloadFailure(DownloadItem item, Exception ex)
    {
        lock (_lock)
        {
            item.FailedAttempts++;
            _log.Log("Error", $"{item.Title} download failed ({item.FailedAttempts}/{item.MaxRetries}): {ex.Message}");

            if (item.FailedAttempts >= item.MaxRetries)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = ex.Message;
            }
            else
            {
                item.Status = DownloadStatus.Pending;
                item.ResetProgress();
                item.ErrorMessage = $"Retrying ({item.FailedAttempts}/{item.MaxRetries}): {ex.Message}";
            }
        }
    }

    private void HandleTranscodeFailure(DownloadItem item, Exception ex)
    {
        lock (_lock)
        {
            item.FailedAttempts++;
            _log.Log("Error", $"{item.Title} transcode failed ({item.FailedAttempts}/{item.MaxRetries}): {ex.Message}");

            if (item.FailedAttempts >= item.MaxRetries)
            {
                item.Status = DownloadStatus.Failed;
                item.ErrorMessage = $"Transcode failed: {ex.Message}";
            }
            else
            {
                // Requeue as Pending — will re-download and re-transcode
                item.Status = DownloadStatus.Pending;
                item.ResetProgress();
                item.PendingTranscode = null;
                item.ErrorMessage = $"Transcode retry ({item.FailedAttempts}/{item.MaxRetries}): {ex.Message}";
            }
        }
    }

    // --- Helpers ---

    private bool HasItemsInState(DownloadStatus status)
    {
        lock (_lock)
        {
            return _allItems.Any(i => i.Status == status);
        }
    }

    private void AssignFilePaths(DownloadItem item)
    {
        if (_options.Type is DownloadType.Audio or DownloadType.Both)
            item.AudioFilePath = _naming.GetFileName(item, "m4a", _options.Indexed);

        if (_options.Type is DownloadType.Video or DownloadType.Both)
        {
            var videoExt = _options.Codec == VideoCodec.X265 ? "mkv" : "m4v";
            item.VideoFilePath = _naming.GetFileName(item, videoExt, _options.Indexed);
        }
    }

    private async Task WritePlaylistAsync()
    {
        if (_playlistInfo is null)
            return;

        var outputDir = Path.GetFullPath(_options.OutputDirectory);
        var playlistFileName = _naming.GetPlaylistFileName(_playlistInfo.Title);
        var playlistPath = Path.Combine(outputDir, playlistFileName);
        await _playlistWriter.WriteAsync(playlistPath, _allItems, _options.Type);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
