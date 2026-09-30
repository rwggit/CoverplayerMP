using Plugin.Maui.Audio;
using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP.Player;

public sealed class MauiPlaylistPlayer : IPlaylistPlayer
{
    private readonly IAudioManager audioManager = AudioManager.Current;
    private IAudioPlayer? audioPlayer;
    private CancellationTokenSource? monitorCancellation;
    private bool monitorRunning;
    private readonly SemaphoreSlim transitionLock = new(1, 1);
    private long transitionVersion;
    private CancellationTokenSource? transitionRequestCancellation;
    private bool isDisposed;
    private List<Track> tracks = [];
    private int currentIndex = -1;
    private int? failedTransitionIndex;
    private int failedTransitionOffset;
    private double volume = 1;
    private double crossfadeSeconds = 2;

    public event EventHandler? StateChanged;
    public IReadOnlyList<Track> Tracks => tracks;
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
        get => crossfadeSeconds;
        set => crossfadeSeconds = Math.Clamp(value, 0, 5);
    }
    public bool IsPlaying { get; private set; }
    public bool IsPaused { get; private set; }
    public string? ErrorMessage { get; private set; }

    public async Task OpenPlaylistAsync(CancellationToken cancellationToken = default)
    {
#if ANDROID
        if (await Permissions.RequestAsync<CoverPlayerMP.Platforms.Android.ReadMediaAudioPermission>() != PermissionStatus.Granted)
        {
            SetError("Ohne Berechtigung für Audiodateien können die Titel nicht gelesen werden.");
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
                SetError($"Der Speicherort der Playlist konnte nicht ermittelt werden: {uri}");
                return;
            }

            await LoadPlaylistAsync(path, false, cancellationToken);
            if (tracks.Count > 0)
                await PlayAsync(0, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError($"Playlist konnte nicht geöffnet werden: {ex.Message}");
        }
#else
        var result = await FilePicker.Default.PickAsync(new PickOptions
        {
            PickerTitle = "M3U-Playlist auswählen",
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
                using var reader = new StreamReader(stream);
                await LoadPlaylistAsync(await reader.ReadToEndAsync(cancellationToken), true, cancellationToken);
            }

            if (tracks.Count > 0)
                await PlayAsync(0, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError($"Playlist konnte nicht geöffnet werden: {ex.Message}");
        }
#endif
    }

    public async Task LoadPlaylistAsync(string pathOrContent, bool isContent = false, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = isContent ? pathOrContent : await File.ReadAllTextAsync(pathOrContent, cancellationToken);
            var baseDirectory = isContent ? null : Path.GetDirectoryName(pathOrContent);
            tracks = PlaylistParser.Parse(content, baseDirectory).ToList();
            currentIndex = -1;
            failedTransitionIndex = null;
            ErrorMessage = tracks.Count == 0 ? "Die Playlist enthält keine abspielbaren Titel." : null;
            RaiseStateChanged();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            tracks = [];
            currentIndex = -1;
            SetError($"Playlist konnte nicht gelesen werden: {ex.Message}");
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
            SetError($"Die Abspielposition konnte nicht geändert werden: {ex.Message}");
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

    public async Task<IReadOnlyList<LibraryTrack>> GetLibraryTracksAsync(CancellationToken cancellationToken = default)
    {
        try
        {
#if ANDROID
            if (await Permissions.RequestAsync<CoverPlayerMP.Platforms.Android.ReadMediaAudioPermission>() != PermissionStatus.Granted)
            {
                SetError("Ohne Berechtigung für Audiodateien können die Titel nicht gelesen werden.");
                return [];
            }

            return await Task.Run(CoverPlayerMP.Platforms.Android.AudioLibrary.Query, cancellationToken);
#else
            return await Task.Run(() => ScanMusicFolderAsync(cancellationToken), cancellationToken);
#endif
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError($"Audiodateien konnten nicht gelesen werden: {ex.Message}");
            return [];
        }
    }

#if !ANDROID
    private static readonly string[] AudioExtensions = [".mp3", ".wav", ".m4a", ".flac", ".aac", ".ogg", ".opus", ".wma"];

    private static async Task<IReadOnlyList<LibraryTrack>> ScanMusicFolderAsync(CancellationToken cancellationToken)
    {
        var result = new List<LibraryTrack>();
        var root = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic);
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            return result;

        var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true };
        foreach (var file in Directory.EnumerateFiles(root, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!AudioExtensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase))
                continue;

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
            catch (Exception)
            {
            }
#else
            await Task.CompletedTask;
#endif
            result.Add(new LibraryTrack(new Track(file, title, artist, file), duration));
        }

        return result;
    }
#endif

    public async Task InsertTrackAsync(Track track, TrackInsertPosition position, CancellationToken cancellationToken = default)
    {
        if (tracks.Count == 0)
        {
            tracks = [track];
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

    private async Task LoadTrackAsync(int index, CancellationToken cancellationToken)
    {
        var source = tracks[index].Source;
        if (!File.Exists(source))
            throw new FileNotFoundException($"Audiodatei nicht gefunden: {source}", source);

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
#if ANDROID
            return (IAudioPlayer)new CoverPlayerMP.Platforms.Android.AndroidFilePlayer(source);
#else
            return audioManager.CreatePlayer(File.OpenRead(source));
#endif
        }, cancellationToken);

    private async Task TransitionByAsync(int offset, CancellationToken cancellationToken)
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
            index = (baseIndex + offset + tracks.Count) % tracks.Count;

            if (index == currentIndex && audioPlayer is not null)
            {
                failedTransitionIndex = null;
                await RestartAsync(transitionCancellation.Token);
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
                SetError($"„{name}“ kann nicht abgespielt werden: {ex.Message}");
            }
            else
            {
                SetError($"Übergang konnte nicht ausgeführt werden: {ex.Message}");
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
                    Position >= Duration - Math.Max(.2, crossfadeSeconds))
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
            SetError($"Playerüberwachung fehlgeschlagen: {ex.Message}");
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
        if (!File.Exists(nextSource))
            throw new FileNotFoundException($"Audiodatei nicht gefunden: {nextSource}", nextSource);

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
            var duration = Math.Max(.05, crossfadeSeconds);
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
        var duration = Math.Max(.05, crossfadeSeconds);
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
        return ValueTask.CompletedTask;
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
