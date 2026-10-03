using Microsoft.Extensions.Logging;
using CoverPlayerMP.Player;
using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP;

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
        builder.Services.AddSingleton<CoverPlayerMP.Shared.Localization.AppText>();
        builder.Services.AddSingleton<IPlaylistPlayer, MauiPlaylistPlayer>();

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }
}
