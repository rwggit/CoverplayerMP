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
    private double volume = 1;
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
        if (!string.IsNullOrEmpty(track.CoverSource) && File.Exists(track.CoverSource))
            coverUrl = server.Register(track.CoverSource, renderer.Host);
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
            {
                if (!playing)
                    return position;
                var value = position + (DateTime.UtcNow - stamp).TotalSeconds;
                return duration > 0 ? Math.Min(value, duration) : value;
            }
        }
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
        lock (sync)
        {
            if (disposed || !IsReady)
                return;

            var elapsed = (DateTime.UtcNow - startTime).TotalSeconds;
            if (status.State == RemoteState.Transitioning || status.State == RemoteState.Unknown)
                return;

            if (status.State == RemoteState.Stopped)
            {
                if (playing && !stoppedByUs && duration > 0 && elapsed > 3)
                {
                    position = duration;
                    stamp = DateTime.UtcNow;
                    playing = false;
                    PlaybackEnded?.Invoke(this, EventArgs.Empty);
                }
                return;
            }

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

        await renderer.SeekAsync(seconds);
        lock (sync)
        {
            position = seconds;
            startOffset = seconds;
            startTime = DateTime.UtcNow;
            stamp = DateTime.UtcNow;
        }
    }

    public void Pause()
    {
        if (disposed || !loaded)
            return;

        lock (sync)
        {
            position = CurrentPosition;
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

    public void Seek(double seconds)
    {
        if (disposed)
            return;

        if (!loaded || !IsReady)
        {
            lock (sync)
            {
                position = seconds;
                stamp = DateTime.UtcNow;
            }
            return;
        }

        lock (sync)
        {
            position = seconds;
            startOffset = seconds;
            startTime = DateTime.UtcNow;
            stamp = DateTime.UtcNow;
        }

        if (ReferenceEquals(renderer.Current, this))
            renderer.Enqueue(() => renderer.SeekAsync(seconds));
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
