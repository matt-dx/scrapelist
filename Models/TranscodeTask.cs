namespace Scrapelist.Models;

public record TranscodeTask
{
    public required TranscodeType Type { get; init; }

    // Audio transcode paths
    public string? AudioPartPath { get; init; }
    public string? AudioRawPath { get; init; }
    public string? AudioFinalPath { get; init; }

    // Video mux paths
    public string? VideoPartPath { get; init; }
    public string? VideoAudioPartPath { get; init; }
    public string? VideoMuxPath { get; init; }
    public string? VideoFinalPath { get; init; }

    public VideoCodec Codec { get; init; }
}

public enum TranscodeType
{
    AudioOnly,
    VideoOnly,
    Both
}
