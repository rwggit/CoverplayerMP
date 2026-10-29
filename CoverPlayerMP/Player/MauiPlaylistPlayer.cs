using System.Runtime.CompilerServices;
using System.Text.Json;
using Plugin.Maui.Audio;
using CoverPlayerMP.Shared.Player;
using CoverPlayerMP.Shared.Localization;

namespace CoverPlayerMP.Player;

public sealed partial class MauiPlaylistPlayer(AppText text) : IPlaylistPlayer, ICoverFileAccess
{
    private readonly ConditionalWeakTable<IAudioPlayer, Stream> playerStreams = new();
    private readonly IAudioManager audioManager = AudioManager.Current;
    private IAudioPlayer? audioPlayer;
    private CancellationTokenSource? monitorCancellation;
    private bool monitorRunning;
    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private long transitionVersion;
    private CancellationTokenSource? transitionRequestCancellation;
    private bool isDisposed;
    private List<Track> tracks = [];
    private string? playlistPath;
#if ANDROID
    private Android.Net.Uri? playlistUri;
#endif
    public bool IsPlaylistModified { get; private set; }
    public bool CanQuit => true;
    private int currentIndex = -1;
    private int? failedTransitionIndex;
    private int failedTransitionOffset;
    private double volume = 1;
    private double crossfadeSeconds = 2;

    public event EventHandler? StateChanged;
    public IReadOnlyList<Track> Tracks => tracks;
    public string? PlaylistPath => playlistPath;
    public Track? CurrentTrack => currentIndex >= 0 && currentIndex < tracks.Count ? tracks[currentIndex] : null;
    public int CurrentIndex => currentIndex;
    public double Position => audioPlayer?.CurrentPosition ?? 0;
    public double Duration => audioPlayer?.Duration ?? 0;
    public double Volume
    {
        get => volume;
        set
        {
            if (isDisposed)
                return;

            volume = Math.Clamp(value, 0, 1);
            if (audioPlayer is not null)
                audioPlayer.Volume = volume;
            RaiseStateChanged();
        }
    }
    public double CrossfadeSeconds
    {
        get => renderer is null ? crossfadeSeconds : 0;
        set => crossfadeSeconds = Math.Clamp(value, 0, 5);
    }
    public bool SupportsCrossfade => renderer is null;
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public string? ErrorMessage { get; private set; }
    public bool NeedsMusicFolder { get; private set; }
    public int LibraryTracksFound { get; private set; }
    public bool LibraryTracksLoadedFromIndex { get; private set; }


    public async Task OpenPlaylistAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        if (await Permissions.RequestAsync<CoverPlayerMP.Platforms.Android.ReadMediaAudioPermission>() != PermissionStatus.Granted)
        {
            SetError(text.ErrorNoAudioPermission);
            return;
        }

