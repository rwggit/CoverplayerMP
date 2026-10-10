namespace CoverPlayerMP.Shared.Player;

public static class PlaylistParser
{
    public static System.Text.Encoding Encoding { get; } = CreateEncoding();

    private static System.Text.Encoding CreateEncoding()
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        return System.Text.Encoding.GetEncoding(1252);
    }

    public static System.Text.Encoding GetEncoding(string? fileName) =>
        string.Equals(Path.GetExtension(fileName), ".m3u8", StringComparison.OrdinalIgnoreCase)
            ? new System.Text.UTF8Encoding(false)
            : Encoding;

    public static byte[] WriteBytes(IEnumerable<Track> tracks, string? fileName = null, string? targetDirectory = null) =>
        GetEncoding(fileName).GetBytes(Write(tracks, targetDirectory));

    public static IReadOnlyList<Track> Parse(string content, string? baseDirectory = null)
    {
        var result = new List<Track>();
        string? pendingInfo = null;
        string? pendingTitle = null;

        foreach (var rawLine in content.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.Trim().TrimEnd('\r');
            if (line.StartsWith("#EXTINF:", StringComparison.OrdinalIgnoreCase))
            {
                pendingInfo = line[(line.IndexOf(',') + 1)..].Trim();
                continue;
            }

            if (line.StartsWith('#') || line.Length == 0)
                continue;

            if (!IsTrackSource(line))
            {
                pendingTitle = line;
                continue;
            }

            var source = NormalizeSeparators(line);
            var isRelative = !Uri.TryCreate(source, UriKind.Absolute, out var uri) && !Path.IsPathRooted(source);
            if (isRelative && baseDirectory is not null)
                source = IsHttpUrl(baseDirectory)
                    ? new Uri(new Uri(baseDirectory.TrimEnd('/') + "/"), line.Replace('\\', '/')).ToString()
                    : Path.GetFullPath(Path.Combine(baseDirectory, source));
            else
                isRelative = false;

            var title = pendingInfo ?? pendingTitle ?? Path.GetFileNameWithoutExtension(source);
            var separator = title.IndexOf(" - ", StringComparison.Ordinal);
            var artist = separator > 0 ? title[..separator].Trim() : "Unbekannter Interpret";
            var trackTitle = separator > 0 ? title[(separator + 3)..].Trim() : title;
            result.Add(new Track(source, trackTitle, artist, source, isRelative));
            pendingInfo = null;
            pendingTitle = null;
        }

        return result;
    }

    public static bool IsHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    private static string NormalizeSeparators(string path)
    {
        // Web-Adressen nicht anfassen – dort ist '/' Teil der Adresse
        if (Uri.TryCreate(path, UriKind.Absolute, out var uri) && !uri.IsFile)
            return path;

        return path.Replace('\\', Path.DirectorySeparatorChar)
                   .Replace('/', Path.DirectorySeparatorChar);
    }

    private static bool IsTrackSource(string line)
    {
        if (Uri.TryCreate(line, UriKind.Absolute, out var uri) &&
            (uri.IsFile || uri.Scheme is "http" or "https" or "smb" or "nfs"))
            return true;

        var extension = Path.GetExtension(line);
        return extension.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".m4a", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".flac", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".aac", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".ogg", StringComparison.OrdinalIgnoreCase) ||
               extension.Equals(".opus", StringComparison.OrdinalIgnoreCase);
    }

    public static string Write(IEnumerable<Track> tracks, string? targetDirectory = null)
    {
        var builder = new System.Text.StringBuilder("#EXTM3U\n");
        foreach (var track in tracks)
        {
            builder.Append("#EXTINF:-1,").Append(track.Artist).Append(" - ").Append(track.Title).Append('\n');
            builder.Append(GetEntryPath(track, targetDirectory)).Append('\n');
        }

        return builder.ToString();
    }

    private static string GetEntryPath(Track track, string? targetDirectory)
    {
        if (!track.IsRelativePath || string.IsNullOrEmpty(targetDirectory) || !Path.IsPathRooted(track.Source))
            return track.Source;

        var relative = Path.GetRelativePath(targetDirectory, track.Source);
        return Path.IsPathRooted(relative) ? track.Source : relative;
    }
}
