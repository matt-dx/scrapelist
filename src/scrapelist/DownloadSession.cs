using Scrapelist.Models;
using Scrapelist.Services;

namespace Scrapelist.Maui;

public class DownloadSession
{
    public DownloadOptions? Options { get; private set; }
    public DownloadManager? Manager { get; private set; }
    public DebugLogger? Logger { get; private set; }

    public void Start(
        DownloadOptions options,
        YouTubeService youtube,
        StreamDownloader downloader,
        FfmpegService ffmpeg,
        FileNamingService naming,
        PlaylistWriter playlistWriter)
    {
        Logger?.Dispose();

        Options = options;
        Logger = new DebugLogger(options.OutputDirectory, options.Debug);
        Manager = new DownloadManager(options, youtube, downloader, ffmpeg, naming, playlistWriter, Logger);
    }

    public void Reset()
    {
        Logger?.Dispose();
        Options = null;
        Manager = null;
        Logger = null;
    }
}
