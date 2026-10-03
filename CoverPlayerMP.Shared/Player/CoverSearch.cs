using TagLib;

namespace CoverPlayerMP.Shared.Player;

/// <summary>
/// Unterstützt die Coversuche: Google-Bildersuche öffnen, Änderung von Cover.jpg im
/// Downloadordner erkennen und das Bild in die Audiodatei übernehmen.
/// </summary>
public sealed class CoverSearch
{
    private const string FileName = "Cover.jpg";
    private DateTime baselineUtc;

    public static string DownloadsFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static string CoverPath => Path.Combine(DownloadsFolder, FileName);

    /// <summary>Merkt den aktuellen Stand von Cover.jpg und liefert die Google-Bildersuch-URL (große Bilder).</summary>
    public string Begin(string? artist, string? title)
    {
        baselineUtc = System.IO.File.Exists(CoverPath)
            ? System.IO.File.GetLastWriteTimeUtc(CoverPath)
            : DateTime.UtcNow;
        var query = Uri.EscapeDataString($"{artist} {title} cover".Trim());
        // tbm=isch: Bildersuche, tbs=isz:l: Größe "Groß"
        return $"https://www.google.com/search?tbm=isch&tbs=isz:l&q={query}";
    }

    /// <summary>Wartet, bis Cover.jpg neu oder geändert ist. Liefert die Bytes oder null bei Timeout.</summary>
    public async Task<byte[]?> WaitForNewCoverAsync(TimeSpan timeout, CancellationToken token)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (System.IO.File.Exists(CoverPath) &&
                    System.IO.File.GetLastWriteTimeUtc(CoverPath) > baselineUtc)
                {
                    await Task.Delay(500, token); // Download abschließen lassen
                    return await System.IO.File.ReadAllBytesAsync(CoverPath, token);
                }
            }
            catch (IOException)
            {
                // Datei noch in Benutzung, nächster Versuch
            }
            await Task.Delay(1000, token);
        }
        return null;
    }

    /// <summary>Wandelt WebP (und andere Nicht-JPEG-Formate) in JPEG um; JPEG bleibt unverändert.</summary>
    public static byte[] EnsureJpeg(byte[] image)
    {
        if (image.Length >= 3 && image[0] == 0xFF && image[1] == 0xD8 && image[2] == 0xFF)
            return image;

        using var bitmap = SkiaSharp.SKBitmap.Decode(image)
            ?? throw new InvalidOperationException("Das Bild konnte nicht gelesen werden.");
        using var flattened = new SkiaSharp.SKBitmap(bitmap.Width, bitmap.Height, SkiaSharp.SKColorType.Rgba8888, SkiaSharp.SKAlphaType.Opaque);
        using (var canvas = new SkiaSharp.SKCanvas(flattened))
        {
            canvas.Clear(SkiaSharp.SKColors.White);
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        using var data = SkiaSharp.SKImage.FromBitmap(flattened).Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 92);
        return data.ToArray();
    }

    /// <summary>Ersetzt das eingebettete Cover der Audiodatei durch das übergebene Bild.</summary>
    public static void Apply(string audioFile, byte[] image)
    {
        image = EnsureJpeg(image);
        using var file = TagLib.File.Create(audioFile);
        file.Tag.Pictures =
        [
            new Picture(new ByteVector(image))
            {
                Type = PictureType.FrontCover,
                MimeType = "image/jpeg"
            }
        ];
        file.Save();
    }
}