        try
        {
            var uri = await CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.PickAsync();
            if (uri is null)
                return;

            var path = CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.GetPhysicalPath(Platform.AppContext, uri);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                var name = CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.GetDisplayName(Platform.AppContext, uri);
                await using var input = Platform.AppContext.ContentResolver?.OpenInputStream(uri)
                    ?? throw new IOException(text.ErrorPlaylistLocation(uri.ToString() ?? string.Empty));
                using var reader = new StreamReader(input, PlaylistParser.GetEncoding(name), false);
                await LoadPlaylistAsync(await reader.ReadToEndAsync(cancellationToken), true, cancellationToken);
                // Bei Content-URIs lassen sich relative Pfade nicht aus der Dokument-ID ableiten.
                // Ein früher freigegebener Ordner wird still verwendet. Bleiben Titel unaufgelöst,
                // erscheint ein Hinweis, über den der Anwender den Musikordner wählt.
                var knownTree = CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.FindPersistedTree(Platform.AppContext, uri.Authority);
                if (knownTree is not null)
                    await ResolveWithFolderAsync(knownTree);
                UpdateMusicFolderHint();
            }
            else
                await LoadPlaylistAsync(path, false, cancellationToken);
            playlistUri = uri;
            var firstPlayable = tracks.FindIndex(t => !IsUnresolved(t));
            if (firstPlayable >= 0)
                await PlayAsync(firstPlayable, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException
                                       or Java.Lang.SecurityException)
        {
            SetError(text.ErrorOpenPlaylist(ex.Message));
        }
#else
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = text.PickPlaylistTitle,
            FileTypes = new FilePickerFileType(new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = [".m3u", ".m3u8"],
                [DevicePlatform.iOS] = ["public.playlist"],
            })
        });

        if (result is null)
            return;

        try
        {
            if (!string.IsNullOrWhiteSpace(result.FullPath) && File.Exists(result.FullPath))
            {
                await LoadPlaylistAsync(result.FullPath, false, cancellationToken);
            }
            else
            {
                await using var stream = await result.OpenReadAsync();
                using var reader = new StreamReader(stream, PlaylistParser.GetEncoding(result.FileName), false);
                await LoadPlaylistAsync(await reader.ReadToEndAsync(cancellationToken), true, cancellationToken);
            }

            if (tracks.Count > 0)
                await PlayAsync(0, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(text.ErrorOpenPlaylist(ex.Message));
        }
#endif
    }

    public async Task SelectMusicFolderAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        try
        {
            var tree = await CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.PickTreeAsync(playlistUri);
            if (tree is null)
                return;

            await ResolveWithFolderAsync(tree);
            UpdateMusicFolderHint();
            var firstPlayable = tracks.FindIndex(t => !IsUnresolved(t));
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

    public async Task LoadPlaylistAsync(string pathOrContent, bool isContent = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var isUrl = !isContent && PlaylistParser.IsHttpUrl(pathOrContent);
            string content;
            string? baseDirectory;
            if (isUrl)
            {
                using var http = CreateHttpClientFor(new Uri(pathOrContent));
                var bytes = await http.GetByteArrayAsync(pathOrContent, cancellationToken);
                content = PlaylistParser.GetEncoding(new Uri(pathOrContent).AbsolutePath).GetString(bytes);
                baseDirectory = pathOrContent[..(pathOrContent.IndexOfAny(['?', '#']) is var q and >= 0 ? q : pathOrContent.Length)];
                baseDirectory = baseDirectory[..baseDirectory.LastIndexOf('/')];
            }
            else
            {
                content = isContent ? pathOrContent : await File.ReadAllTextAsync(pathOrContent, PlaylistParser.GetEncoding(pathOrContent), cancellationToken);
                baseDirectory = isContent ? null : Path.GetDirectoryName(pathOrContent);
            }
            tracks = PlaylistParser.Parse(content, baseDirectory).ToList();
            playlistPath = isContent ? null : pathOrContent;
#if ANDROID
            playlistUri = null;
#endif
            IsPlaylistModified = false;
            currentIndex = -1;
            failedTransitionIndex = null;
            if (!isUrl)
                serverBase = null;
            NeedsMusicFolder = false;
            ErrorMessage = tracks.Count == 0 ? text.ErrorEmptyPlaylist : null;
            RaiseStateChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or HttpRequestException)
        {
            tracks = [];
            currentIndex = -1;
            SetError(text.ErrorReadPlaylist(ex.Message));
        }
    }

    public async Task PlayAsync(int index = 0, CancellationToken cancellationToken = default)
    {
        if (tracks.Count == 0)
            return;
        index = Math.Clamp(index, 0, tracks.Count - 1);
        var resuming = index == currentIndex && audioPlayer is not null && IsPaused;
        if (index != currentIndex)
            await LoadTrackAsync(index, cancellationToken);

        audioPlayer?.Play();
        if (resuming)
            await FadeToAsync(volume, cancellationToken);
        IsPlaying = true;
        IsPaused = false;
        StartMonitor();
        RaiseStateChanged();
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (audioPlayer is null || !IsPlaying)
            return;

        CancelTransitionRequest();
        await transitionLock.WaitAsync(cancellationToken);
        try
        {
            if (audioPlayer is null || !IsPlaying)
                return;

            await FadeToAsync(0, cancellationToken);
            audioPlayer.Pause();
#if ANDROID
            CoverPlayerMP.Platforms.Android.PlaybackService.Stop();
#endif
            IsPlaying = false;
            IsPaused = true;
            RaiseStateChanged();
        }
        finally
        {
            transitionLock.Release();
        }
    }

    public Task SeekAsync(double seconds, CancellationToken cancellationToken = default)
    {
        if (audioPlayer is null || Duration <= 0)
            return Task.CompletedTask;

        try
        {
            audioPlayer.Seek(Math.Clamp(seconds, 0, Duration));
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentOutOfRangeException)
        {
            SetError(text.ErrorSeek(ex.Message));
            return Task.CompletedTask;
        }

        RaiseStateChanged();
        return Task.CompletedTask;
    }

    public Task NextAsync(CancellationToken cancellationToken = default) =>
        tracks.Count == 0 ? Task.CompletedTask : TransitionByAsync(1, cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default) =>
        tracks.Count == 0 ? Task.CompletedTask : TransitionByAsync(-1, cancellationToken);

    public Task RestartAsync(CancellationToken cancellationToken = default) => SeekAsync(0, cancellationToken);

    public async Task<IReadOnlyList<LibraryTrack>> GetLibraryTracksAsync(CancellationToken cancellationToken = default, bool refresh = false)
    {
        try
        {
#if ANDROID
            if (await Permissions.RequestAsync<CoverPlayerMP.Platforms.Android.ReadMediaAudioPermission>() != PermissionStatus.Granted)
            {
                SetError(text.ErrorNoAudioPermission);
                return [];
            }

            LibraryTracksFound = 0;
            RaiseStateChanged();
            var libraryTracks = await Task.Run(CoverPlayerMP.Platforms.Android.AudioLibrary.Query, cancellationToken);
            LibraryTracksFound = libraryTracks.Count;
            RaiseStateChanged();
            return libraryTracks;
#else
            LibraryTracksFound = 0;
            LibraryTracksLoadedFromIndex = false;
            RaiseStateChanged();
            return await Task.Run(() => ScanMusicFolderAsync(cancellationToken, refresh), cancellationToken);
#endif
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(text.ErrorReadAudioFiles(ex.Message));
            return [];
        }
    }

