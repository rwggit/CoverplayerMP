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

    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.OnActivityResult(requestCode, resultCode, data);
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

        var displayName = Path.GetFileName(GetDisplayName(uri));
        var extension = string.Equals(Path.GetExtension(displayName), ".m3u8", StringComparison.OrdinalIgnoreCase) ? ".m3u8" : ".m3u";
        var fileName = string.IsNullOrEmpty(displayName) || !string.Equals(Path.GetExtension(displayName), extension, StringComparison.OrdinalIgnoreCase)
            ? $"shared{extension}"
            : displayName;
        var folder = Path.Combine(FileSystem.CacheDirectory, $"shared-{DateTime.UtcNow:yyyyMMddHHmmss}");
        Directory.CreateDirectory(folder);
        var target = Path.Combine(folder, fileName);
        await using var input = Android.App.Application.Context.ContentResolver?.OpenInputStream(uri);
        if (input is null)
            return;
        await using (var output = File.Create(target))
            await input.CopyToAsync(output);
        await player.LoadPlaylistAsync(target);
        await player.PlayAsync(0);
    }

    private static string? GetDisplayName(Android.Net.Uri uri)
    {
        using var cursor = Android.App.Application.Context.ContentResolver?.Query(uri, [Android.Provider.IOpenableColumns.DisplayName], null, null, null);
        if (cursor is not null && cursor.MoveToFirst())
        {
            var name = cursor.GetString(0);
            if (!string.IsNullOrEmpty(name))
                return name;
        }

        return uri.LastPathSegment;
    }
}
