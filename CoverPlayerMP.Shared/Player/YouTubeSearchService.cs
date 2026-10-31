using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CoverPlayerMP.Shared.Player;

public enum YouTubeSearchMode
{
    DataApi,
    PublicSearch,
    SearchPage
}

public sealed record YouTubeSearchResult(string VideoId, string Title, string Channel, string Url);

public sealed class YouTubeSearchService
{
    private static readonly Regex VideoIdRegex = new("\"videoId\":\"(?<id>[A-Za-z0-9_-]{11})\"", RegexOptions.Compiled);
    private static readonly Regex TitleRegex = new("\"title\":\\{\"runs\":\\[\\{\"text\":\"(?<title>(?:\\\\.|[^\"])*)\"", RegexOptions.Compiled);
    private readonly HttpClient http;

    public YouTubeSearchService(HttpClient http)
    {
        this.http = http;
        this.http.Timeout = TimeSpan.FromSeconds(10);
        this.http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CoverPlayerMP", "1.0"));
    }

    public async Task<IReadOnlyList<YouTubeSearchResult>> SearchAsync(
        string artist, string title, YouTubeSearchMode mode, string? apiKey, CancellationToken cancellationToken = default)
    {
        var query = string.Join(" ", new[] { artist, title }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        if (query.Length == 0)
            return [];

        return mode switch
        {
            YouTubeSearchMode.DataApi => await SearchDataApiAsync(query, apiKey, cancellationToken),
            YouTubeSearchMode.PublicSearch => await SearchPublicAsync(query, cancellationToken),
            _ => []
        };
    }

    public static string SearchUrl(string artist, string title)
    {
        var query = string.Join(" ", new[] { artist, title }.Where(value => !string.IsNullOrWhiteSpace(value))).Trim();
        return $"https://www.youtube.com/results?search_query={Uri.EscapeDataString(query)}";
    }

    private async Task<IReadOnlyList<YouTubeSearchResult>> SearchDataApiAsync(
        string query, string? apiKey, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
            return [];

        var url = $"https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&maxResults=10&q={Uri.EscapeDataString(query)}&key={Uri.EscapeDataString(apiKey.Trim())}";
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var candidates = new List<YouTubeSearchResult>();

        if (!document.RootElement.TryGetProperty("items", out var items))
            return [];

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var id) || !id.TryGetProperty("videoId", out var videoId))
                continue;
            var value = videoId.GetString();
            if (string.IsNullOrWhiteSpace(value))
                continue;
            var snippet = item.GetProperty("snippet");
            candidates.Add(new(value, snippet.GetProperty("title").GetString() ?? value,
                snippet.GetProperty("channelTitle").GetString() ?? string.Empty,
                VideoUrl(value)));
        }

        if (candidates.Count == 0)
            return [];

        var ids = string.Join(',', candidates.Select(result => result.VideoId));
        var statusUrl = $"https://www.googleapis.com/youtube/v3/videos?part=status&id={ids}&key={Uri.EscapeDataString(apiKey.Trim())}";
        using var statusResponse = await http.GetAsync(statusUrl, cancellationToken);
        statusResponse.EnsureSuccessStatusCode();
        await using var statusStream = await statusResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var statusDocument = await JsonDocument.ParseAsync(statusStream, cancellationToken: cancellationToken);
        HashSet<string> embeddableIds = statusDocument.RootElement.TryGetProperty("items", out var statusItems)
            ? statusItems.EnumerateArray()
                .Where(item => item.TryGetProperty("status", out var status)
                    && status.TryGetProperty("embeddable", out var embeddable)
                    && embeddable.GetBoolean())
                .Select(item => item.GetProperty("id").GetString())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .ToHashSet(StringComparer.Ordinal)
            : new(StringComparer.Ordinal);

        return candidates.Where(result => embeddableIds.Contains(result.VideoId)).ToArray();
    }

    private async Task<IReadOnlyList<YouTubeSearchResult>> SearchPublicAsync(
        string query, CancellationToken cancellationToken)
    {
        var url = SearchUrl(string.Empty, query);
        using var response = await http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync(cancellationToken);
        var ids = VideoIdRegex.Matches(html).Select(match => match.Groups["id"].Value).Distinct().Take(10).ToArray();
        var titles = TitleRegex.Matches(html).Select(match => JsonUnescape(match.Groups["title"].Value)).ToArray();
        return ids.Select((id, index) => new YouTubeSearchResult(id,
            index < titles.Length && !string.IsNullOrWhiteSpace(titles[index]) ? titles[index] : id,
            string.Empty, VideoUrl(id))).ToArray();
    }

    private static string JsonUnescape(string value)
    {
        try
        {
            return JsonSerializer.Deserialize<string>($"\"{value}\"") ?? value;
        }
        catch (JsonException)
        {
            return value;
        }
    }

    private static string VideoUrl(string id) => $"https://www.youtube.com/watch?v={id}";
}
