using CoverPlayerMP.Shared.Localization;

namespace CoverPlayerMP.Shared.Player;

public static class TrackMetadata
{
    public static IReadOnlyDictionary<MetadataField, string> ReadEditableValues(string? path)
    {
        var result = new Dictionary<MetadataField, string>();
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return result;

        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        Add(result, MetadataField.Title, tag.Title);
        Add(result, MetadataField.Artist, string.Join("; ", tag.Performers));
        Add(result, MetadataField.AlbumArtist, string.Join("; ", tag.AlbumArtists));
        Add(result, MetadataField.Album, tag.Album);
        Add(result, MetadataField.Year, tag.Year == 0 ? null : tag.Year.ToString());
        Add(result, MetadataField.TrackNumber, tag.Track == 0 ? null : tag.TrackCount == 0 ? tag.Track.ToString() : $"{tag.Track}/{tag.TrackCount}");
        Add(result, MetadataField.DiscNumber, tag.Disc == 0 ? null : tag.DiscCount == 0 ? tag.Disc.ToString() : $"{tag.Disc}/{tag.DiscCount}");
        Add(result, MetadataField.Genre, string.Join("; ", tag.Genres));
        Add(result, MetadataField.Composer, string.Join("; ", tag.Composers));
        return result;
    }

    public static byte[]? ReadFirstPictureBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            using var file = TagLib.File.Create(path);
            return file.Tag.Pictures.FirstOrDefault()?.Data?.Data;
        }
        catch (Exception) when (File.Exists(path))
        {
            return null;
        }
    }

    public static void WriteEditableValues(string path, IReadOnlyDictionary<MetadataField, string> values)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new FileNotFoundException("Die Audiodatei wurde nicht gefunden.", path);

        using var file = TagLib.File.Create(path);
        var tag = file.Tag;
        foreach (var (field, value) in values)
        {
            if (string.IsNullOrWhiteSpace(value))
                continue;

            var trimmed = value.Trim();
            switch (field)
            {
                case MetadataField.Title: tag.Title = trimmed; break;
                case MetadataField.Artist: tag.Performers = SplitValues(trimmed); break;
                case MetadataField.AlbumArtist: tag.AlbumArtists = SplitValues(trimmed); break;
                case MetadataField.Album: tag.Album = trimmed; break;
                case MetadataField.Year when uint.TryParse(trimmed, out var year): tag.Year = year; break;
                case MetadataField.TrackNumber: SetTrackNumber(tag, trimmed); break;
                case MetadataField.DiscNumber: SetDiscNumber(tag, trimmed); break;
                case MetadataField.Genre: tag.Genres = SplitValues(trimmed); break;
                case MetadataField.Composer: tag.Composers = SplitValues(trimmed); break;
            }
        }
        file.Save();
    }

    private static void Add(Dictionary<MetadataField, string> values, MetadataField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[field] = value.Trim();
    }

    private static string[] SplitValues(string value) => value.Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

    private static void SetTrackNumber(TagLib.Tag tag, string value)
    {
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (uint.TryParse(parts[0], out var number)) tag.Track = number;
        if (parts.Length > 1 && uint.TryParse(parts[1], out var count)) tag.TrackCount = count;
    }

    private static void SetDiscNumber(TagLib.Tag tag, string value)
    {
        var parts = value.Split('/', 2, StringSplitOptions.TrimEntries);
        if (uint.TryParse(parts[0], out var number)) tag.Disc = number;
        if (parts.Length > 1 && uint.TryParse(parts[1], out var count)) tag.DiscCount = count;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> Read(string? path, AppText text)
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

        Add(text.MetaFile, path);
        FileInfo info;
        try
        {
            if (!File.Exists(path))
                return result;
            info = new FileInfo(path);
        }
        catch (Exception ex)
        {
            Add(text.MetaError, ex.Message);
            return result;
        }

        Add(text.MetaFileSize, $"{info.Length / 1024d / 1024d:0.00} MB");
        Add(text.MetaModified, info.LastWriteTime.ToString("g"));

        try
        {
            using var file = TagLib.File.Create(path);
            var tag = file.Tag;
            Add(text.Title, tag.Title);
            Add(text.Artist, tag.Performers);
            Add(text.MetaAlbumArtist, tag.AlbumArtists);
            Add(text.MetaAlbum, tag.Album);
            Add(text.MetaYear, tag.Year);
            Add(text.MetaTrackNumber, tag.Track == 0 ? null : tag.TrackCount > 0 ? $"{tag.Track}/{tag.TrackCount}" : tag.Track.ToString());
            Add(text.MetaDisc, tag.Disc == 0 ? null : tag.DiscCount > 0 ? $"{tag.Disc}/{tag.DiscCount}" : tag.Disc.ToString());
            Add(text.MetaGenre, tag.Genres);
            Add(text.MetaComposer, tag.Composers);
            Add(text.MetaConductor, tag.Conductor);
            Add(text.MetaGrouping, tag.Grouping);
            Add("BPM", tag.BeatsPerMinute);
            Add(text.MetaComment, tag.Comment);
            Add("Copyright", tag.Copyright);
            Add(text.MetaLyrics, tag.Lyrics);
            Add(text.CoverPictures, tag.Pictures.Length);
            Add(text.MetaTagTypes, file.TagTypes.ToString());

            var props = file.Properties;
            if (props is not null)
            {
                Add(text.MetaDuration, props.Duration);
                Add("Codec", props.Description);
                Add("Bitrate", props.AudioBitrate == 0 ? null : $"{props.AudioBitrate} kbit/s");
                Add(text.MetaSampleRate, props.AudioSampleRate == 0 ? null : $"{props.AudioSampleRate} Hz");
                Add(text.MetaChannels, props.AudioChannels);
                Add(text.MetaBitsPerSample, props.BitsPerSample);
            }
        }
        catch (Exception ex)
        {
            Add(text.MetaMetadata, text.MetaUnreadable(ex.Message));
        }

        return result;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ReadRawTags(string? path, AppText text)
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
            Add(text.MetaError, ex.Message);
        }

        return result;
    }

    public static IReadOnlyList<KeyValuePair<string, string>> ReadPictures(string? path)
    {
        var result = new List<KeyValuePair<string, string>>();
        if (string.IsNullOrWhiteSpace(path))
            return result;

        try
        {
            using var file = TagLib.File.Create(path);
            foreach (var picture in file.Tag.Pictures)
            {
                var data = picture.Data?.Data;
                if (data is null || data.Length == 0)
                    continue;

                var mime = string.IsNullOrWhiteSpace(picture.MimeType) ? "image/jpeg" : picture.MimeType;
                var resolution = string.Empty;
                try
                {
                    using var codec = SkiaSharp.SKCodec.Create(new MemoryStream(data));
                    if (codec is not null)
                        resolution = $"{codec.Info.Width} × {codec.Info.Height} px, ";
                }
                catch (Exception)
                {
                }
                var caption = $"{picture.Type}, {mime}, {resolution}{data.Length / 1024d:0.0} KB";
                if (!string.IsNullOrWhiteSpace(picture.Description))
                    caption = $"{picture.Description} ({caption})";
                result.Add(new(caption, $"data:{mime};base64,{Convert.ToBase64String(data)}"));
            }
        }
        catch (Exception)
        {
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
