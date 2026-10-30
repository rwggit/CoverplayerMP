using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP.Player;

// Zusätzliche Quellen für Playlists:
// - Playlist von einem WebDAV-/HTTP(S)-Server öffnen (relative Pfade werden gegen die Playlist-Adresse aufgelöst)
// - Fehlende Audiodateien einzeln auswählen (Android, Dateidialog mit Mehrfachauswahl)
public sealed partial class MauiPlaylistPlayer
{
    private Uri? serverBase;
    private string? serverUser;
    private string? serverPassword;
    private bool serverTrustCertificate;

    public bool SupportsServerPlaylist => true;

        // Listet Unterordner und Playlists (.m3u/.m3u8) eines WebDAV-Ordners auf.
        public async Task<IReadOnlyList<ServerEntry>> ListServerFolderAsync(string folderUrl, string? user, string? password, bool trustCertificate = false, CancellationToken cancellationToken = default)
        {
#if ANDROID
            if (global::Android.OS.Build.VERSION.SdkInt >=
                (global::Android.OS.BuildVersionCodes)36)
            {
                var permission = await Permissions.RequestAsync<CoverPlayerMP.Platforms.Android.LocalNetworkPermission>();
                if (permission != PermissionStatus.Granted)
                    throw new InvalidOperationException("Der Zugriff auf das lokale Netzwerk wurde nicht erlaubt.");
            }
#endif

            if (!Uri.TryCreate(folderUrl?.Trim(), UriKind.Absolute, out var parsed) ||
                parsed.Scheme is not ("http" or "https"))
                throw new InvalidOperationException(text.ErrorInvalidServerUrl);

            var builder = new UriBuilder(parsed);
            if (!builder.Path.EndsWith('/'))
                builder.Path += "/";
            var folder = builder.Uri;

            serverBase = folder;
            serverUser = string.IsNullOrWhiteSpace(user) ? null : user;
            serverPassword = password;
            serverTrustCertificate = trustCertificate;

            using var http = CreateHttpClientFor(folder);
            using var request = new HttpRequestMessage(new HttpMethod("PROPFIND"), folder);
            request.Headers.Add("Depth", "1");
            request.Content = new StringContent(
                "<?xml version=\"1.0\"?><d:propfind xmlns:d=\"DAV:\"><d:prop><d:resourcetype/></d:prop></d:propfind>",
                Encoding.UTF8, "application/xml");
            using var response = await http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(text.ErrorServerFile(folder.AbsoluteUri, (int)response.StatusCode));

            var document = System.Xml.Linq.XDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            System.Xml.Linq.XNamespace dav = "DAV:";
            var ownPath = Uri.UnescapeDataString(folder.AbsolutePath).TrimEnd('/');
            var result = new List<ServerEntry>();
            foreach (var item in document.Descendants(dav + "response"))
            {
                var href = item.Element(dav + "href")?.Value;
                if (string.IsNullOrEmpty(href))
                    continue;

                var url = new Uri(folder, href);
                var path = Uri.UnescapeDataString(url.AbsolutePath).TrimEnd('/');
                if (string.Equals(path, ownPath, StringComparison.Ordinal))
                    continue;

                var name = path[(path.LastIndexOf('/') + 1)..];
                var isFolder = item.Descendants(dav + "collection").Any();
                if (!isFolder && !name.EndsWith(".m3u", StringComparison.OrdinalIgnoreCase) &&
                    !name.EndsWith(".m3u8", StringComparison.OrdinalIgnoreCase))
                    continue;

                var entryUrl = isFolder ? url.AbsoluteUri.TrimEnd('/') + "/" : url.AbsoluteUri;
                result.Add(new ServerEntry(name, entryUrl, isFolder));
            }

            return result.OrderByDescending(e => e.IsFolder).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

    public IReadOnlyList<string> MissingFiles =>
        tracks.Where(IsRelativeSource)
              .Select(t => t.Source.Replace('\\', '/'))
              .ToList();

    private static bool IsRelativeSource(Track track) =>
        !Path.IsPathRooted(track.Source) && !track.Source.Contains("://", StringComparison.Ordinal);

    // Sendet Zugangsdaten nur an den Server, den der Anwender angegeben hat.
    private HttpClient CreateHttpClientFor(Uri url)
    {
        var handler = new SocketsHttpHandler();
        if (serverTrustCertificate && serverBase is not null &&
            string.Equals(url.Host, serverBase.Host, StringComparison.OrdinalIgnoreCase) && url.Port == serverBase.Port)
            handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        var http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        if (serverBase is not null && !string.IsNullOrEmpty(serverUser) &&
            string.Equals(url.Host, serverBase.Host, StringComparison.OrdinalIgnoreCase) &&
            url.Port == serverBase.Port)
        {
            var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{serverUser}:{serverPassword}"));
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        }

        return http;
    }

    private readonly Dictionary<string, string?> serverFolderCovers = new();

    private string ApplyCoverAndReturn(string source, string localFile)
    {
        ApplyDownloadedCover(source, localFile);
        return localFile;
    }

    // Titel vom Server (oder per content://): Das Cover kommt aus der heruntergeladenen Datei. Fehlt es dort,
    // wird cover.jpg/folder.jpg/front.jpg aus dem Serverordner geladen. Die Quelle bleibt unverändert,
    // nur CoverSource zeigt auf die lokale Datei.
    private void ApplyDownloadedCover(string source, string localFile)
    {
        var isHttp = PlaylistParser.IsHttpUrl(source);
        if (!isHttp && !source.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return;

        var coverFile = CoverImage.HasEmbeddedArtwork(localFile)
            ? localFile
            : (isHttp ? DownloadFolderCover(new Uri(source)) : null) ?? localFile;
        tracks = tracks
            .Select(t => string.Equals(t.Source, source, StringComparison.Ordinal) && t.CoverSource != coverFile
                ? t with { CoverSource = coverFile }
                : t)
            .ToList();
    }

    private string? DownloadFolderCover(Uri trackUrl)
    {
        var folder = new Uri(trackUrl, "./");
        var key = folder.AbsoluteUri;
        lock (serverFolderCovers)
            if (serverFolderCovers.TryGetValue(key, out var known))
                return known;

        string? result = null;
        foreach (var name in new[] { "cover.jpg", "folder.jpg", "front.jpg", "Cover.jpg", "Folder.jpg", "cover.png", "folder.png", "front.png" })
        {
            try
            {
                var url = new Uri(folder, name);
                using var http = CreateHttpClientFor(url);
                using var response = http.GetAsync(url).GetAwaiter().GetResult();
                if (!response.IsSuccessStatusCode)
                    continue;

                var bytes = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                if (bytes.Length == 0)
                    continue;

                var file = Path.Combine(FileSystem.CacheDirectory,
                    "cover_" + Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(key))) + Path.GetExtension(name));
                File.WriteAllBytes(file, bytes);
                result = file;
                break;
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
            {
            }
        }

        lock (serverFolderCovers)
            serverFolderCovers[key] = result;
        return result;
    }

    // Der Dateiserver für DLNA/Cast liefert nur lokale Dateien. Server- und content://-Quellen werden
    // deshalb vorher in den Cache geladen; das Gerät bekommt sie vom lokalen Dateiserver (ohne Zugangsdaten).
    private Track PrepareTrackForRenderer(Track track)
    {
        var source = track.Source;
        string local;
#if ANDROID
        local = ResolveAndroidSource(source);
#else
        local = PlaylistParser.IsHttpUrl(source) ? ApplyCoverAndReturn(source, DownloadToCache(source)) : source;
#endif
        if (string.Equals(local, source, StringComparison.Ordinal))
            return track;

        var updated = tracks.FirstOrDefault(t => string.Equals(t.Source, source, StringComparison.Ordinal)) ?? track;
        var cover = updated.CoverSource;
        if (string.IsNullOrEmpty(cover) || string.Equals(cover, source, StringComparison.Ordinal))
            cover = local;
        return updated with { Source = local, CoverSource = cover };
    }

    private string DownloadToCache(string source)
    {
        var url = new Uri(source);
        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(source)));
        var cacheFile = Path.Combine(FileSystem.CacheDirectory, "net_" + hash + Path.GetExtension(url.AbsolutePath));
        if (File.Exists(cacheFile))
            return cacheFile;

