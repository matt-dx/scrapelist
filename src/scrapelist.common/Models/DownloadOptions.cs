namespace Scrapelist.Models;

public record DownloadOptions(
    Uri Uri,
    DownloadType Type,
    int Retries,
    int MaxParallel,
    bool Indexed,
    int TimeoutSeconds,
    string OutputDirectory,
    VideoCodec Codec,
    bool Debug
);
