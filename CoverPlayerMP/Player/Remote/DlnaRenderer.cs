using System.Net.Http.Headers;
using System.Security;
using System.Text;
using System.Xml.Linq;

namespace CoverPlayerMP.Player.Remote;

/// <summary>DLNA/UPnP-MediaRenderer (AVTransport + RenderingControl) per SOAP.</summary>
internal sealed class DlnaRenderer(Uri descriptionLocation) : RemoteRenderer
{
    private const string AvTransport = "urn:schemas-upnp-org:service:AVTransport:1";
    private const string RenderingControl = "urn:schemas-upnp-org:service:RenderingControl:1";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private Uri? avTransportUrl;
    private Uri? renderingControlUrl;

    public override string Host => descriptionLocation.Host;

    public override async Task ConnectAsync(CancellationToken cancellationToken)
    {
        var xml = await Http.GetStringAsync(descriptionLocation, cancellationToken);
        var document = XDocument.Parse(xml);
        var baseUri = descriptionLocation;
        var urlBase = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value;
        if (Uri.TryCreate(urlBase, UriKind.Absolute, out var parsedBase))
            baseUri = parsedBase;

        foreach (var service in document.Descendants().Where(e => e.Name.LocalName == "service"))
        {
            var type = service.Elements().FirstOrDefault(e => e.Name.LocalName == "serviceType")?.Value ?? string.Empty;
            var control = service.Elements().FirstOrDefault(e => e.Name.LocalName == "controlURL")?.Value;
            if (control is null)
                continue;

            var uri = new Uri(baseUri, control);
            if (type.Contains("AVTransport", StringComparison.OrdinalIgnoreCase))
                avTransportUrl = uri;
            else if (type.Contains("RenderingControl", StringComparison.OrdinalIgnoreCase))
                renderingControlUrl = uri;
        }

        if (avTransportUrl is null)
            throw new InvalidOperationException("Das Gerät unterstützt keine Wiedergabe über AVTransport.");
    }

    public override async Task PlayUrlAsync(string url, string contentType, string title, string artist, string? coverUrl)
    {
        var didl = new StringBuilder();
        didl.Append("<DIDL-Lite xmlns=\"urn:schemas-upnp-org:metadata-1-0/DIDL-Lite/\" ")
            .Append("xmlns:dc=\"http://purl.org/dc/elements/1.1/\" ")
            .Append("xmlns:upnp=\"urn:schemas-upnp-org:metadata-1-0/upnp/\">")
            .Append("<item id=\"0\" parentID=\"-1\" restricted=\"1\">")
            .Append("<dc:title>").Append(EscapeDidl(title)).Append("</dc:title>")
            .Append("<dc:creator>").Append(EscapeDidl(artist)).Append("</dc:creator>")
            .Append("<upnp:artist>").Append(EscapeDidl(artist)).Append("</upnp:artist>")
            .Append("<upnp:class>object.item.audioItem.musicTrack</upnp:class>");
        if (coverUrl is not null)
            didl.Append("<upnp:albumArtURI>").Append(EscapeDidl(coverUrl)).Append("</upnp:albumArtURI>");
        didl.Append("<res protocolInfo=\"http-get:*:").Append(contentType).Append(":*\">")
            .Append(EscapeDidl(url)).Append("</res></item></DIDL-Lite>");

        // Nur DLNA: Manche Renderer lehnen SetAVTransportURI während der Wiedergabe ab (UPnP 705).
        // Fehler hier ignorieren, das Gerät kann bereits gestoppt sein.
        try
        {
            await SendAsync(avTransportUrl!, AvTransport, "Stop", ("InstanceID", "0"));
        }
        catch (InvalidOperationException)
        {
        }

        await SendAsync(avTransportUrl!, AvTransport, "SetAVTransportURI",
            ("InstanceID", "0"), ("CurrentURI", url), ("CurrentURIMetaData", didl.ToString()));
        await SendAsync(avTransportUrl!, AvTransport, "Play", ("InstanceID", "0"), ("Speed", "1"));
    }

    // Nur &, < und > maskieren. SecurityElement.Escape erzeugt &apos; und &quot;, was manche DLNA-Geräte mit HTTP 500 ablehnen.
    private static string EscapeDidl(string? value) =>
        (value ?? string.Empty)
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;");

