namespace Scrapelist.Models;

public enum DownloadStatus
{
    Pending,
    Downloading,
    Transcoding,
    Completed,
    Failed,
    Skipped
}
