using System.Text.Json;

namespace CoverPlayerMP.Shared.Player;

public enum MetadataField
{
    Title,
    Artist,
    AlbumArtist,
    Album,
    Year,
    TrackNumber,
    DiscNumber,
    Genre,
    Composer
}

public sealed record MusicBrainzMetadata(
    IReadOnlyDictionary<MetadataField, string> Values,
    string RecordingTitle,
    string? CoverUrl);

public sealed record AcoustIdMatch(string RecordingId, double Score);

public sealed record AudioFingerprint(string Fingerprint, int DurationSeconds);

public sealed class FingerprintCreationException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);

public sealed class AcoustIdApiException(string message, bool apiKeyProblem = false)
    : InvalidOperationException(message)
{
    public bool IsApiKeyProblem { get; } = apiKeyProblem;
}

public sealed class MusicBrainzService
{
    private readonly HttpClient http;
    private readonly IAudioFingerprintProvider fingerprintProvider;

    public MusicBrainzService(HttpClient http, IAudioFingerprintProvider fingerprintProvider)
    {
        this.http = http;
        this.fingerprintProvider = fingerprintProvider;
        this.http.Timeout = TimeSpan.FromSeconds(10);
        this.http.DefaultRequestHeaders.UserAgent.Clear();
        this.http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "CoverPlayerMP/1.0 (+https://github.com/rwggit/CoverplayerMP)");
    }

    public async Task<MusicBrainzMetadata?> FindAsync(string artist, string title, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(artist) || string.IsNullOrWhiteSpace(title))
            return null;

        var query = $"artist:\"{artist.Trim()}\" AND recording:\"{title.Trim()}\"";
        var url = $"https://musicbrainz.org/ws/2/recording/?query={Uri.EscapeDataString(query)}&fmt=json&limit=5";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (!document.RootElement.TryGetProperty("recordings", out var recordings) || recordings.GetArrayLength() == 0)
            return null;

        var recording = recordings[0];
        var values = new Dictionary<MetadataField, string>();
        Add(values, MetadataField.Title, GetString(recording, "title"));
        Add(values, MetadataField.Artist, ReadArtistCredit(recording));

        string? coverUrl = null;
        if (recording.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
        {
            var release = releases[0];
            Add(values, MetadataField.Album, GetString(release, "title"));
            if (release.TryGetProperty("date", out var date))
                Add(values, MetadataField.Year, date.GetString()?.Split('-', 2)[0]);
            var releaseId = GetString(release, "id");
            if (!string.IsNullOrWhiteSpace(releaseId))
                coverUrl = $"https://coverartarchive.org/release/{releaseId}/front-500";
        }

        if (recording.TryGetProperty("genres", out var genres) && genres.GetArrayLength() > 0)
            Add(values, MetadataField.Genre, genres[0].TryGetProperty("name", out var genre) ? genre.GetString() : null);

        return new MusicBrainzMetadata(values, GetString(recording, "title") ?? title, coverUrl);
    }

    public async Task<byte[]?> DownloadCoverAsync(string url, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return null;
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<AudioFingerprint> CreateFingerprintAsync(
        string path, CancellationToken cancellationToken = default)
    {
        try
        {
            return await fingerprintProvider.CreateAsync(path, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new FingerprintCreationException(ex.Message, ex);
        }
    }

    public async Task<IReadOnlyList<AcoustIdMatch>> FindAcoustIdMatchesAsync(
        AudioFingerprint fingerprint, string apiKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new AcoustIdApiException("Für AcoustID ist ein API-Key erforderlich.", apiKeyProblem: true);

        var url = $"https://api.acoustid.org/v2/lookup?client={Uri.EscapeDataString(apiKey.Trim())}" +
                  $"&meta=recordings+releasegroups+releases&duration={fingerprint.DurationSeconds}" +
                  $"&fingerprint={Uri.EscapeDataString(fingerprint.Fingerprint)}";
        using var response = await http.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var apiKeyProblem = response.StatusCode is System.Net.HttpStatusCode.BadRequest
                or System.Net.HttpStatusCode.Unauthorized
                or System.Net.HttpStatusCode.Forbidden;
            throw new AcoustIdApiException(
                $"AcoustID antwortete mit HTTP {(int)response.StatusCode}.", apiKeyProblem);
        }
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        if (document.RootElement.TryGetProperty("status", out var status) &&
            !string.Equals(status.GetString(), "ok", StringComparison.OrdinalIgnoreCase))
        {
            var errorMessage = document.RootElement.TryGetProperty("error", out var error) &&
                               error.TryGetProperty("message", out var message)
                ? message.GetString()
                : null;
            throw new AcoustIdApiException(
                errorMessage ?? "AcoustID hat die Anfrage abgelehnt.",
                errorMessage?.Contains("client", StringComparison.OrdinalIgnoreCase) == true ||
                errorMessage?.Contains("key", StringComparison.OrdinalIgnoreCase) == true);
        }
        if (!document.RootElement.TryGetProperty("results", out var results))
            return [];

        return results.EnumerateArray()
            .SelectMany(result =>
            {
                var score = result.TryGetProperty("score", out var scoreElement) ? scoreElement.GetDouble() : 0;
                return result.TryGetProperty("recordings", out var recordings)
                    ? recordings.EnumerateArray()
                        .Select(recording => new AcoustIdMatch(GetString(recording, "id") ?? string.Empty, score))
                    : [];
            })
            .Where(match => !string.IsNullOrWhiteSpace(match.RecordingId))
            .GroupBy(match => match.RecordingId, StringComparer.Ordinal)
            .Select(group => group.OrderByDescending(match => match.Score).First())
            .OrderByDescending(match => match.Score)
            .Take(5)
            .ToArray();
    }

    public async Task<MusicBrainzMetadata?> FindByRecordingIdAsync(
        string recordingId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(recordingId))
            return null;

        var url = $"https://musicbrainz.org/ws/2/recording/{Uri.EscapeDataString(recordingId)}?inc=artists+releases+genres&fmt=json";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return ReadMetadata(document.RootElement, document.RootElement.TryGetProperty("title", out var title) ? title.GetString() ?? string.Empty : string.Empty);
    }

    private static MusicBrainzMetadata ReadMetadata(JsonElement recording, string fallbackTitle)
    {
        var values = new Dictionary<MetadataField, string>();
        Add(values, MetadataField.Title, GetString(recording, "title"));
        Add(values, MetadataField.Artist, ReadArtistCredit(recording));

        string? coverUrl = null;
        if (recording.TryGetProperty("releases", out var releases) && releases.GetArrayLength() > 0)
        {
            var release = releases[0];
            Add(values, MetadataField.Album, GetString(release, "title"));
            if (release.TryGetProperty("date", out var date))
                Add(values, MetadataField.Year, date.GetString()?.Split('-', 2)[0]);
            var releaseId = GetString(release, "id");
            if (!string.IsNullOrWhiteSpace(releaseId))
                coverUrl = $"https://coverartarchive.org/release/{releaseId}/front-500";
        }

        if (recording.TryGetProperty("genres", out var genres) && genres.GetArrayLength() > 0)
            Add(values, MetadataField.Genre, genres[0].TryGetProperty("name", out var genre) ? genre.GetString() : null);

        return new MusicBrainzMetadata(values, GetString(recording, "title") ?? fallbackTitle, coverUrl);
    }

    private static string? ReadArtistCredit(JsonElement recording)
    {
        if (!recording.TryGetProperty("artist-credit", out var credits))
            return null;

        var names = credits.EnumerateArray()
            .Select(credit => credit.TryGetProperty("name", out var name)
                ? name.GetString()
                : credit.TryGetProperty("artist", out var artist) && artist.TryGetProperty("name", out var artistName)
                    ? artistName.GetString()
                    : null)
            .Where(name => !string.IsNullOrWhiteSpace(name));
        return string.Join(", ", names);
    }

    private static string? GetString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) ? value.GetString() : null;

    private static void Add(Dictionary<MetadataField, string> values, MetadataField field, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            values[field] = value.Trim();
    }
}
