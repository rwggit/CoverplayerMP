using CoverPlayerMP.Shared.Player;
using Plugin.Maui.Audio;

namespace CoverPlayerMP.Player.Remote;

/// <summary>IAudioPlayer-Adapter, der die Wiedergabe an ein Cast-/DLNA-Gerät weiterreicht.</summary>
internal sealed class RemoteAudioPlayer : IAudioPlayer
{
    private readonly RemoteRenderer renderer;
    private readonly MediaFileServer server;
    private readonly string url;
    private readonly string? coverUrl;
    private readonly string contentType;
    private readonly string title;
    private readonly string artist;
    private readonly object sync = new();
    private double position;
    private double duration;
    private DateTime stamp = DateTime.UtcNow;
    private double startOffset;
    private DateTime startTime = DateTime.UtcNow;
    private DateTime ignoreStatusUntil = DateTime.MinValue;
    private DateTime lastResume = DateTime.MinValue;
    private double volume = 1;
    private double pendingSeek = -1;
    private int seekQueued;
    private bool loaded;
    private bool playing;
    private bool disposed;
    private bool stoppedByUs;

    public RemoteAudioPlayer(RemoteRenderer renderer, MediaFileServer server, Track track, double volume)
    {
        this.renderer = renderer;
        this.server = server;
        this.volume = volume;
        title = string.IsNullOrWhiteSpace(track.Title) ? Path.GetFileNameWithoutExtension(track.Source) : track.Title;
        artist = track.Artist ?? string.Empty;
        contentType = MediaFileServer.GetContentType(track.Source);
        url = server.Register(track.Source, renderer.Host);
        var coverFile = CoverImage.GetCoverImageFile(track.CoverSource, FileSystem.CacheDirectory);
        if (coverFile is not null)
            coverUrl = server.Register(coverFile, renderer.Host);
    }

    public bool IsReady { get; private set; }

    public event EventHandler? PlaybackEnded;
    public event EventHandler? Error;

    public double Duration
    {
        get { lock (sync) return duration; }
    }

    public double CurrentPosition
    {
        get
        {
            lock (sync)
                return EstimatePosition();
        }
    }

    private double EstimatePosition()
    {
        if (!playing)
            return position;
        var value = position + (DateTime.UtcNow - stamp).TotalSeconds;
        return duration > 0 ? Math.Min(value, duration) : value;
    }

    public double Volume
    {
        get => volume;
        set
        {
            var clamped = Math.Clamp(value, 0, 1);
            if (Math.Abs(clamped - volume) < 0.004 && loaded)
                return;
            volume = clamped;
            if (!disposed)
                renderer.EnqueueVolume(clamped);
        }
    }

    public double Balance { get; set; }
    public double Speed { get; set; } = 1;
    public double MinimumSpeed => 1;
    public double MaximumSpeed => 1;
    public bool CanSetSpeed => false;
    public bool IsPlaying => playing;
    public bool Loop { get; set; }
    public bool CanSeek => true;

    public void SetSource(Stream audioStream) =>
        throw new NotSupportedException("RemoteAudioPlayer spielt nur Dateien ab.");