        var partFile = cacheFile + ".part";
        using (var http = CreateHttpClientFor(url))
        using (var response = http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult())
        {
            response.EnsureSuccessStatusCode();
            using var remote = response.Content.ReadAsStream();
            using var target = File.Create(partFile);
            remote.CopyTo(target);
        }

        File.Move(partFile, cacheFile, true);
        return cacheFile;
    }

    // Öffnet eine Playlist über ihre Adresse (WebDAV oder anderer HTTP(S)-Server). Die relativen Pfade
    // der Playlist werden gegen diese Adresse aufgelöst, die Lieder beim Abspielen vom Server geladen.
    public async Task OpenServerPlaylistAsync(string playlistUrl, string? user, string? password, bool trustCertificate = false, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(playlistUrl?.Trim(), UriKind.Absolute, out var parsed) ||
            parsed.Scheme is not ("http" or "https"))
        {
            SetError(text.ErrorInvalidServerUrl);
            return;
        }

        serverBase = parsed;
        serverUser = string.IsNullOrWhiteSpace(user) ? null : user;
        serverPassword = password;
        serverTrustCertificate = trustCertificate;

        await LoadPlaylistAsync(parsed.AbsoluteUri, false, cancellationToken);
        if (tracks.Count > 0)
            await PlayAsync(0, cancellationToken);
    }

    // Der Anwender wählt die fehlenden Audiodateien selbst aus. Zugeordnet wird über den Dateinamen.
    public async Task SelectAudioFilesAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        try
        {
            var files = await CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.PickAudioFilesAsync(playlistUri);
            if (files.Count == 0)
                return;

            var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                var name = CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.GetDisplayName(Platform.AppContext, file);
                var uri = file.ToString();
                if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(uri))
                    byName.TryAdd(name, uri);
            }

            tracks = tracks.Select(track =>
            {
                if (!IsRelativeSource(track))
                    return track;

                var fileName = Path.GetFileName(track.Source.Replace('\\', '/'));
                return byName.TryGetValue(fileName, out var match)
                    ? track with { Source = match, CoverSource = match }
                    : track;
            }).ToList();

            UpdateMusicFolderHint();
            var firstPlayable = tracks.FindIndex(t => !IsRelativeSource(t));
            if (currentIndex < 0 && firstPlayable >= 0)
                await PlayAsync(firstPlayable, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Java.Lang.SecurityException)
        {
            SetError(text.ErrorOpenPlaylist(ex.Message));
        }
#else
        await Task.CompletedTask;
#endif
    }
}
