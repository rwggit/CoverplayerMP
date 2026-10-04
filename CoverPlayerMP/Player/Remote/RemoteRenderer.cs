using System.Threading.Channels;

namespace CoverPlayerMP.Player.Remote;

internal enum RemoteState
{
    Unknown,
    Playing,
    Paused,
    Stopped,
    Transitioning
}

internal sealed record RemoteStatus(double Position, double Duration, RemoteState State);

/// <summary>Gemeinsame Basis für Cast- und DLNA-Geräte: serialisierte Befehle und Statusabfrage.</summary>
internal abstract class RemoteRenderer : IAsyncDisposable
{
    private readonly Channel<Func<Task>> queue = Channel.CreateUnbounded<Func<Task>>();
    private readonly CancellationTokenSource cancellation = new();
    private double pendingVolume = -1;
    private int volumeQueued;

    protected RemoteRenderer()
    {
        _ = Task.Run(RunQueueAsync);
        _ = Task.Run(PollAsync);
    }

    public abstract string Host { get; }
    public RemoteAudioPlayer? Current { get; set; }
    public event Action<string>? Failed;

    public abstract Task ConnectAsync(CancellationToken cancellationToken);
    public abstract Task PlayUrlAsync(string url, string contentType, string title, string artist, string? coverUrl);
    public abstract Task ResumeAsync();
    public abstract Task PauseAsync();
    public abstract Task StopAsync();
    public abstract Task SeekAsync(double seconds);
    public abstract Task SetVolumeAsync(double volume);
    public abstract Task<double?> GetVolumeAsync();
    public abstract Task<RemoteStatus?> QueryStatusAsync();

    public void Enqueue(Func<Task> command) => queue.Writer.TryWrite(command);

    /// <summary>Lautstärke wird zusammengefasst, damit Schieberegler-Bewegungen das Gerät nicht überfluten.</summary>
    public void EnqueueVolume(double volume)
    {
        Volatile.Write(ref pendingVolume, volume);
        if (Interlocked.Exchange(ref volumeQueued, 1) == 1)
            return;

        Enqueue(async () =>
        {
            Interlocked.Exchange(ref volumeQueued, 0);
            var value = Volatile.Read(ref pendingVolume);
            if (value >= 0)
                await SetVolumeAsync(value);
        });
    }

    private async Task RunQueueAsync()
    {
        try
        {
            await foreach (var command in queue.Reader.ReadAllAsync(cancellation.Token))
            {
                try
                {
                    await command();
                }
                catch (Exception ex) when (!cancellation.IsCancellationRequested)
                {
                    Failed?.Invoke(ex.Message);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task PollAsync()
    {
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellation.Token);
                var current = Current;
                if (current is null || !current.IsReady)
                    continue;

                try
                {
                    var status = await QueryStatusAsync();
                    if (status is not null)
                        current.ApplyStatus(status);
                }
                catch (Exception)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        queue.Writer.TryComplete();
        cancellation.Cancel();
        try
        {
            await StopAsync();
        }
        catch (Exception)
        {
        }
        cancellation.Dispose();
    }
}