#if !ANDROID
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".flac", ".aac", ".ogg", ".opus", ".wma"];
    private const int MetadataConcurrency = 8;
    private const int IndexThreshold = 1000;
    private const string IndexFileName = "CoverPlayerMP.index";

    private sealed record LibraryIndexEntry(string Source, string Title, string Artist, TimeSpan Duration);

    private async Task<IReadOnlyList<LibraryTrack>> ScanMusicFolderAsync(CancellationToken cancellationToken, bool refresh)
    {
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return [];

        var indexPath = Path.Combine(root, IndexFileName);
        if (!refresh && File.Exists(indexPath))
        {
            var indexedTracks = await LoadLibraryIndexAsync(indexPath, cancellationToken);
            if (indexedTracks is not null)
            {
                LibraryTracksFound = indexedTracks.Count;
                LibraryTracksLoadedFromIndex = true;
                RaiseStateChanged();
                return indexedTracks;
            }
        }

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        var files = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

            files.Add(file);
        }

        var result = new LibraryTrack?[files.Count];
        var completed = 0;
        var progressLock = new object();
        await Parallel.ForEachAsync(
            files.Select((file, index) => (file, index)),
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = MetadataConcurrency
            },
            async (item, token) =>
            {
                token.ThrowIfCancellationRequested();
                var file = item.file;

                var name = Path.GetFileNameWithoutExtension(file);
                var separator = name.IndexOf(" - ", StringComparison.Ordinal);
                var artist = separator > 0 ? name[..separator].Trim() : "Unbekannter Interpret";
                var title = separator > 0 ? name[(separator + 3)..].Trim() : name;
                var duration = TimeSpan.Zero;
#if WINDOWS
                try
                {
                    var storageFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(file);
                    var properties = await storageFile.Properties.GetMusicPropertiesAsync();
                    if (!string.IsNullOrWhiteSpace(properties.Artist))
                        artist = properties.Artist;
                    if (!string.IsNullOrWhiteSpace(properties.Title))
                        title = properties.Title;
                    duration = properties.Duration;
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception)
                {
                }
#else
                await Task.CompletedTask;
#endif
                result[item.index] = new LibraryTrack(new Track(file, title, artist, file), duration);
                var count = Interlocked.Increment(ref completed);
                if (count % 100 == 0)
                {
                    lock (progressLock)
                    {
                        LibraryTracksFound = count;
                        RaiseStateChanged();
                    }
                }
            });

        lock (progressLock)
        {
            LibraryTracksFound = completed;
            LibraryTracksLoadedFromIndex = false;
            RaiseStateChanged();
        }
        var tracks = result.Where(track => track is not null).Select(track => track!).ToList();
        if (tracks.Count > IndexThreshold)
            await SaveLibraryIndexAsync(indexPath, tracks, cancellationToken);
        else if (File.Exists(indexPath))
            File.Delete(indexPath);
        return tracks;
    }

    private static async Task<IReadOnlyList<LibraryTrack>?> LoadLibraryIndexAsync(string indexPath, CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = File.OpenRead(indexPath);
            var entries = await JsonSerializer.DeserializeAsync<List<LibraryIndexEntry>>(stream, cancellationToken: cancellationToken);
            if (entries is null)
                return null;

            return entries.Select(entry => new LibraryTrack(
                new Track(entry.Source, entry.Title, entry.Artist, entry.Source), entry.Duration)).ToList();
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static async Task SaveLibraryIndexAsync(string indexPath, IReadOnlyList<LibraryTrack> tracks, CancellationToken cancellationToken)
    {
        var temporaryPath = indexPath + ".tmp";
        var entries = tracks.Select(track => new LibraryIndexEntry(
            track.Track.Source, track.Track.Title, track.Track.Artist, track.Duration)).ToList();
        try
        {
            await using (var stream = File.Create(temporaryPath))
                await JsonSerializer.SerializeAsync(stream, entries, cancellationToken: cancellationToken);
            File.Move(temporaryPath, indexPath, true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }
#endif

    public async Task InsertTrackAsync(Track track, TrackInsertPosition position, CancellationToken cancellationToken = default)
    {
        if (tracks.Count == 0)
        {
            tracks = [track];
            playlistPath = null;
#if ANDROID
            playlistUri = null;
#endif
            IsPlaylistModified = true;
            currentIndex = -1;
            failedTransitionIndex = null;
            ErrorMessage = null;
            await PlayAsync(0, cancellationToken);
            return;
        }

        var current = Math.Max(currentIndex, 0);
        var index = position switch
        {
            TrackInsertPosition.Start => 0,
            TrackInsertPosition.BeforeCurrent => current,
            TrackInsertPosition.AfterCurrent or TrackInsertPosition.AfterCurrentAndPlay => currentIndex < 0 ? 0 : current + 1,
            _ => tracks.Count
        };

        tracks.Insert(index, track);
        IsPlaylistModified = true;
        if (currentIndex >= 0 && index <= currentIndex)
            currentIndex++;
        failedTransitionIndex = null;
        RaiseStateChanged();

        if (position == TrackInsertPosition.AfterCurrentAndPlay)
        {
            if (currentIndex < 0 || audioPlayer is null || !IsPlaying)
                await PlayAsync(index, cancellationToken);
            else
                await NextAsync(cancellationToken);
        }
    }

    public Task PlayWithCrossfadeAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= tracks.Count)
            return Task.CompletedTask;
        if (index == currentIndex && audioPlayer is not null)
            return IsPlaying ? RestartAsync(cancellationToken) : PlayAsync(index, cancellationToken);
        return TransitionByAsync(0, cancellationToken, index);
    }

    public void MoveTrack(int fromIndex, int toIndex)
    {
        if (fromIndex < 0 || fromIndex >= tracks.Count)
            return;
        toIndex = Math.Clamp(toIndex, 0, tracks.Count - 1);
        if (fromIndex == toIndex)
            return;

        var track = tracks[fromIndex];
        tracks.RemoveAt(fromIndex);
        tracks.Insert(toIndex, track);
        if (currentIndex == fromIndex)
            currentIndex = toIndex;
        else if (fromIndex < currentIndex && toIndex >= currentIndex)
            currentIndex--;
        else if (fromIndex > currentIndex && toIndex <= currentIndex)
            currentIndex++;
        failedTransitionIndex = null;
        IsPlaylistModified = true;
        RaiseStateChanged();
    }

    public async Task RemoveTrackAsync(int index, CancellationToken cancellationToken = default)
    {
        if (index < 0 || index >= tracks.Count)
            return;

        if (index == currentIndex)
        {
            if (index + 1 < tracks.Count)
                await TransitionByAsync(0, cancellationToken, index + 1);
            else
                Stop();
        }

        if (index >= tracks.Count)
            return;
        tracks.RemoveAt(index);
        if (index < currentIndex)
            currentIndex--;
        else if (index == currentIndex)
            currentIndex = -1;
        failedTransitionIndex = null;
        IsPlaylistModified = true;
        RaiseStateChanged();
    }

    private void Stop()
    {
        CancelTransitionRequest();
        monitorCancellation?.Cancel();
        try
        {
            audioPlayer?.Stop();
            audioPlayer?.Dispose();
        }
        catch (Exception)
        {
        }
        audioPlayer = null;
#if ANDROID
        CoverPlayerMP.Platforms.Android.PlaybackService.Stop();
#endif
        currentIndex = -1;
        IsPlaying = false;
        IsPaused = false;
    }

    public async Task<string?> SavePlaylistAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var suggestedName = string.IsNullOrEmpty(playlistPath)
                ? $"CoverPlayer-{DateTime.Now:yyyyMMdd-HHmmss}.m3u"
                : Path.GetFileName(playlistPath);

#if ANDROID
            var uri = await CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.PickSaveLocationAsync(suggestedName, playlistUri);
            if (uri is null)
                return null;

            var target = CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.GetPhysicalPath(Platform.AppContext, uri);
            var fileName = target ?? CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.GetDisplayName(Platform.AppContext, uri) ?? suggestedName;
            await CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.WriteAsync(
                Platform.AppContext, uri, PlaylistParser.WriteBytes(tracks, fileName, Path.GetDirectoryName(target)), cancellationToken);
            playlistUri = uri;
            target ??= fileName;
#elif WINDOWS
            var target = await PickWindowsSaveLocationAsync(suggestedName);
            if (target is null)
                return null;

            await File.WriteAllTextAsync(target, PlaylistParser.Write(tracks, Path.GetDirectoryName(target)), PlaylistParser.GetEncoding(target), cancellationToken);
#else
            var folder = string.IsNullOrEmpty(playlistPath) || playlistPath.StartsWith(FileSystem.CacheDirectory, StringComparison.OrdinalIgnoreCase)
                ? FileSystem.AppDataDirectory
                : Path.GetDirectoryName(playlistPath)!;
            var target = Path.Combine(folder, suggestedName);
            await File.WriteAllTextAsync(target, PlaylistParser.Write(tracks, folder), PlaylistParser.GetEncoding(target), cancellationToken);
#endif
            playlistPath = target;
            IsPlaylistModified = false;
            RaiseStateChanged();
            return target;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(text.ErrorSavePlaylist(ex.Message));
            return null;
        }
    }

