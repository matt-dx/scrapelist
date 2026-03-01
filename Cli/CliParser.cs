using System.CommandLine;
using System.CommandLine.Parsing;
using Scrapelist.Models;

namespace Scrapelist.Cli;

public static class CliParser
{
    public static CliOptions? Parse(string[] args)
    {
        CliOptions? result = null;

        var uriArgument = new Argument<string>("uri")
        {
            Description = "YouTube video or playlist URL"
        };

        var typeOption = new Option<string>("--type")
        {
            Description = "Download type: audio, video, or both",
            DefaultValueFactory = _ => "both"
        };

        var retriesOption = new Option<int>("--retries")
        {
            Description = "Number of retries for failed downloads",
            DefaultValueFactory = _ => 3
        };

        var parallelOption = new Option<int>("--parallel")
        {
            Description = "Maximum number of parallel downloads",
            DefaultValueFactory = _ => 3
        };

        var indexedOption = new Option<bool>("--indexed")
        {
            Description = "Include index number in filenames",
            DefaultValueFactory = _ => false
        };

        var timeoutOption = new Option<int>("--timeout")
        {
            Description = "Download timeout in seconds",
            DefaultValueFactory = _ => 60
        };

        var outputOption = new Option<string>("--output")
        {
            Description = "Output directory for downloaded files",
            DefaultValueFactory = _ => "."
        };

        var codecOption = new Option<string>("--codec")
        {
            Description = "Video codec: x264 or x265",
            DefaultValueFactory = _ => "x265"
        };

        var debugOption = new Option<bool>("--debug")
        {
            Description = "Enable debug logging to file",
            DefaultValueFactory = _ => false
        };

        var rootCommand = new RootCommand("scrapelist - YouTube video/playlist downloader")
        {
            uriArgument,
            typeOption,
            retriesOption,
            parallelOption,
            indexedOption,
            timeoutOption,
            outputOption,
            codecOption,
            debugOption
        };

        rootCommand.SetAction(parseResult =>
        {
            var uriStr = parseResult.GetValue(uriArgument);
            var typeStr = parseResult.GetValue(typeOption) ?? "both";
            var retries = parseResult.GetValue(retriesOption);
            var parallel = parseResult.GetValue(parallelOption);
            var indexed = parseResult.GetValue(indexedOption);
            var timeout = parseResult.GetValue(timeoutOption);
            var output = parseResult.GetValue(outputOption) ?? ".";

            if (string.IsNullOrWhiteSpace(uriStr) || !Uri.TryCreate(uriStr, UriKind.Absolute, out var uri))
            {
                Console.Error.WriteLine($"Invalid URL: '{uriStr}'");
                return;
            }

            if (!Enum.TryParse<DownloadType>(typeStr, ignoreCase: true, out var downloadType))
            {
                Console.Error.WriteLine($"Invalid download type: '{typeStr}'. Must be audio, video, or both.");
                return;
            }

            var codecStr = parseResult.GetValue(codecOption) ?? "x265";
            if (!Enum.TryParse<VideoCodec>(codecStr, ignoreCase: true, out var codec))
            {
                Console.Error.WriteLine($"Invalid codec: '{codecStr}'. Must be x264 or x265.");
                return;
            }

            var debug = parseResult.GetValue(debugOption);

            result = new CliOptions(uri, downloadType, retries, parallel, indexed, timeout, output, codec, debug);
        });

        var config = new CommandLineConfiguration(rootCommand);
        config.Invoke(args);
        return result;
    }
}
