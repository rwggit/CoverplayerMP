using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Xml.Linq;
using CoverPlayerMP.Shared.Player;
using Sharpcaster;

namespace CoverPlayerMP.Player.Remote;

/// <summary>Sucht Google-Cast-Geräte (mDNS) und DLNA-MediaRenderer (SSDP) im lokalen Netzwerk.</summary>
internal sealed class RemoteDiscovery
{
    private static readonly string[] SearchTargets =
    [
        "urn:schemas-upnp-org:device:MediaRenderer:1",
        "urn:schemas-upnp-org:service:AVTransport:1",
        "upnp:rootdevice",
        "ssdp:all"
    ];

    private static readonly IPEndPoint SsdpEndpoint = new(IPAddress.Parse("239.255.255.250"), 1900);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
    private readonly Dictionary<string, Func<RemoteRenderer>> factories = [];

    public RemoteRenderer? Create(string id)
    {
        lock (factories)
            return factories.TryGetValue(id, out var factory) ? factory() : null;
    }

    public async Task<IReadOnlyList<RemoteDevice>> FindAsync(CancellationToken cancellationToken)
    {
#if ANDROID
        using var networkScope = AndroidNetworkScope.Enter();
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
        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var addresses = GetLocalAddresses();

        var searches = addresses.Select(address => SearchAsync(address, locations, cancellationToken)).ToList();
        searches.Add(ListenAsync(addresses, locations, cancellationToken));
        try
        {
            await Task.WhenAll(searches);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
        }

        List<string> found;
        lock (locations)
            found = locations.ToList();

        var described = await Task.WhenAll(found.Select(location => DescribeAsync(location, cancellationToken)));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var info in described)
        {
            if (info is not { } device || !seen.Add(device.Udn ?? device.Uri.ToString()))
                continue;

            var uri = device.Uri;
            result.Add((new RemoteDevice($"dlna:{uri}", device.Name, RemoteDeviceKind.Dlna, "DLNA"), () => new DlnaRenderer(uri)));
        }

        return result;
    }

    private static async Task<(Uri Uri, string Name, string? Udn)?> DescribeAsync(string location, CancellationToken token)
    {
        if (!Uri.TryCreate(location, UriKind.Absolute, out var uri))
            return null;

        for (var attempt = 1; attempt <= 2; attempt++)
        {
            try
            {
                var xml = await Http.GetStringAsync(uri, token);
                if (!xml.Contains("AVTransport", StringComparison.OrdinalIgnoreCase))
                    return null;

                var document = XDocument.Parse(xml);
                var name = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "friendlyName")?.Value;
                var udn = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "UDN")?.Value;
                return (uri, string.IsNullOrWhiteSpace(name) ? uri.Host : name, udn);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception)
            {
            }
        }

        return null;
    }

    /// <summary>SSDP M-SEARCH über eine bestimmte lokale Schnittstelle (Multicast und Unicast); sammelt die LOCATION-Adressen der Antworten.</summary>
    private static async Task SearchAsync(IPAddress local, HashSet<string> locations, CancellationToken cancellationToken)
    {
        try
        {
            using var client = new UdpClient(new IPEndPoint(local, 0));
            try
            {
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, local.GetAddressBytes());
                client.Client.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 2);
            }
            catch (Exception)
            {
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            for (var round = 0; round < 2; round++)
            {
                foreach (var target in SearchTargets)
                {
                    var message = BuildSearch(target);
                    await client.SendAsync(message, message.Length, SsdpEndpoint);
                }

                await Task.Delay(200, timeout.Token);
            }

            if (!local.Equals(IPAddress.Any))
                await SweepAsync(client, local, timeout.Token);

            while (!timeout.IsCancellationRequested)
            {
                var response = await client.ReceiveAsync(timeout.Token);
                var location = ParseLocation(Encoding.UTF8.GetString(response.Buffer));
                if (location is not null)
                {
                    lock (locations)
                        locations.Add(location);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    /// <summary>Sendet die Suche ohne Multicast direkt an jede Adresse des /24-Netzes (Unicast); Antworten kommen auf demselben Socket.</summary>
    private static async Task SweepAsync(UdpClient client, IPAddress local, CancellationToken token)
    {
        var bytes = local.GetAddressBytes();
        var messages = SearchTargets.Take(2).Select(BuildSearch).ToList();
        for (var host = 1; host < 255; host++)
        {
            if (host == bytes[3])
                continue;

            var target = new IPEndPoint(new IPAddress([bytes[0], bytes[1], bytes[2], (byte)host]), 1900);
            foreach (var message in messages)
            {
                try
                {
                    await client.SendAsync(message, message.Length, target);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception)
                {
                }
            }

            if (host % 32 == 0)
                await Task.Delay(20, token);
        }
    }

    /// <summary>Hört auf Port 1900 (Multicast-Gruppe) mit und sendet die Suche von dort; erfasst auch NOTIFY-Meldungen der Geräte.</summary>
    private static async Task ListenAsync(List<IPAddress> addresses, HashSet<string> locations, CancellationToken cancellationToken)
    {
        var interfaces = addresses.Where(a => !a.Equals(IPAddress.Any)).ToList();
        try
        {
            using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            try
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 1900));
            }
            catch (SocketException)
            {
                socket.Bind(new IPEndPoint(IPAddress.Any, 0));
            }

            foreach (var address in interfaces)
            {
                try
                {
                    socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.AddMembership, new MulticastOption(SsdpEndpoint.Address, address));
                }
                catch (Exception)
                {
                }
            }

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(6));

            for (var round = 0; round < 2; round++)
            {
                foreach (var address in interfaces)
                {
                    try
                    {
                        socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastInterface, address.GetAddressBytes());
                        foreach (var target in SearchTargets)
                            await socket.SendToAsync(BuildSearch(target), SocketFlags.None, SsdpEndpoint, timeout.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception)
                    {
                    }
                }

                await Task.Delay(300, timeout.Token);
            }

            var buffer = new byte[4096];
            while (!timeout.IsCancellationRequested)
            {
                var result = await socket.ReceiveFromAsync(buffer, SocketFlags.None, new IPEndPoint(IPAddress.Any, 0), timeout.Token);
                var text = Encoding.UTF8.GetString(buffer, 0, result.ReceivedBytes);
                if (text.StartsWith("M-SEARCH", StringComparison.OrdinalIgnoreCase))
                    continue;

                var location = ParseLocation(text);
                if (location is not null)
                {
                    lock (locations)
                        locations.Add(location);
                }
            }
        }
        catch (Exception)
        {
        }
    }

    private static byte[] BuildSearch(string target) => Encoding.ASCII.GetBytes(
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST: 239.255.255.250:1900\r\n" +
        "MAN: \"ssdp:discover\"\r\n" +
        "MX: 2\r\n" +
        $"ST: {target}\r\n" +
        "USER-AGENT: CoverPlayerMP/1.0 UPnP/1.0\r\n\r\n");

    private static string? ParseLocation(string response)
    {
        foreach (var line in response.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            if (line.StartsWith("LOCATION:", StringComparison.OrdinalIgnoreCase))
                return line["LOCATION:".Length..].Trim();
        }

        return null;
    }

    private static List<IPAddress> GetLocalAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                    continue;

                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    if (address.Address.AddressFamily == AddressFamily.InterNetwork &&
                        !IPAddress.IsLoopback(address.Address) &&
                        !address.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal) &&
                        !result.Contains(address.Address))
                        result.Add(address.Address);
                }
            }
        }
        catch (Exception)
        {
        }

