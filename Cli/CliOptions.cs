using Scrapelist.Models;

namespace Scrapelist.Cli;

public record CliOptions(
    Uri Uri,
    DownloadType Type,
    int Retries,
    int MaxParallel,
    bool Indexed,
    int TimeoutSeconds,
    string OutputDirectory,
    VideoCodec Codec,
    bool Debug
)
{
}