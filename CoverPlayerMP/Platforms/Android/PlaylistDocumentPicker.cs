using Android.App;
using Android.Content;
using Android.Database;
using Android.Provider;
using AndroidEnvironment = Android.OS.Environment;
using AndroidUri = Android.Net.Uri;

namespace CoverPlayerMP.Platforms.Android;

public static class PlaylistDocumentPicker
{
    public const int RequestCode = 4711;
    public const int SaveRequestCode = 4712;
    public const int TreeRequestCode = 4713;
    private static TaskCompletionSource<AndroidUri?>? pendingTree;

    public static AndroidUri? FindPersistedTree(Context context, string? authority)
    {
        var permissions = context.ContentResolver?.PersistedUriPermissions;
        if (permissions is null)
            return null;

        foreach (var permission in permissions)
        {
            var uri = permission.Uri;
            if (uri is not null && permission.IsReadPermission && DocumentsContract.IsTreeUri(uri) &&
                string.Equals(uri.Authority, authority, StringComparison.Ordinal))
                return uri;
        }

        return null;
    }

    public static Task<AndroidUri?> PickTreeAsync(AndroidUri? initialLocation)
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Keine aktive Android-Activity.");

        pendingTree?.TrySetResult(null);
        pendingTree = new TaskCompletionSource<AndroidUri?>();

