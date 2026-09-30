using Android.Provider;
using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP.Platforms.Android;

public static class AudioLibrary
{
    public static IReadOnlyList<LibraryTrack> Query()
    {
        var result = new List<LibraryTrack>();
        var resolver = Platform.AppContext.ContentResolver;
        var uri = MediaStore.Audio.Media.ExternalContentUri;
        if (resolver is null || uri is null)
            return result;

#pragma warning disable CA1422
        string[] projection =
        [
            MediaStore.Audio.Media.InterfaceConsts.Data!,
            MediaStore.Audio.Media.InterfaceConsts.Title!,
            MediaStore.Audio.Media.InterfaceConsts.Artist!,
            MediaStore.Audio.Media.InterfaceConsts.Duration!
        ];
#pragma warning restore CA1422

        using var cursor = resolver.Query(uri, projection, null, null, null);
        if (cursor is null)
            return result;

        while (cursor.MoveToNext())
        {
            var path = cursor.GetString(0);
            if (string.IsNullOrWhiteSpace(path))
                continue;

            var title = cursor.GetString(1);
            var artist = cursor.GetString(2);
            if (string.IsNullOrWhiteSpace(title))
                title = System.IO.Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(artist) || artist == "<unknown>")
                artist = "Unbekannter Interpret";

            result.Add(new LibraryTrack(
                new Track(path, title, artist, path),
                TimeSpan.FromMilliseconds(cursor.GetLong(3))));
        }

        return result;
    }
}
