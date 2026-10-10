using SkiaSharp;

namespace CoverPlayerMP.Shared.Player;

public static class CoverImage
{
    public static string? ToDataUri(string? path, int maxSize = 1200)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return null;

        var imageBytes = IsImageFile(path)
            ? System.IO.File.ReadAllBytes(path)
            : ReadEmbeddedArtwork(path) ?? ReadExternalArtwork(path);
        if (imageBytes is null)
            return null;

        using var input = new MemoryStream(imageBytes);
        using var bitmap = SKBitmap.Decode(input);
        if (bitmap is null)
            return null;

        var scale = Math.Min(1d, Math.Min((double)maxSize / bitmap.Width, (double)maxSize / bitmap.Height));
        var width = Math.Max(1, (int)Math.Round(bitmap.Width * scale));
        var height = Math.Max(1, (int)Math.Round(bitmap.Height * scale));
        using var resized = bitmap.Resize(new SKImageInfo(width, height),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        using var image = SKImage.FromBitmap(resized ?? bitmap);
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return $"data:image/jpeg;base64,{Convert.ToBase64String(encoded.ToArray())}";
    }

    public static bool HasEmbeddedArtwork(string path) => ReadEmbeddedArtwork(path) is not null;

    // Liefert eine Bilddatei für das Cover: die Datei selbst, wenn sie ein Bild ist, sonst das eingebettete
    // oder ein benachbartes Cover, als Datei im angegebenen Ordner abgelegt (z. B. für DLNA-Geräte).
    public static string? GetCoverImageFile(string? path, string cacheDirectory)
    {
        if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
            return null;
        if (IsImageFile(path))
            return path;

        var bytes = ReadEmbeddedArtwork(path) ?? ReadExternalArtwork(path);
        if (bytes is null || bytes.Length == 0)
            return null;

        var extension = bytes.Length > 4 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47
            ? ".png"
            : ".jpg";
        var key = path + "|" + new FileInfo(path).Length;
        var name = "dlna_cover_" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(key))) + extension;
        var target = Path.Combine(cacheDirectory, name);
        if (!System.IO.File.Exists(target))
            System.IO.File.WriteAllBytes(target, bytes);
        return target;
    }

    private static bool IsImageFile(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is
            ".jpg" or ".jpeg" or ".png" or ".webp" or ".tif" or ".tiff" or ".gif" or ".bmp";

    private static byte[]? ReadEmbeddedArtwork(string path)
    {
        try
        {
            using var file = TagLib.File.Create(path);
            return file.Tag.Pictures.FirstOrDefault()?.Data.Data;
        }
        catch (Exception ex) when (ex is IOException ||
                                   ex is UnauthorizedAccessException ||
                                   ex is InvalidOperationException ||
                                   ex is TagLib.CorruptFileException)
        {
            return null;
        }
    }

    private static byte[]? ReadExternalArtwork(string audioPath)
    {
        var directory = Path.GetDirectoryName(audioPath);
        if (directory is null)
            return null;

        foreach (var name in new[]
        {
            "cover.jpg", "cover.jpeg", "cover.png", "cover.webp", "cover.tif", "cover.tiff", "cover.gif",
            "folder.jpg", "folder.jpeg", "folder.png", "folder.webp", "folder.tif", "folder.tiff", "folder.gif",
            "front.jpg", "front.jpeg", "front.png", "front.webp", "front.tif", "front.tiff", "front.gif"
        })
        {
            var path = Path.Combine(directory, name);
            if (System.IO.File.Exists(path))
                return System.IO.File.ReadAllBytes(path);
        }

        return null;
    }
}