        var intent = new Intent(Intent.ActionOpenDocumentTree);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantPrefixUriPermission);
        if (initialLocation is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
            intent.PutExtra(DocumentsContract.ExtraInitialUri, initialLocation);
        activity.StartActivityForResult(intent, TreeRequestCode);
        return pendingTree.Task;
    }
    public const int AudioRequestCode = 4714;
    private static TaskCompletionSource<IReadOnlyList<AndroidUri>>? pendingAudio;

    public static Task<IReadOnlyList<AndroidUri>> PickAudioFilesAsync(AndroidUri? initialLocation)
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Keine aktive Android-Activity.");

        pendingAudio?.TrySetResult([]);
        pendingAudio = new TaskCompletionSource<IReadOnlyList<AndroidUri>>();

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantPersistableUriPermission);
        intent.SetType("audio/*");
        intent.PutExtra(Intent.ExtraAllowMultiple, true);
        if (initialLocation is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
            intent.PutExtra(DocumentsContract.ExtraInitialUri, initialLocation);
        activity.StartActivityForResult(intent, AudioRequestCode);
        return pendingAudio.Task;
    }

    private static TaskCompletionSource<AndroidUri?>? pending;
    private static TaskCompletionSource<AndroidUri?>? pendingSave;

    public static Task<AndroidUri?> PickSaveLocationAsync(string suggestedName, AndroidUri? initialLocation)
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Keine aktive Android-Activity.");

        pendingSave?.TrySetResult(null);
        pendingSave = new TaskCompletionSource<AndroidUri?>();

        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType("audio/x-mpegurl");
        intent.PutExtra(Intent.ExtraTitle, suggestedName);
        if (initialLocation is not null && OperatingSystem.IsAndroidVersionAtLeast(26))
            intent.PutExtra(DocumentsContract.ExtraInitialUri, initialLocation);
        activity.StartActivityForResult(intent, SaveRequestCode);
        return pendingSave.Task;
    }

    public static Task<AndroidUri?> PickAsync()
    {
        var activity = Platform.CurrentActivity
            ?? throw new InvalidOperationException("Keine aktive Android-Activity.");



        pending?.TrySetResult(null);
        pending = new TaskCompletionSource<AndroidUri?>();

        var intent = new Intent(Intent.ActionOpenDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPersistableUriPermission);
        intent.SetType("*/*");
        intent.PutExtra(Intent.ExtraMimeTypes, new[] { "audio/x-mpegurl", "audio/mpegurl", "application/vnd.apple.mpegurl", "application/x-mpegurl", "application/octet-stream" });
        activity.StartActivityForResult(intent, RequestCode);
        return pending.Task;
    }

    public static void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        if (requestCode == TreeRequestCode)
        {
            var tree = resultCode == Result.Ok ? data?.Data : null;
            if (tree is not null)
            {
                try
                {
                    global::Android.App.Application.Context.ContentResolver?.TakePersistableUriPermission(
                        tree, ActivityFlags.GrantReadUriPermission);
                }
                catch (Java.Lang.SecurityException)
                {
                }
            }

            pendingTree?.TrySetResult(tree);
            pendingTree = null;
            return;
        }

        if (requestCode == AudioRequestCode)
        {
            var files = new List<AndroidUri>();
            if (resultCode == Result.Ok && data is not null)
            {
                if (data.ClipData is { } clip)
                {
                    for (var i = 0; i < clip.ItemCount; i++)
                    {
                        var item = clip.GetItemAt(i)?.Uri;
                        if (item is not null)
                            files.Add(item);
                    }
                }
                else if (data.Data is { } single)
                    files.Add(single);
            }

            foreach (var file in files)
            {
                try
                {
                    global::Android.App.Application.Context.ContentResolver?.TakePersistableUriPermission(
                        file, ActivityFlags.GrantReadUriPermission);
                }
                catch (Java.Lang.SecurityException)
                {
                }
            }

            pendingAudio?.TrySetResult(files);
            pendingAudio = null;
            return;
        }

        if (requestCode == SaveRequestCode)
        {
            pendingSave?.TrySetResult(resultCode == Result.Ok ? data?.Data : null);
            pendingSave = null;
            return;
        }

        if (requestCode != RequestCode)
            return;
        var uri = resultCode == Result.Ok ? data?.Data : null;
        if (uri is not null)
        {
            try
            {
                global::Android.App.Application.Context.ContentResolver?.TakePersistableUriPermission(
                    uri, ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }
            catch (Java.Lang.SecurityException)
            {
            }
        }

        pending?.TrySetResult(uri);
        pending = null;
    }

    public static async Task WriteAsync(Context context, AndroidUri uri, byte[] content, CancellationToken cancellationToken)
    {
        await using var output = context.ContentResolver?.OpenOutputStream(uri, "wt")
            ?? throw new IOException($"Playlist kann nicht zum Schreiben geöffnet werden: {uri}");
        await output.WriteAsync(content, cancellationToken);
    }

    public static string? GetDisplayName(Context context, AndroidUri uri)
    {
        try
        {
            using ICursor? cursor = context.ContentResolver?.Query(uri, [IOpenableColumns.DisplayName], null, null, null);
            if (cursor is not null && cursor.MoveToFirst())
            {
                var name = GetString(cursor, IOpenableColumns.DisplayName);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
        }
        catch (Java.Lang.Exception)
        {
        }

        return uri.LastPathSegment;
    }

    public static string? GetPhysicalPath(Context context, AndroidUri uri)
    {
        if (uri.Scheme == "file")
            return uri.Path;

        if (DocumentsContract.IsDocumentUri(context, uri))
        {
            var documentId = DocumentsContract.GetDocumentId(uri) ?? string.Empty;
            switch (uri.Authority)
            {
                case "com.android.externalstorage.documents":
                {
                    var parts = documentId.Split(':', 2);
                    if (parts.Length == 2)
                    {
                        var root = parts[0].Equals("primary", StringComparison.OrdinalIgnoreCase)
                            ? AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath
                            : $"/storage/{parts[0]}";
                        return Path.Combine(root, parts[1]);
                    }
                    break;
                }
                case "com.android.providers.downloads.documents":
                    if (documentId.StartsWith("raw:", StringComparison.Ordinal))
                        return documentId[4..];
                    var idText = documentId.StartsWith("msf:", StringComparison.Ordinal) ? documentId[4..] : documentId;
                    if (long.TryParse(idText, out var downloadId))
                        return QueryMediaStorePath(context, downloadId);
                    break;
                case "com.android.providers.media.documents":
                {
                    var parts = documentId.Split(':', 2);
                    if (parts.Length == 2 && long.TryParse(parts[1], out var mediaId))
                        return QueryMediaStorePath(context, mediaId);
                    break;
                }
            }
        }

        return QueryPath(context, uri, null, null);
    }

    private static string? QueryMediaStorePath(Context context, long id) =>
        QueryPath(context, MediaStore.Files.GetContentUri("external")!, "_id=?", [id.ToString()]);

    private static string? QueryPath(Context context, AndroidUri uri, string? selection, string[]? args)
    {
        try
        {
            using ICursor? cursor = context.ContentResolver?.Query(
                uri,
                [MediaStore.IMediaColumns.RelativePath, MediaStore.IMediaColumns.DisplayName, "_data"],
                selection, args, null);
            if (cursor is null || !cursor.MoveToFirst())
                return null;

            var relativePath = GetString(cursor, MediaStore.IMediaColumns.RelativePath);
            var displayName = GetString(cursor, MediaStore.IMediaColumns.DisplayName);
            if (!string.IsNullOrEmpty(relativePath) && !string.IsNullOrEmpty(displayName))
                return Path.Combine(AndroidEnvironment.ExternalStorageDirectory!.AbsolutePath, relativePath, displayName);

            var data = GetString(cursor, "_data");
            return string.IsNullOrEmpty(data) ? null : data;
        }
        catch (Exception ex) when (ex is Java.Lang.Exception or ArgumentException)
        {
            return null;
        }
    }

    private static string? GetString(ICursor cursor, string column)
    {
        var index = cursor.GetColumnIndex(column);
        return index >= 0 && !cursor.IsNull(index) ? cursor.GetString(index) : null;
    }
}
