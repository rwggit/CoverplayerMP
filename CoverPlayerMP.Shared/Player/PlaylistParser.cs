namespace CoverPlayerMP.Shared.Player;

public static class PlaylistParser
{
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

            var source = line;
            if (!Uri.TryCreate(source, UriKind.Absolute, out var uri) && baseDirectory is not null)
                source = Path.GetFullPath(Path.Combine(baseDirectory, source));

            var title = pendingInfo ?? pendingTitle ?? Path.GetFileNameWithoutExtension(source);
            var separator = title.IndexOf(" - ", StringComparison.Ordinal);
            var artist = separator > 0 ? title[..separator].Trim() : "Unbekannter Interpret";
            var trackTitle = separator > 0 ? title[(separator + 3)..].Trim() : title;
            result.Add(new Track(source, trackTitle, artist, source));
            pendingInfo = null;
            pendingTitle = null;
        }

        return result;
    }

    private static bool IsTrackSource(string line)
    {
        if (Uri.TryCreate(line, UriKind.Absolute, out var uri) &&
            (uri.IsFile || uri.Scheme is "http" or "https"))
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

    public static string Write(IEnumerable<Track> tracks)
    {
        var builder = new System.Text.StringBuilder("#EXTM3U\n");
        foreach (var track in tracks)
        {
            builder.Append("#EXTINF:-1,").Append(track.Artist).Append(" - ").Append(track.Title).Append('\n');
            builder.Append(track.Source).Append('\n');
        }

        return builder.ToString();
    }
}