#if WINDOWS
    private async Task<string?> PickWindowsSaveLocationAsync(string suggestedName)
    {
        var picker = new Windows.Storage.Pickers.FileSavePicker
        {
            SuggestedFileName = Path.GetFileNameWithoutExtension(suggestedName),
            SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.MusicLibrary
        };
        var extension = Path.GetExtension(suggestedName);
        if (string.Equals(extension, ".m3u8", StringComparison.OrdinalIgnoreCase))
        {
            picker.FileTypeChoices.Add("M3U8-Playlist (UTF-8)", [".m3u8"]);
            picker.FileTypeChoices.Add("M3U-Playlist", [".m3u"]);
        }
        else
        {
            picker.FileTypeChoices.Add("M3U-Playlist", [".m3u"]);
            picker.FileTypeChoices.Add("M3U8-Playlist (UTF-8)", [".m3u8"]);
        }

        if (!string.IsNullOrEmpty(playlistPath) && File.Exists(playlistPath) &&
            !playlistPath.StartsWith(FileSystem.CacheDirectory, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                picker.SuggestedSaveFile = await Windows.Storage.StorageFile.GetFileFromPathAsync(playlistPath);
            }
            catch (Exception)
            {
            }
        }

        var window = Application.Current?.Windows.FirstOrDefault()?.Handler?.PlatformView as Microsoft.UI.Xaml.Window
            ?? throw new InvalidOperationException("Kein aktives Fenster.");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(window));

        var file = await picker.PickSaveFileAsync();
        return file?.Path;
    }
