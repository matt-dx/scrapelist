using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RazorConsole.Core;
using Scrapelist.Cli;
using Scrapelist.Cli.UI;
using Scrapelist.Models;
using Scrapelist.Services;

var options = CliParser.Parse(args);
if (options is null)
    return 1;

using var debugLogger = new DebugLogger(options.OutputDirectory, options.Debug);

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
            services.AddSingleton<FfmpegService>();
            services.AddSingleton<FileNamingService>();
            services.AddSingleton<PlaylistWriter>();
            services.AddLogging(builder => builder.AddConsole());
        });
    })
    .Build();

await host.RunAsync();
return 0;
