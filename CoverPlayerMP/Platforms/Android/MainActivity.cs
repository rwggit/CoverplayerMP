using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
    ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                           ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density,
    Exported = true)]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "text/plain")]
[IntentFilter(new[] { Intent.ActionSend }, Categories = new[] { Intent.CategoryDefault }, DataMimeType = "application/octet-stream")]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _ = HandleSharedPlaylistAsync(Intent);
    }

    protected override void OnNewIntent(Intent? intent)
    {
        base.OnNewIntent(intent);
        _ = HandleSharedPlaylistAsync(intent);
    }

    private static async Task HandleSharedPlaylistAsync(Intent? intent)
    {
        var uri = intent?.GetParcelableExtra(Intent.ExtraStream) as Android.Net.Uri;
        if (uri is null)
            return;

        var services = IPlatformApplication.Current?.Services;
        var player = services?.GetService<IPlaylistPlayer>();
        if (player is null)
            return;

        var fileName = $"shared-{DateTime.UtcNow:yyyyMMddHHmmss}.m3u";
        var target = Path.Combine(FileSystem.CacheDirectory, fileName);
        await using var input = Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
        if (input is null)
            return;
        await using (var output = File.Create(target))
            await input.CopyToAsync(output);
        await player.LoadPlaylistAsync(target);
        await player.PlayAsync(0);
    }
}