#endif

    public void Quit()
    {
        // Zuerst Monitor, Transitions und Events stoppen, damit nach dem Schließen
        // des WebViews keine Rendering-/JS-Aufrufe mehr ausgelöst werden.
        _ = DisposeAsync();
        // Dem Blazor-Renderer Zeit geben, Komponenten abzumelden, bevor das WebView zerstört wird.
        _ = Task.Delay(200).ContinueWith(_ =>
            MainThread.BeginInvokeOnMainThread(() => Application.Current?.Quit()),
            TaskScheduler.Default);
    }

    private static bool SourceExists(string source) =>
        PlaylistParser.IsHttpUrl(source) || source.StartsWith("content://", StringComparison.OrdinalIgnoreCase) || File.Exists(source);

#if ANDROID
    private static bool IsUnresolved(Track track) =>
        !Path.IsPathRooted(track.Source) && !track.Source.Contains("://", StringComparison.Ordinal);

    private async Task ResolveWithFolderAsync(global::Android.Net.Uri tree)
    {
        var snapshot = tracks.ToList();
        tracks = await Task.Run(() => ResolveTracksInFolder(tree, snapshot));
    }

    private void UpdateMusicFolderHint()
    {
        var unresolved = tracks.Count(IsUnresolved);
        NeedsMusicFolder = unresolved > 0;
        if (NeedsMusicFolder)
            ErrorMessage = text.MissingTracksHint(unresolved);
        else if (tracks.Count > 0)
            ErrorMessage = null;
        RaiseStateChanged();
    }

    private static List<(string Id, string Name)> QueryChildren(
        global::Android.Net.Uri tree, string parentId, Dictionary<string, List<(string Id, string Name)>> cache)
    {
        if (cache.TryGetValue(parentId, out var cached))
            return cached;

        var result = new List<(string Id, string Name)>();
        try
        {
            var childrenUri = global::Android.Provider.DocumentsContract.BuildChildDocumentsUriUsingTree(tree, parentId);
            using var cursor = childrenUri is null ? null : Platform.AppContext.ContentResolver?.Query(
                childrenUri,
                [global::Android.Provider.DocumentsContract.Document.ColumnDocumentId,
                 global::Android.Provider.DocumentsContract.Document.ColumnDisplayName],
                null, null, null);
            if (cursor is not null)
            {
                while (cursor.MoveToNext())
                {
                    var childId = cursor.GetString(0);
                    var name = cursor.GetString(1);
                    if (childId is not null && name is not null)
                        result.Add((childId, name));
                }
            }
        }
        catch (Java.Lang.Exception)
        {
        }

        cache[parentId] = result;
        return result;
    }

    // Sucht die Audiodateien der Playlist im gewählten Ordner. Die relativen Pfade werden ab dem
    // gewählten Ordner aufgelöst; führende ".." und "." werden ignoriert. Liegt der gewählte Ordner
    // tiefer als erwartet, werden führende Pfadsegmente schrittweise weggelassen.
    private static List<Track> ResolveTracksInFolder(global::Android.Net.Uri tree, IReadOnlyList<Track> source)
    {
        var rootId = global::Android.Provider.DocumentsContract.GetTreeDocumentId(tree);
        if (rootId is null)
            return source.ToList();

        var cache = new Dictionary<string, List<(string Id, string Name)>>();
        return source.Select(track =>
        {
            if (!IsUnresolved(track))
                return track;

            var segments = track.Source.Split('/', '\\')
                .Where(s => s is not ("" or "." or ".."))
                .ToList();
            for (var skip = 0; skip < segments.Count; skip++)
            {
                var id = FindDocumentId(tree, rootId, segments.Skip(skip), cache);
                if (id is null)
                    continue;

                var uri = global::Android.Provider.DocumentsContract.BuildDocumentUriUsingTree(tree, id)?.ToString();
                if (uri is not null)
                    return track with { Source = uri, CoverSource = uri };
            }

            return track;
        }).ToList();
    }

    private static string? FindDocumentId(
        global::Android.Net.Uri tree, string startId, IEnumerable<string> segments,
        Dictionary<string, List<(string Id, string Name)>> cache)
    {
        var current = startId;
        foreach (var segment in segments)
        {
            var children = QueryChildren(tree, current, cache);
            var match = children.FirstOrDefault(x => string.Equals(x.Name, segment, StringComparison.Ordinal));
            if (match.Id is null)
                match = children.FirstOrDefault(x => string.Equals(x.Name, segment, StringComparison.OrdinalIgnoreCase));
            if (match.Id is null)
                return null;

            current = match.Id;
        }

        return current;
    }

    private string ResolveAndroidSource(string source)
    {
        if (PlaylistParser.IsHttpUrl(source))
            return ApplyCoverAndReturn(source, DownloadToCache(source));
        return ResolveContentSource(source);
    }

    private static string ResolveContentSource(string source)
    {
        if (!source.StartsWith("content://", StringComparison.OrdinalIgnoreCase))
            return source;

        var uri = global::Android.Net.Uri.Parse(source)!;
        var extension = Path.GetExtension(CoverPlayerMP.Platforms.Android.PlaylistDocumentPicker.GetDisplayName(Platform.AppContext, uri) ?? string.Empty);
        var cacheFile = Path.Combine(FileSystem.CacheDirectory, "doc_" + Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(System.Text.Encoding.UTF8.GetBytes(source))) + extension);
        if (File.Exists(cacheFile))
            return cacheFile;

        using var input = OpenContent(uri, source);
        using var output = File.Create(cacheFile);
        input.CopyTo(output);
        return cacheFile;
    }

    private static Stream OpenContent(global::Android.Net.Uri uri, string source)
    {
        try
        {
            return Platform.AppContext.ContentResolver?.OpenInputStream(uri)
                ?? throw new FileNotFoundException(source, source);
        }
        catch (Java.Lang.SecurityException ex)
        {
            throw new UnauthorizedAccessException(ex.Message, ex);
        }
    }
