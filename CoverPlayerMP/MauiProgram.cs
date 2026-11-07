using Microsoft.Maui.ApplicationModel;
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
        builder.Services.AddSingleton<IExternalBrowser, ExternalBrowser>();
        builder.Services.AddSingleton<CoverPlayerMP.Shared.Localization.AppText>();
        builder.Services.AddSingleton<IPlaylistPlayer, MauiPlaylistPlayer>();
        builder.Services.AddSingleton<HttpClient>();
        builder.Services.AddSingleton<CoverPlayerMP.Shared.Player.CoverSearch>();
        builder.Services.AddSingleton<CoverPlayerMP.Shared.Player.YouTubeSearchService>();
        builder.Services.AddSingleton<CoverPlayerMP.Shared.Player.MusicBrainzService>();
        builder.Services.AddSingleton<IAudioFingerprintProvider>(_ =>
            OperatingSystem.IsAndroid() || FfmpegNativeLibraryLoader.HasChromaprintLibrary(AppContext.BaseDirectory)
                ? new FfmpegAudioFingerprintProvider()
                : new FpcalcFingerprintProvider());
#if WINDOWS
        builder.Services.AddSingleton<IWindowControls, CoverPlayerMP.Platforms.Windows.WindowControls>();
#endif

#if DEBUG
        builder.Services.AddBlazorWebViewDeveloperTools();
        builder.Logging.AddDebug();
#endif

        return builder.Build();
    }

    private sealed class ExternalBrowser : IExternalBrowser
    {
        public Task OpenAsync(string url) => Launcher.Default.OpenAsync(url);
    }
}
