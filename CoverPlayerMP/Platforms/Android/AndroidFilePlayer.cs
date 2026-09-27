using Android.Media;
using Plugin.Maui.Audio;

namespace CoverPlayerMP.Platforms.Android;

public sealed class AndroidFilePlayer : IAudioPlayer
{
    private readonly MediaPlayer player = new();
    private double volume = 1;
    private double balance;
    private bool disposed;

    public AndroidFilePlayer(string path)
    {
        player.SetAudioAttributes(new AudioAttributes.Builder()
            .SetUsage(AudioUsageKind.Media)!
            .SetContentType(AudioContentType.Music)!
            .Build()!);
        player.SetDataSource(path);
        player.Prepare();
        player.Completion += (_, _) => PlaybackEnded?.Invoke(this, EventArgs.Empty);
        player.Error += (_, e) =>
        {
            e.Handled = true;
            Error?.Invoke(this, EventArgs.Empty);
        };
    }

    public event EventHandler? PlaybackEnded;
    public event EventHandler? Error;

    public double Duration => disposed ? 0 : player.Duration / 1000d;
    public double CurrentPosition => disposed ? 0 : player.CurrentPosition / 1000d;

    public double Volume
    {
        get => volume;
        set
        {
            volume = Math.Clamp(value, 0, 1);
            ApplyVolume();
        }
    }

    public double Balance
    {
        get => balance;
        set
        {
            balance = Math.Clamp(value, -1, 1);
            ApplyVolume();
        }
    }

    public double Speed { get; set; } = 1;
    public double MinimumSpeed => 1;
    public double MaximumSpeed => 1;
    public bool CanSetSpeed => false;
    public bool IsPlaying => !disposed && player.IsPlaying;
    public bool Loop
    {
        get => player.Looping;
        set => player.Looping = value;
    }
    public bool CanSeek => true;

    public void SetSource(System.IO.Stream audioStream) =>
        throw new NotSupportedException("AndroidFilePlayer spielt nur Dateipfade ab.");

    public void Play()
    {
        if (!disposed)
            player.Start();
    }

    public void Pause()
    {
        if (!disposed && player.IsPlaying)
            player.Pause();
    }

    public void Stop()
    {
        if (disposed)
            return;
        if (player.IsPlaying)
            player.Pause();
        player.SeekTo(0);
    }

    public void Seek(double position)
    {
        if (!disposed)
            player.SeekTo((int)(position * 1000));
    }

    public void Dispose()
    {
        if (disposed)
            return;
        disposed = true;
        player.Release();
        player.Dispose();
    }

    private void ApplyVolume()
    {
        if (disposed)
            return;
        var left = (float)(volume * Math.Min(1, 1 - balance));
        var right = (float)(volume * Math.Min(1, 1 + balance));
        player.SetVolume(left, right);
    }
}
