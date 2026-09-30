namespace CoverPlayerMP.Shared.Player;

public static class TrackMetadata
{
    public static IReadOnlyList<KeyValuePair<string, string>> Read(string? path)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(path))
            return result;

        void Add(string name, object? value)
        {
            var text = value switch
            {
                null => null,
                string s => s,
                string[] a => string.Join("; ", a.Where(x => !string.IsNullOrWhiteSpace(x))),
                uint u when u == 0 => null,
                int i when i == 0 => null,
                TimeSpan t when t == TimeSpan.Zero => null,
                TimeSpan t => t.ToString(t.TotalHours >= 1 ? @"h\:mm\:ss" : @"m\:ss"),
                _ => value.ToString()
            };
            if (!string.IsNullOrWhiteSpace(text))
                result.Add(new(name, text.Trim()));
        }

        Add("Datei", path);
        FileInfo info;
        try
        {
            if (!File.Exists(path))
                return result;
            info = new FileInfo(path);
        }
        catch (Exception ex)
        {
            Add("Fehler", ex.Message);
            return result;
        }

        Add("Dateigröße", $"{info.Length / 1024d / 1024d:0.00} MB");
        Add("Geändert", info.LastWriteTime.ToString("g"));

        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            Add("Titel", tag.Title);
            Add("Interpret", tag.Performers);
            Add("Album-Interpret", tag.AlbumArtists);
            Add("Album", tag.Album);
            Add("Jahr", tag.Year);
            Add("Titelnummer", tag.Track == 0 ? null : tag.TrackCount > 0 ? $"{tag.Track}/{tag.TrackCount}" : tag.Track.ToString());
            Add("CD", tag.Disc == 0 ? null : tag.DiscCount > 0 ? $"{tag.Disc}/{tag.DiscCount}" : tag.Disc.ToString());
            Add("Genre", tag.Genres);
            Add("Komponist", tag.Composers);
            Add("Dirigent", tag.Conductor);
            Add("Gruppierung", tag.Grouping);
            Add("BPM", tag.BeatsPerMinute);
            Add("Kommentar", tag.Comment);
            Add("Copyright", tag.Copyright);
            Add("Liedtext", tag.Lyrics);
            Add("Cover-Bilder", tag.Pictures.Length);
            Add("Tag-Typen", file.TagTypes.ToString());

            var props = file.Properties;
            if (props is not null)
            {
                Add("Spieldauer", props.Duration);
                Add("Codec", props.Description);
                Add("Bitrate", props.AudioBitrate == 0 ? null : $"{props.AudioBitrate} kbit/s");
                Add("Abtastrate", props.AudioSampleRate == 0 ? null : $"{props.AudioSampleRate} Hz");
                Add("Kanäle", props.AudioChannels);
                Add("Bits pro Sample", props.BitsPerSample);
            }
        }
        catch (Exception ex)
        {
            Add("Metadaten", $"Nicht lesbar: {ex.Message}");
        }

        return result;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ReadRawTags(string? path)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(path))
            return result;

        void Add(string name, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                result.Add(new(name, value.Trim()));
        }

        try
        {
            using var file = TagLib.File.Create(path);
            foreach (var type in Enum.GetValues<TagLib.TagTypes>())
            {
                if (type is TagLib.TagTypes.None or TagLib.TagTypes.AllTags ||
                    !file.TagTypes.HasFlag(type) ||
                    System.Numerics.BitOperations.PopCount((uint)type) != 1)
                    continue;

                var tag = file.GetTag(type, false);
                if (tag is null)
                    continue;

                var prefix = type.ToString();
                switch (tag)
                {
                    case TagLib.Id3v2.Tag id3:
                        foreach (var frame in id3)
                            Add($"{prefix} · {DescribeFrame(frame)}", FrameValue(frame));
                        break;
                    case TagLib.Ogg.XiphComment xiph:
                        foreach (var field in xiph)
                            Add($"{prefix} · {field}", string.Join("; ", xiph.GetField(field)));
                        break;
                    case TagLib.Ape.Tag ape:
                        foreach (var key in ape)
                            Add($"{prefix} · {key}", ape.GetItem(key)?.ToString());
                        break;
                    default:
                        foreach (var property in typeof(TagLib.Tag).GetProperties())
                        {
                            if (property.GetIndexParameters().Length > 0 ||
                                property.Name is "TagTypes" or "IsEmpty" or "Pictures" ||
                                property.Name.StartsWith("First", StringComparison.Ordinal) ||
                                property.Name.StartsWith("Joined", StringComparison.Ordinal))
                                continue;

                            var value = property.GetValue(tag) switch
                            {
                                null => null,
                                string[] a => string.Join("; ", a),
                                uint u when u == 0 => null,
                                double d when d == 0 || double.IsNaN(d) => null,
                                object o => o.ToString()
                            };
                            Add($"{prefix} · {property.Name}", value);
                        }
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            Add("Fehler", ex.Message);
        }

        return result;
    }

    private static string DescribeFrame(TagLib.Id3v2.Frame frame)
    {
        var id = frame.FrameId.ToString();
        return frame switch
        {
            TagLib.Id3v2.UserTextInformationFrame u when !string.IsNullOrEmpty(u.Description) => $"{id} ({u.Description})",
            TagLib.Id3v2.CommentsFrame c when !string.IsNullOrEmpty(c.Description) => $"{id} ({c.Description})",
            TagLib.Id3v2.PrivateFrame p => $"{id} ({p.Owner})",
            _ => id
        };
    }

    private static string? FrameValue(TagLib.Id3v2.Frame frame) => frame switch
    {
        TagLib.Id3v2.UserTextInformationFrame u => string.Join("; ", u.Text),
        TagLib.Id3v2.TextInformationFrame t => string.Join("; ", t.Text),
        TagLib.Id3v2.AttachmentFrame a => $"{a.Type}, {a.MimeType}, {a.Data.Count / 1024d:0.0} KB",
        TagLib.Id3v2.PrivateFrame p => $"{p.PrivateData?.Count ?? 0} Byte",
        TagLib.Id3v2.UnknownFrame x => $"{x.Data?.Count ?? 0} Byte",
        _ => frame.ToString()
    };
}
