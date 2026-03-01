using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RazorConsole.Core;
using Scrapelist.Cli;
using Scrapelist.Models;
using Scrapelist.Services;
using Scrapelist.UI;

var options = CliParser.Parse(args);
if (options is null)
    return 1;

using var debugLogger = new DebugLogger(options.OutputDirectory, options.Debug);

// FFmpeg is needed for all modes (audio transcode + video mux)
var ffmpeg = new FfmpegService(debugLogger);
await ffmpeg.EnsureAvailableAsync();

var host = Host.CreateDefaultBuilder(args)
    .UseRazorConsole<App>(configure: config =>
    {
        config.ConfigureServices(services =>
        {
            services.AddSingleton(options);
            services.AddSingleton(debugLogger);
            services.AddSingleton<YouTubeService>();
            services.AddSingleton<DownloadManager>();
            services.AddSingleton<StreamDownloader>();
            services.AddSingleton(ffmpeg);
            services.AddSingleton<FileNamingService>();
            services.AddSingleton<PlaylistWriter>();
        });
    })
    .Build();

await host.RunAsync();
return 0;
