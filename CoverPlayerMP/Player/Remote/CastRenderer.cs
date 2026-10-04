using Sharpcaster;
using Sharpcaster.Models;
using Sharpcaster.Models.Media;

namespace CoverPlayerMP.Player.Remote;

/// <summary>Google-Cast-Gerät (Default Media Receiver) über SharpCaster.</summary>
internal sealed class CastRenderer(ChromecastReceiver receiver) : RemoteRenderer
{
    private const string DefaultMediaReceiver = "CC1AD845";
    private readonly ChromecastClient client = new();
    private double lastVolume = 1;

    public override string Host => receiver.DeviceUri.Host;

    public override async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await client.ConnectChromecast(receiver);
        await client.LaunchApplicationAsync(DefaultMediaReceiver);
    }

    public override async Task PlayUrlAsync(string url, string contentType, string title, string artist, string? coverUrl)
    {
        var metadata = new MusicTrackMetadata
        {
            Title = title,
            Artist = artist,
            MetadataType = MetadataType.Music
        };
        if (coverUrl is not null)
            metadata.Images = [new Sharpcaster.Models.Media.Image { Url = coverUrl }];

        await client.MediaChannel.LoadAsync(new Media
        {
            ContentUrl = url,
            ContentType = contentType,
            Metadata = metadata
        });
    }

    public override async Task ResumeAsync() => await client.MediaChannel.PlayAsync();

    public override async Task PauseAsync() => await client.MediaChannel.PauseAsync();

    public override async Task StopAsync()
    {
        try
        {
            await client.MediaChannel.StopAsync();
        }
        catch (Exception)
        {
        }
    }

    public override async Task SeekAsync(double seconds) => await client.MediaChannel.SeekAsync(seconds);

    public override async Task SetVolumeAsync(double volume)
    {
        lastVolume = volume;
        await client.MediaChannel.SetVolumeAsync(volume);
    }

    public override Task<double?> GetVolumeAsync() => Task.FromResult<double?>(lastVolume);

    public override Task<RemoteStatus?> QueryStatusAsync()
    {
        var status = client.MediaChannel.MediaStatus;
        if (status is null)
            return Task.FromResult<RemoteStatus?>(null);

        var state = status.PlayerState.ToString() switch
        {
            "PLAYING" or "Playing" => RemoteState.Playing,
            "PAUSED" or "Paused" => RemoteState.Paused,
            "BUFFERING" or "Buffering" or "LOADING" or "Loading" => RemoteState.Transitioning,
            "IDLE" or "Idle" => RemoteState.Stopped,
            _ => RemoteState.Unknown
        };
        return Task.FromResult<RemoteStatus?>(new RemoteStatus(status.CurrentTime, status.Media?.Duration ?? 0, state));
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        try
        {
            await client.DisconnectAsync();
        }
        catch (Exception)
        {
        }
    }
}