#if ANDROID
        foreach (var address in GetAndroidAddresses())
        {
            if (!result.Contains(address))
                result.Add(address);
        }
#endif

        // Zusätzlich ohne Schnittstellenbindung suchen (Fallback, falls die Liste unvollständig ist).
        result.Add(IPAddress.Any);
        return result;
    }

#if ANDROID
    private static List<IPAddress> GetAndroidAddresses()
    {
        var result = new List<IPAddress>();
        try
        {
            if (Platform.AppContext.GetSystemService(global::Android.Content.Context.ConnectivityService) is not global::Android.Net.ConnectivityManager manager)
                return result;

            foreach (var network in manager.GetAllNetworks() ?? [])
            {
                var capabilities = manager.GetNetworkCapabilities(network);
                if (capabilities is null || capabilities.HasTransport(global::Android.Net.TransportType.Cellular))
                    continue;

                foreach (var linkAddress in manager.GetLinkProperties(network)?.LinkAddresses ?? [])
                {
                    var text = linkAddress.Address?.HostAddress;
                    if (IPAddress.TryParse(text, out var ip) && ip.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(ip))
                        result.Add(ip);
                }
            }
        }
        catch (Exception)
        {
        }

        return result;
    }

    /// <summary>Hält während der Suche den Multicast-Empfang offen und bindet den Prozess an das WLAN.</summary>
    private sealed class AndroidNetworkScope : IDisposable
    {
        private readonly global::Android.Net.Wifi.WifiManager.MulticastLock? multicastLock;
        private readonly global::Android.Net.ConnectivityManager? manager;
        private readonly bool bound;

        private AndroidNetworkScope()
        {
            try
            {
                var wifi = Platform.AppContext.GetSystemService(global::Android.Content.Context.WifiService) as global::Android.Net.Wifi.WifiManager;
                multicastLock = wifi?.CreateMulticastLock("coverplayer-discovery");
                multicastLock?.SetReferenceCounted(false);
                multicastLock?.Acquire();
            }
            catch (Exception)
            {
            }

            try
            {
                manager = Platform.AppContext.GetSystemService(global::Android.Content.Context.ConnectivityService) as global::Android.Net.ConnectivityManager;
                var candidates = new List<global::Android.Net.Network>();
                if (manager?.ActiveNetwork is { } active)
                    candidates.Add(active);
                candidates.AddRange(manager?.GetAllNetworks() ?? []);
                foreach (var network in candidates)
                {
                    var capabilities = manager!.GetNetworkCapabilities(network);
                    if (capabilities?.HasTransport(global::Android.Net.TransportType.Wifi) == true)
                    {
                        bound = manager.BindProcessToNetwork(network);
                        if (bound)
                            break;
                    }
                }
            }
            catch (Exception)
            {
            }
        }

        public static AndroidNetworkScope Enter() => new();

        public void Dispose()
        {
            try
            {
                if (multicastLock?.IsHeld == true)
                    multicastLock.Release();
                if (bound)
                    manager?.BindProcessToNetwork(null);
            }
            catch (Exception)
            {
            }
        }
    }
#endif
}