    public void ApplyStatus(RemoteStatus status)
    {
        var ended = false;
        var resume = false;
        lock (sync)
        {
            if (disposed || !IsReady || DateTime.UtcNow < ignoreStatusUntil)
                return;

            if (status.State is RemoteState.Transitioning or RemoteState.Unknown)
                return;

            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            if (status.State == RemoteState.Stopped)
            {
                if (!playing || stoppedByUs)
                    return;

                if (duration > 0 && EstimatePosition() >= duration - 3)
                {
                    position = duration;
                    stamp = DateTime.UtcNow;
                    playing = false;
                    ended = true;
                }
                else if (elapsed > 1 && (DateTime.UtcNow - lastResume).TotalSeconds > 5)
                {
                    // Das Gerät hat (z. B. nach einem Seek) mitten im Titel gestoppt: Wiedergabe fortsetzen.
                    lastResume = DateTime.UtcNow;
                    resume = true;
                }
            }
            else
            {
                // Verzögerte Werte des vorherigen Titels verwerfen.
                if (status.Position > startOffset + elapsed + 5)
                    return;

                position = status.Position;
                stamp = DateTime.UtcNow;
                if (status.Duration > 0)
                    duration = status.Duration;
                playing = status.State == RemoteState.Playing && !stoppedByUs;
            }
        }

        if (resume)
            renderer.Enqueue(renderer.ResumeAsync);
        if (ended)
            PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    public void Play()
    {
        if (disposed)
            return;

        renderer.Current = this;
        stoppedByUs = false;
        lock (sync)
        {
            stamp = DateTime.UtcNow;
            playing = true;
        }

        if (!loaded)
        {
            loaded = true;
            var seekTo = position;
            renderer.Enqueue(async () =>
            {
                await renderer.PlayUrlAsync(url, contentType, title, artist, coverUrl);
                await renderer.SetVolumeAsync(volume);
                lock (sync)
                {
                    startOffset = 0;
                    startTime = DateTime.UtcNow;
                    stamp = DateTime.UtcNow;
                    IsReady = true;
                }

                if (seekTo > 1)
                    await SeekWhenPlayingAsync(seekTo);
            });
        }
        else
        {
            renderer.Enqueue(renderer.ResumeAsync);
        }
    }

    private async Task SeekWhenPlayingAsync(double seconds)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var status = await renderer.QueryStatusAsync();
            if (status?.State == RemoteState.Playing)
                break;
            await Task.Delay(500);
        }

        await SeekNowAsync(seconds);
    }

    /// <summary>Führt den Seek aus und gibt dem Gerät Zeit, bevor der Status wieder ausgewertet wird.</summary>
    private async Task SeekNowAsync(double seconds)
    {
        lock (sync)
            ignoreStatusUntil = DateTime.UtcNow.AddSeconds(30);

        try
        {
            await renderer.SeekAsync(seconds);
            if (playing)
                await renderer.ResumeAsync();
        }
        finally
        {
            lock (sync)
            {
                position = seconds;
                startOffset = seconds;
                startTime = DateTime.UtcNow;
                stamp = DateTime.UtcNow;
                ignoreStatusUntil = DateTime.UtcNow.AddSeconds(2);
            }
        }
    }

    public void Pause()
    {
        if (disposed || !loaded)
            return;

        lock (sync)
        {
            position = EstimatePosition();
            stamp = DateTime.UtcNow;
            playing = false;
        }

        if (ReferenceEquals(renderer.Current, this))
            renderer.Enqueue(renderer.PauseAsync);
    }

    public void Stop()
    {
        if (disposed)
            return;

        stoppedByUs = true;
        lock (sync)
        {
            position = 0;
            playing = false;
        }

        if (loaded && ReferenceEquals(renderer.Current, this))
            renderer.Enqueue(renderer.StopAsync);
    }

    /// <summary>Seeks (z. B. beim Ziehen des Reglers) werden zusammengefasst; nur die letzte Position wird gesendet.</summary>
    public void Seek(double seconds)
    {
        if (disposed)
            return;

        lock (sync)
        {
            position = seconds;
            startOffset = seconds;
            startTime = DateTime.UtcNow;
            stamp = DateTime.UtcNow;
            ignoreStatusUntil = DateTime.UtcNow.AddSeconds(30);
        }

        if (!loaded || !IsReady)
            return;

        Volatile.Write(ref pendingSeek, seconds);
        if (Interlocked.Exchange(ref seekQueued, 1) == 1)
            return;

        renderer.Enqueue(async () =>
        {
            // Kurz warten, damit weitere Reglerbewegungen zusammengefasst werden.
            await Task.Delay(300);
            Interlocked.Exchange(ref seekQueued, 0);
            var target = Volatile.Read(ref pendingSeek);
            if (target < 0 || disposed || !ReferenceEquals(renderer.Current, this))
                return;

            await SeekNowAsync(target);
        });
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        playing = false;
        if (ReferenceEquals(renderer.Current, this))
            renderer.Current = null;
        server.Unregister(url);
        if (coverUrl is not null)
            server.Unregister(coverUrl);
    }
}
