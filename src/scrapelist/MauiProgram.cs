using Microsoft.Extensions.Logging;
using MudBlazor.Services;
using Scrapelist.Services;

namespace Scrapelist.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
            });

        builder.Services.AddMauiBlazorWebView();
        builder.Services.AddMudServices();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        // Scrapelist services
        builder.Services.AddSingleton<DownloadSession>();
        builder.Services.AddSingleton(new DebugLogger(null, false)); // disabled placeholder for DI
        builder.Services.AddSingleton<YouTubeService>();
        builder.Services.AddSingleton<StreamDownloader>();
        builder.Services.AddSingleton<FfmpegService>();
        builder.Services.AddSingleton<FileNamingService>();
        builder.Services.AddSingleton<PlaylistWriter>();
        builder.Services.AddLogging();

        return builder.Build();
    }
}
