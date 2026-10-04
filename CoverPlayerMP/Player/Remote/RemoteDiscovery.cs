using CoverPlayerMP.Shared.Player;
using Rssdp;
using Sharpcaster;

namespace CoverPlayerMP.Player.Remote;

/// <summary>Sucht Google-Cast-Geräte (mDNS) und DLNA-MediaRenderer (SSDP) im lokalen Netzwerk.</summary>
internal sealed class RemoteDiscovery
{
    private const string MediaRendererType = "urn:schemas-upnp-org:device:MediaRenderer:1";
    private readonly Dictionary<string, Func<RemoteRenderer>> factories = [];

    public RemoteRenderer? Create(string id)
    {
        lock (factories)
            return factories.TryGetValue(id, out var factory) ? factory() : null;
    }

    public async Task<IReadOnlyList<RemoteDevice>> FindAsync(CancellationToken cancellationToken)
    {
#if ANDROID
        using var multicast = AcquireMulticastLock();
#endif
        var results = await Task.WhenAll(FindCastAsync(cancellationToken), FindDlnaAsync(cancellationToken));
        var found = results.SelectMany(x => x).OrderBy(x => x.Device.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        lock (factories)
        {
            factories.Clear();
            foreach (var (device, factory) in found)
                factories[device.Id] = factory;
        }

        return found.Select(x => x.Device).ToList();
    }

    private static async Task<List<(RemoteDevice Device, Func<RemoteRenderer> Factory)>> FindCastAsync(CancellationToken cancellationToken)
    {
        var result = new List<(RemoteDevice, Func<RemoteRenderer>)>();
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var receivers = await new ChromecastLocator().FindReceiversAsync(TimeSpan.FromSeconds(5));
            foreach (var receiver in receivers)
            {
                var id = $"cast:{receiver.DeviceUri.Host}:{receiver.Port}";
                if (result.Any(x => x.Item1.Id == id))
                    continue;
                var captured = receiver;
                result.Add((new RemoteDevice(id, receiver.Name, RemoteDeviceKind.Cast, "Google Cast"), () => new CastRenderer(captured)));
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    private static async Task<List<(RemoteDevice Device, Func<RemoteRenderer> Factory)>> FindDlnaAsync(CancellationToken cancellationToken)
    {
        var result = new List<(RemoteDevice, Func<RemoteRenderer>)>();
        try
        {
            using var locator = new SsdpDeviceLocator();
            var devices = await locator.SearchAsync(MediaRendererType, TimeSpan.FromSeconds(4));
            foreach (var device in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var location = device.DescriptionLocation;
                var id = $"dlna:{location}";
                if (result.Any(x => x.Item1.Id == id))
                    continue;

                var name = location.Host;
                try
                {
                    var info = await device.GetDeviceInfo();
                    if (!string.IsNullOrWhiteSpace(info.FriendlyName))
                        name = info.FriendlyName;
                }
                catch (Exception)
                {
                }

                result.Add((new RemoteDevice(id, name, RemoteDeviceKind.Dlna, "DLNA"), () => new DlnaRenderer(location)));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }

        return result;
    }

#if ANDROID
    private static IDisposable? AcquireMulticastLock()
    {
        var wifi = Platform.AppContext.GetSystemService(global::Android.Content.Context.WifiService) as global::Android.Net.Wifi.WifiManager;
        var multicastLock = wifi?.CreateMulticastLock("coverplayer-discovery");
        if (multicastLock is null)
            return null;

        multicastLock.SetReferenceCounted(false);
        multicastLock.Acquire();
        return new MulticastRelease(multicastLock);
    }

    private sealed class MulticastRelease(global::Android.Net.Wifi.WifiManager.MulticastLock multicastLock) : IDisposable
    {
        public void Dispose()
        {
            if (multicastLock.IsHeld)
                multicastLock.Release();
        }
    }
#endif
}
