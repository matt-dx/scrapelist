namespace Scrapelist.Models;

public class DownloadItem
{
    public required string VideoId { get; init; }
    public required string Title { get; init; }
    public required string ChannelName { get; init; }
    public int PlaylistIndex { get; init; }
    public TimeSpan? Duration { get; init; }

    public DownloadStatus Status { get; set; } = DownloadStatus.Pending;

    private double _progress;
    public double Progress
    {
        get => _progress;
        set
        {
            // Enforce monotonic progress — never go backwards once above 0
            if (value >= _progress)
                _progress = value;
        }
    }

    public int FailedAttempts { get; set; }
    public int MaxRetries { get; set; }
    public long TotalBytes { get; set; }
    public long DownloadedBytes { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? ErrorMessage { get; set; }

    public string? AudioFilePath { get; set; }
    public string? VideoFilePath { get; set; }
    public TranscodeTask? PendingTranscode { get; set; }

    /// <summary>
    /// Explicitly resets progress to 0. Used only when an item is requeued after failure.
    /// </summary>
    public void ResetProgress()
    {
        _progress = 0;
        DownloadedBytes = 0;
        TotalBytes = 0;
    }

    public TimeSpan Elapsed => StartedAt.HasValue
        ? DateTime.UtcNow - StartedAt.Value
        : TimeSpan.Zero;

    public TimeSpan? EstimatedRemaining
    {
        get
        {
            if (Progress <= 0 || !StartedAt.HasValue)
                return null;

            var elapsed = Elapsed;
            var totalEstimated = TimeSpan.FromTicks((long)(elapsed.Ticks / Progress));
            var remaining = totalEstimated - elapsed;
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }
}