#endif

    private async Task LoadTrackAsync(int index, CancellationToken cancellationToken)
    {
        var source = tracks[index].Source;
        if (!SourceExists(source))
            throw new FileNotFoundException(text.ErrorFileNotFound(source), source);

        var next = await CreatePlayerAsync(source, cancellationToken);

        monitorCancellation?.Cancel();
        if (audioPlayer is not null)
        {
            audioPlayer.Stop();
            audioPlayer.Dispose();
        }

        audioPlayer = next;
        audioPlayer.Volume = 0;
        currentIndex = index;
        await FadeToAsync(volume, cancellationToken);
        RaiseStateChanged();
    }

    private Task<IAudioPlayer> CreatePlayerAsync(string source, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            if (renderer is not null)
            {
                var index = tracks.FindIndex(x => string.Equals(x.Source, source, StringComparison.OrdinalIgnoreCase));
                var track = index >= 0 ? tracks[index] : new Track(source, Path.GetFileNameWithoutExtension(source), string.Empty);
                track = PrepareTrackForRenderer(track);
                return (IAudioPlayer)new CoverPlayerMP.Player.Remote.RemoteAudioPlayer(renderer, fileServer, track, volume);
            }

#if ANDROID
            var androidPlayer = new CoverPlayerMP.Platforms.Android.AndroidFilePlayer(ResolveAndroidSource(source));
            androidPlayer.SetPreferredDevice(preferredDevice);
            return (IAudioPlayer)androidPlayer;
#else
            Stream stream;
            if (PlaylistParser.IsHttpUrl(source))
            {
                // Im persistenten Cache ablegen (statt einer flüchtigen DeleteOnClose-Datei), damit ID3-Tags
                // und Cover auch nach dem Abspielen aus der lokalen Datei gelesen werden können.
                var cacheFile = DownloadToCache(source);
                ApplyDownloadedCover(source, cacheFile);
                stream = File.OpenRead(cacheFile);
            }
            else
                stream = File.OpenRead(source);
            var player = audioManager.CreatePlayer(stream);
            playerStreams.Add(player, stream);
            return player;
#endif
        }, cancellationToken);

    private async Task TransitionByAsync(int offset, CancellationToken cancellationToken, int? targetIndex = null)
    {
        var transitionCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var previousRequest = Interlocked.Exchange(
            ref transitionRequestCancellation,
            transitionCancellation);
        previousRequest?.Cancel();
        var version = Interlocked.Increment(ref transitionVersion);

        var lockTaken = false;
        var index = -1;
        try
        {
            await transitionLock.WaitAsync(transitionCancellation.Token);
            lockTaken = true;
            transitionCancellation.Token.ThrowIfCancellationRequested();
            var baseIndex = failedTransitionIndex is { } failed && failedTransitionOffset == offset
                ? failed
                : currentIndex;
            index = targetIndex is { } target && target >= 0 && target < tracks.Count
                ? target
                : (baseIndex + offset + tracks.Count) % tracks.Count;

            if (index == currentIndex && audioPlayer is not null)
            {
                failedTransitionIndex = null;
                await RestartAsync(transitionCancellation.Token);
                return;
            }

            if (audioPlayer is not null && currentIndex >= 0 &&
                string.Equals(tracks[currentIndex].Source, tracks[index].Source, StringComparison.OrdinalIgnoreCase))
            {
                ReleasePlayer(audioPlayer);
                audioPlayer = null;
                IsPlaying = false;
                IsPaused = false;
                await LoadTrackAsync(index, transitionCancellation.Token);
                OnTransitionSucceeded();
                audioPlayer?.Play();
                IsPlaying = true;
                IsPaused = false;
                StartMonitor();
                RaiseStateChanged();
                return;
            }

            if (audioPlayer is null || !IsPlaying)
            {
                await LoadTrackAsync(index, transitionCancellation.Token);
                OnTransitionSucceeded();
                audioPlayer?.Play();
                IsPlaying = true;
                IsPaused = false;
                StartMonitor();
                RaiseStateChanged();
                return;
            }

            await CrossfadeToAsync(index, transitionCancellation.Token, version);
            OnTransitionSucceeded();
        }
        catch (OperationCanceledException) when (transitionCancellation.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (!isDisposed)
        {
            if (index >= 0 && index < tracks.Count)
            {
                failedTransitionIndex = index;
                failedTransitionOffset = offset;
                var track = tracks[index];
                var name = string.IsNullOrWhiteSpace(track.Title)
                    ? Path.GetFileName(track.Source)
                    : track.Title;
                SetError(text.ErrorCannotPlay(name, ex.Message));
            }
            else
            {
                SetError(text.ErrorTransition(ex.Message));
            }
        }
        finally
        {
            if (ReferenceEquals(transitionRequestCancellation, transitionCancellation))
                Interlocked.CompareExchange(
                    ref transitionRequestCancellation,
                    null,
                    transitionCancellation);
            if (lockTaken)
                transitionLock.Release();
            transitionCancellation.Dispose();
        }
    }

    private void OnTransitionSucceeded()
    {
        failedTransitionIndex = null;
        ErrorMessage = null;
    }

    private void StartMonitor()
    {
        if (monitorRunning)
            return;

        monitorCancellation?.Cancel();
        monitorCancellation = new CancellationTokenSource();
        monitorRunning = true;
        _ = MonitorAsync(monitorCancellation.Token);
    }

    private async Task MonitorAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                if (audioPlayer is not null &&
                    IsPlaying &&
                    Duration > 0 &&
                    Position >= Duration - Math.Max(.2, CrossfadeSeconds))
                {
                    await TransitionByAsync(1, token);
                }
                RaiseStateChanged();
                await Task.Delay(250, token);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) when (isDisposed)
        {
        }
        catch (Exception ex)
        {
            SetError(text.ErrorMonitor(ex.Message));
        }
        finally
        {
            monitorRunning = false;
        }
    }

    private async Task CrossfadeToAsync(int nextIndex, CancellationToken token, long version)
    {
        var oldPlayer = audioPlayer;
        if (oldPlayer is null)
        {
            await LoadTrackAsync(nextIndex, token);
            audioPlayer?.Play();
            IsPlaying = true;
            IsPaused = false;
            return;
        }

        var nextSource = tracks[nextIndex].Source;
        if (!SourceExists(nextSource))
            throw new FileNotFoundException(text.ErrorFileNotFound(nextSource), nextSource);

        var next = await CreatePlayerAsync(nextSource, token);
        next.Volume = 0;
        next.Play();
        audioPlayer = next;
        currentIndex = nextIndex;
        IsPlaying = true;
        IsPaused = false;
        RaiseStateChanged();

        try
        {
            var duration = Math.Max(.05, CrossfadeSeconds);
            for (var step = 0; step < 20; step++)
            {
                token.ThrowIfCancellationRequested();
                if (version != Volatile.Read(ref transitionVersion))
                    throw new OperationCanceledException(token);
                var progress = (step + 1) / 20d;
                var oldGain = 1 - Math.Pow(progress, 2);
                var newGain = Math.Pow(progress, 2);
                oldPlayer.Volume = volume * oldGain;
                next.Volume = volume * newGain;
                RaiseStateChanged();
                await Task.Delay(TimeSpan.FromSeconds(duration / 20), token);
            }

            next.Volume = volume;
            _ = Task.Run(() =>
            {
                try
                {
                    oldPlayer.Stop();
                    oldPlayer.Dispose();
                }
                catch (Exception)
                {
                }
            });
            RaiseStateChanged();
        }
        catch (OperationCanceledException)
        {
            try
            {
                oldPlayer.Stop();
                oldPlayer.Dispose();
            }
            catch (Exception) when (isDisposed)
            {
            }

            if (isDisposed)
            {
                try
                {
                    next.Stop();
                    next.Dispose();
                }
                catch (Exception)
                {
                }
                audioPlayer = null;
                IsPlaying = false;
                IsPaused = false;
            }
            else
            {
                next.Volume = volume;
                RaiseStateChanged();
            }

            throw;
        }
    }

    private async Task FadeToAsync(double target, CancellationToken token)
    {
        if (audioPlayer is null)
            return;
        var start = audioPlayer.Volume;
        var duration = Math.Max(.05, CrossfadeSeconds);
        for (var step = 0; step < 20; step++)
        {
            audioPlayer.Volume = start + (target - start) * ((step + 1) / 20d);
            await Task.Delay(TimeSpan.FromSeconds(duration / 20), token);
        }
    }

    public ValueTask DisposeAsync()
    {
        isDisposed = true;
        CancelTransitionRequest();
        monitorCancellation?.Cancel();
        try
        {
            audioPlayer?.Stop();
            audioPlayer?.Dispose();
        }
        catch (Exception)
        {
        }
        monitorCancellation?.Dispose();
        DisposeRemote();
        return ValueTask.CompletedTask;
    }

    private void ReleasePlayer(IAudioPlayer player)
    {
        try
        {
            player.Stop();
            player.Dispose();
        }
        catch (Exception)
        {
        }

        if (playerStreams.TryGetValue(player, out var stream))
        {
            try
            {
                stream.Dispose();
            }
            catch (Exception)
            {
            }
            playerStreams.Remove(player);
        }
    }

    public async Task RunWithReleasedFileAsync(string source, Func<Task> action, CancellationToken cancellationToken = default)
    {
        bool IsCurrent() =>
            audioPlayer is not null && currentIndex >= 0 && currentIndex < tracks.Count &&
            string.Equals(tracks[currentIndex].Source, source, StringComparison.OrdinalIgnoreCase);

        if (!IsCurrent())
        {
            await action();
            return;
        }

        CancelTransitionRequest();
        await transitionLock.WaitAsync(cancellationToken);
        try
        {
            var player = audioPlayer;
            if (player is null || !IsCurrent())
            {
                await action();
                return;
            }

            var position = player.CurrentPosition;
            var wasPlaying = IsPlaying;
            var wasPaused = IsPaused;

            // Ohne Ausblendung stoppen und Datei freigeben.
            monitorCancellation?.Cancel();
            monitorRunning = false;
            audioPlayer = null;
            IsPlaying = false;
            ReleasePlayer(player);

            try
            {
                await action();
            }
            finally
            {
                if (!isDisposed && SourceExists(source))
                {
                    var next = await CreatePlayerAsync(source, CancellationToken.None);
                    next.Volume = wasPaused ? 0 : volume; // ohne Einblendung
                    audioPlayer = next;
                    try
                    {
                        if (position > 0 && position < next.Duration)
                            next.Seek(position);
                    }
                    catch (Exception)
                    {
                    }

                    if (wasPlaying)
                    {
                        next.Play();
                        IsPlaying = true;
                        IsPaused = false;
                        StartMonitor();
                    }
                    else
                    {
                        IsPaused = wasPaused;
                    }
                    RaiseStateChanged();
                }
            }
        }
        finally
        {
            transitionLock.Release();
        }
    }

    private void CancelTransitionRequest()
    {
        var request = transitionRequestCancellation;
        if (request is null)
            return;

        try
        {
            request.Cancel();
        }
        catch (ObjectDisposedException)
        {
            Interlocked.CompareExchange(ref transitionRequestCancellation, null, request);
        }
    }

    private void SetError(string message)
    {
        if (isDisposed)
            return;

        ErrorMessage = message;
        RaiseStateChanged();
    }

    private void RaiseStateChanged()
    {
        if (isDisposed)
            return;

        try
        {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (ObjectDisposedException) when (isDisposed)
        {
        }
    }
}
