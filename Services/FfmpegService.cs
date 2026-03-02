using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;
using CliWrap;
using CliWrap.Buffered;

using CliRunner = CliWrap.Cli;

namespace Scrapelist.Services;

public class FfmpegService
{
    private readonly DebugLogger _log;
    private string? _ffmpegPath;

    public FfmpegService(DebugLogger log)
    {
        _log = log;
    }

    public async Task EnsureAvailableAsync()
    {
        // Check PATH first
        var pathResult = await TryFindOnPathAsync();
        if (pathResult is not null)
        {
            _ffmpegPath = pathResult;
            return;
        }

        // Check local .tools directory
        var toolsDir = Path.Combine(AppContext.BaseDirectory, ".tools");
        var localPath = GetLocalFfmpegPath(toolsDir);
        if (File.Exists(localPath))
        {
            _ffmpegPath = localPath;
            return;
        }

        // Download
        Console.WriteLine("FFmpeg not found. Downloading...");
        await DownloadFfmpegAsync(toolsDir);
        _ffmpegPath = localPath;

        if (!File.Exists(_ffmpegPath))
            throw new InvalidOperationException(
                "FFmpeg download failed. Please install FFmpeg manually and ensure it's on your PATH.");
    }

    public async Task TranscodeAudioAsync(string inputPath, string outputPath,
        TimeSpan? duration = null, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        if (_ffmpegPath is null)
            throw new InvalidOperationException("FFmpeg is not available. Call EnsureAvailableAsync first.");

        var args = new List<string> { "-i", inputPath, "-c:a", "aac", "-b:a", "256k" };
        args.AddRange(["-progress", "pipe:1", "-y", outputPath]);

        var ffmpegArgs = args.ToArray();
        _log.Log("FFmpeg", $"Transcode audio: {string.Join(' ', ffmpegArgs)}");

        var totalUs = duration?.TotalMicroseconds ?? 0;
        var stderr = new StringBuilder();

        var result = await CliRunner.Wrap(_ffmpegPath)
            .WithArguments(ffmpegArgs)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
                ParseFfmpegProgress(line, totalUs, onProgress)))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
            .ExecuteAsync(ct);

        if (result.ExitCode != 0)
        {
            _log.Log("FFmpeg", $"Audio transcode failed (exit {result.ExitCode}): {stderr}");
            throw new InvalidOperationException(
                $"FFmpeg audio transcode failed (exit code {result.ExitCode}): {stderr}");
        }
        _log.Log("FFmpeg", $"Audio transcode complete: {outputPath}");
    }

    public async Task MuxAsync(string videoPath, string audioPath, string outputPath,
        Models.VideoCodec codec, string? subtitlePath = null,
        TimeSpan? duration = null, Action<double>? onProgress = null, CancellationToken ct = default)
    {
        if (_ffmpegPath is null)
            throw new InvalidOperationException("FFmpeg is not available. Call EnsureAvailableAsync first.");

        var encoder = codec switch
        {
            Models.VideoCodec.X264 => "libx264",
            Models.VideoCodec.X265 => "libx265",
            _ => "libx265"
        };

        var args = new List<string> { "-i", videoPath, "-i", audioPath };

        if (subtitlePath is not null)
            args.AddRange(["-i", subtitlePath]);

        args.AddRange(["-c:v", encoder, "-preset", "fast", "-crf", "18"]);
        args.AddRange(["-c:a", "aac", "-b:a", "256k"]);

        if (subtitlePath is not null)
            args.AddRange(["-c:s", "srt"]);  // MKV supports SRT natively

        var isMkv = outputPath.EndsWith(".mkv", StringComparison.OrdinalIgnoreCase);
        if (!isMkv)
            args.AddRange(["-movflags", "+faststart"]);  // MP4/M4V only
        if (codec == Models.VideoCodec.X265 && !isMkv)
            args.AddRange(["-tag:v", "hvc1"]);           // MP4/M4V HEVC only

        args.AddRange(["-progress", "pipe:1", "-y", outputPath]);

        var ffmpegArgs = args.ToArray();
        _log.Log("FFmpeg", $"Mux: {string.Join(' ', ffmpegArgs)}");

        var totalUs = duration?.TotalMicroseconds ?? 0;
        var stderr = new StringBuilder();

        var result = await CliRunner.Wrap(_ffmpegPath)
            .WithArguments(ffmpegArgs)
            .WithValidation(CommandResultValidation.None)
            .WithStandardOutputPipe(PipeTarget.ToDelegate(line =>
                ParseFfmpegProgress(line, totalUs, onProgress)))
            .WithStandardErrorPipe(PipeTarget.ToStringBuilder(stderr))
            .ExecuteAsync(ct);

        if (result.ExitCode != 0)
        {
            _log.Log("FFmpeg", $"Mux failed (exit {result.ExitCode}): {stderr}");
            throw new InvalidOperationException(
                $"FFmpeg muxing failed (exit code {result.ExitCode}): {stderr}");
        }
        _log.Log("FFmpeg", $"Mux complete: {outputPath}");
    }

    private static void ParseFfmpegProgress(string line, double totalUs, Action<double>? onProgress)
    {
        if (onProgress is null || totalUs <= 0)
            return;

        // FFmpeg -progress pipe:1 outputs key=value pairs; out_time_us gives microseconds processed
        if (line.StartsWith("out_time_us=", StringComparison.Ordinal)
            && long.TryParse(line.AsSpan(12), out var us) && us >= 0)
        {
            var progress = Math.Clamp(us / totalUs, 0, 1);
            onProgress(progress);
        }
    }

    private static async Task<string?> TryFindOnPathAsync()
    {
        try
        {
            var name = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
            var whichCmd = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "where" : "which";

            var result = await CliRunner.Wrap(whichCmd)
                .WithArguments([name])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (result.ExitCode == 0)
            {
                var path = result.StandardOutput.Trim().Split('\n', '\r')[0];
                if (File.Exists(path))
                    return path;
            }
        }
        catch
        {
            // Ignore - ffmpeg not on PATH
        }

        return null;
    }

    private static string GetLocalFfmpegPath(string toolsDir)
    {
        var exe = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "ffmpeg.exe" : "ffmpeg";
        return Path.Combine(toolsDir, exe);
    }

    private static async Task DownloadFfmpegAsync(string toolsDir)
    {
        Directory.CreateDirectory(toolsDir);

        string downloadUrl;
        string archiveEntryName;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-win64-gpl.zip";
            archiveEntryName = "ffmpeg.exe";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            downloadUrl = "https://github.com/BtbN/FFmpeg-Builds/releases/download/latest/ffmpeg-master-latest-linux64-gpl.tar.xz";
            archiveEntryName = "ffmpeg";
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // macOS builds from evermeet.cx
            downloadUrl = "https://evermeet.cx/ffmpeg/getrelease/zip";
            archiveEntryName = "ffmpeg";
        }
        else
        {
            throw new PlatformNotSupportedException("Automatic FFmpeg download is not supported on this platform.");
        }

        using var httpClient = new HttpClient();
        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("scrapelist/1.0");

        var tempFile = Path.Combine(toolsDir, "ffmpeg-download.tmp");
        try
        {
            Console.Write("  Downloading FFmpeg... ");
            await using var downloadStream = await httpClient.GetStreamAsync(downloadUrl);
            await using var fileStream = File.Create(tempFile);
            await downloadStream.CopyToAsync(fileStream);
            fileStream.Close();
            Console.WriteLine("done.");

            Console.Write("  Extracting... ");
            await ExtractFfmpegAsync(tempFile, toolsDir, archiveEntryName);
            Console.WriteLine("done.");
        }
        finally
        {
            if (File.Exists(tempFile))
                File.Delete(tempFile);
        }
    }

    private static async Task ExtractFfmpegAsync(string archivePath, string toolsDir, string targetName)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            // Linux builds are .tar.xz — use system tar to extract
            var result = await CliRunner.Wrap("tar")
                .WithArguments(["xf", archivePath, "--wildcards", $"*/bin/{targetName}",
                    "--strip-components=2", "-C", toolsDir])
                .WithValidation(CommandResultValidation.None)
                .ExecuteBufferedAsync();

            if (result.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Failed to extract FFmpeg from tar.xz: {result.StandardError}");

            var outputPath = Path.Combine(toolsDir, targetName);
            File.SetUnixFileMode(outputPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            return;
        }

        // ZIP extraction (Windows, macOS)
        using var archive = ZipFile.OpenRead(archivePath);

        var entry = archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase)
            && e.FullName.Contains("bin/"));

        entry ??= archive.Entries.FirstOrDefault(e =>
            e.Name.Equals(targetName, StringComparison.OrdinalIgnoreCase));

        if (entry is null)
            throw new InvalidOperationException($"Could not find '{targetName}' in the downloaded archive.");

        var zipOutputPath = Path.Combine(toolsDir, targetName);
        entry.ExtractToFile(zipOutputPath, overwrite: true);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            File.SetUnixFileMode(zipOutputPath,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }
}
