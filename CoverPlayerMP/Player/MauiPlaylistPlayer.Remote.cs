using CoverPlayerMP.Player.Remote;
using CoverPlayerMP.Shared.Player;

namespace CoverPlayerMP.Player;

public sealed partial class MauiPlaylistPlayer
{
    private readonly RemoteDiscovery discovery = new();
    private readonly MediaFileServer fileServer = new();
    private RemoteRenderer? renderer;
#if ANDROID
    private Android.Media.AudioDeviceInfo? preferredDevice;
#endif

    private const double MaxRemoteVolume = 0.15;

    public bool SupportsRemotePlayback => true;
    public RemoteDevice? ActiveDevice { get; private set; }

    public async Task<IReadOnlyList<RemoteDevice>> GetRemoteDevicesAsync(CancellationToken cancellationToken = default)
    {
        var result = new List<RemoteDevice>();
#if ANDROID
        if (global::Android.OS.Build.VERSION.SdkInt >= (global::Android.OS.BuildVersionCodes)36)
            await Permissions.RequestAsync<CoverPlayerMP.Platforms.Android.LocalNetworkPermission>();

        foreach (var device in GetOutputDevices())
            result.Add(new RemoteDevice(device.Id.ToString(), device.ProductName?.ToString() ?? device.Type.ToString(), RemoteDeviceKind.Audio, GetConnectionName(device.Type)));
#endif
        result.AddRange(await discovery.FindAsync(cancellationToken));
        result.Add(new RemoteDevice("screen-settings", text.RemoteScreen, RemoteDeviceKind.Screen, "Miracast"));
        return result;
    }

    public async Task SelectRemoteDeviceAsync(RemoteDevice? device, CancellationToken cancellationToken = default)
    {
        if (device?.Kind == RemoteDeviceKind.Screen)
        {
            await OpenScreenSettingsAsync();
            return;
        }

        RemoteRenderer? newRenderer = null;
        if (device is not null && Volume > MaxRemoteVolume)
            Volume = MaxRemoteVolume;
#if ANDROID
        Android.Media.AudioDeviceInfo? newPreferred = null;
#endif
        try
        {
            if (device?.Kind is RemoteDeviceKind.Cast or RemoteDeviceKind.Dlna)
            {
                newRenderer = discovery.Create(device.Id)
                    ?? throw new InvalidOperationException(text.RemoteNoDevices);
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                await newRenderer.ConnectAsync(timeout.Token);
                newRenderer.Failed += OnRemoteFailed;
            }
#if ANDROID
            else if (device?.Kind == RemoteDeviceKind.Audio)
            {
                newPreferred = GetOutputDevices().FirstOrDefault(d => d.Id.ToString() == device.Id);
            }
#endif
        }
        catch (Exception ex)
        {
            if (newRenderer is not null)
                await newRenderer.DisposeAsync();
            SetError(text.ErrorRemote(ex.Message));
            return;
        }

        CancelTransitionRequest();
        await transitionLock.WaitAsync(cancellationToken);
        try
        {
            var old = audioPlayer;
            var oldRenderer = renderer;
            var position = old?.CurrentPosition ?? 0;
            var wasPlaying = IsPlaying;
            var wasPaused = IsPaused;

            monitorCancellation?.Cancel();
            monitorRunning = false;
            audioPlayer = null;
            IsPlaying = false;
            if (old is not null)
                ReleasePlayer(old);

            renderer = newRenderer;
#if ANDROID
            preferredDevice = newPreferred;
            ActiveDevice = newRenderer is not null || newPreferred is not null ? device : null;
#else
            ActiveDevice = newRenderer is not null ? device : null;
#endif
            if (oldRenderer is not null)
            {
                oldRenderer.Failed -= OnRemoteFailed;
                _ = oldRenderer.DisposeAsync().AsTask();
            }

            if (!isDisposed && currentIndex >= 0 && currentIndex < tracks.Count && SourceExists(tracks[currentIndex].Source))
            {
                var next = await CreatePlayerAsync(tracks[currentIndex].Source, CancellationToken.None);
                next.Volume = volume;
                audioPlayer = next;
                try
                {
                    if (position > 0)
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
            }

            ErrorMessage = null;
            RaiseStateChanged();
        }
        finally
        {
            transitionLock.Release();
        }
    }

    private void OnRemoteFailed(string message) => SetError(text.ErrorRemote(message));

    private void DisposeRemote()
    {
        if (renderer is not null)
            _ = renderer.DisposeAsync().AsTask();
        fileServer.Dispose();
    }

    private static async Task OpenScreenSettingsAsync()
    {
#if ANDROID
        var intent = new Android.Content.Intent(Android.Provider.Settings.ActionCastSettings);
        intent.AddFlags(Android.Content.ActivityFlags.NewTask);
        Platform.AppContext.StartActivity(intent);
        await Task.CompletedTask;
#elif WINDOWS
        await Launcher.Default.OpenAsync(new Uri("ms-settings-connectabledevices:devicediscovery"));
#else
        await Task.CompletedTask;
#endif
    }

#if ANDROID
    private static string GetConnectionName(Android.Media.AudioDeviceType type) => type switch
    {
        Android.Media.AudioDeviceType.BluetoothA2dp or Android.Media.AudioDeviceType.BluetoothSco or Android.Media.AudioDeviceType.BleHeadset or Android.Media.AudioDeviceType.BleSpeaker or Android.Media.AudioDeviceType.BleBroadcast => "Bluetooth",
        Android.Media.AudioDeviceType.UsbDevice or Android.Media.AudioDeviceType.UsbHeadset or Android.Media.AudioDeviceType.UsbAccessory => "USB",
        Android.Media.AudioDeviceType.Hdmi or Android.Media.AudioDeviceType.HdmiArc or Android.Media.AudioDeviceType.HdmiEarc => "HDMI",
        Android.Media.AudioDeviceType.WiredHeadphones or Android.Media.AudioDeviceType.WiredHeadset or Android.Media.AudioDeviceType.LineAnalog or Android.Media.AudioDeviceType.LineDigital => "Kabel",
        _ => type.ToString()
    };

    private static IEnumerable<Android.Media.AudioDeviceInfo> GetOutputDevices()
    {
        var manager = Platform.AppContext.GetSystemService(Android.Content.Context.AudioService) as Android.Media.AudioManager;
        foreach (var device in manager?.GetDevices(Android.Media.GetDevicesTargets.Outputs) ?? [])
        {
            if (device.Type is Android.Media.AudioDeviceType.BuiltinEarpiece
                or Android.Media.AudioDeviceType.BuiltinSpeaker
                or Android.Media.AudioDeviceType.BuiltinMic
                or Android.Media.AudioDeviceType.Telephony
                or Android.Media.AudioDeviceType.RemoteSubmix)
                continue;
            yield return device;
        }
    }
#endif
}