    public override Task ResumeAsync() =>
        SendAsync(avTransportUrl!, AvTransport, "Play", ("InstanceID", "0"), ("Speed", "1"));

    public override Task PauseAsync() =>
        SendAsync(avTransportUrl!, AvTransport, "Pause", ("InstanceID", "0"));

    public override Task StopAsync() =>
        avTransportUrl is null
            ? Task.CompletedTask
            : SendAsync(avTransportUrl, AvTransport, "Stop", ("InstanceID", "0"));

    public override async Task SeekAsync(double seconds)
    {
        try
        {
            await SendAsync(avTransportUrl!, AvTransport, "Seek", ("InstanceID", "0"), ("Unit", "REL_TIME"), ("Target", FormatTime(seconds)));
        }
        catch (InvalidOperationException)
        {
            await SendAsync(avTransportUrl!, AvTransport, "Seek", ("InstanceID", "0"), ("Unit", "ABS_TIME"), ("Target", FormatTime(seconds)));
        }
    }

    public override Task SetVolumeAsync(double volume) =>
        renderingControlUrl is null
            ? Task.CompletedTask
            : SendAsync(renderingControlUrl, RenderingControl, "SetVolume",
                ("InstanceID", "0"), ("Channel", "Master"), ("DesiredVolume", ((int)Math.Round(volume * 100)).ToString()));

    public override async Task<double?> GetVolumeAsync()
    {
        if (renderingControlUrl is null)
            return null;

        var response = await SendAsync(renderingControlUrl, RenderingControl, "GetVolume", ("InstanceID", "0"), ("Channel", "Master"));
        return double.TryParse(Value(response, "CurrentVolume"), out var volume) ? volume / 100d : null;
    }

    public override async Task<RemoteStatus?> QueryStatusAsync()
    {
        if (avTransportUrl is null)
            return null;

        var transport = await SendAsync(avTransportUrl, AvTransport, "GetTransportInfo", ("InstanceID", "0"));
        var state = Value(transport, "CurrentTransportState") switch
        {
            "PLAYING" => RemoteState.Playing,
            "PAUSED_PLAYBACK" or "PAUSED_RECORDING" => RemoteState.Paused,
            "STOPPED" or "NO_MEDIA_PRESENT" => RemoteState.Stopped,
            "TRANSITIONING" => RemoteState.Transitioning,
            _ => RemoteState.Unknown
        };

        var info = await SendAsync(avTransportUrl, AvTransport, "GetPositionInfo", ("InstanceID", "0"));
        return new RemoteStatus(ParseTime(Value(info, "RelTime")), ParseTime(Value(info, "TrackDuration")), state);
    }

    private static string FormatTime(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)time.TotalHours:00}:{time.Minutes:00}:{time.Seconds:00}";
    }

    private static double ParseTime(string? value) =>
        TimeSpan.TryParse(value, out var time) ? time.TotalSeconds : 0;

    private static string? Value(XDocument? document, string name) =>
        document?.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

    private static string DescribeUpnpError(string text)
    {
        try
        {
            var error = XDocument.Parse(text).Descendants().FirstOrDefault(e => e.Name.LocalName == "UPnPError");
            var code = error?.Elements().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value;
            var description = error?.Elements().FirstOrDefault(e => e.Name.LocalName == "errorDescription")?.Value;
            return code is null ? string.Empty : $" (UPnP {code}: {description})";
        }
        catch (Exception)
        {
            return string.Empty;
        }
    }

    private static async Task<XDocument?> SendAsync(Uri url, string service, string action, params (string Name, string Value)[] arguments)
    {
        var body = new StringBuilder();
        body.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            .Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">")
            .Append("<s:Body><u:").Append(action).Append(" xmlns:u=\"").Append(service).Append("\">");
        foreach (var (name, value) in arguments)
            body.Append('<').Append(name).Append('>').Append(SecurityElement.Escape(value)).Append("</").Append(name).Append('>');
        body.Append("</u:").Append(action).Append("></s:Body></s:Envelope>");

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(body.ToString(), Encoding.UTF8)
        };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("text/xml") { CharSet = "utf-8" };
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{service}#{action}\"");

        using var response = await Http.SendAsync(request);
        var text = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{action}: HTTP {(int)response.StatusCode}{DescribeUpnpError(text)}");

        return string.IsNullOrWhiteSpace(text) ? null : XDocument.Parse(text);
    }
}
