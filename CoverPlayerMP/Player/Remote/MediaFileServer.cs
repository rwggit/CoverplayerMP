using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace CoverPlayerMP.Player.Remote;

/// <summary>Minimaler HTTP-Server (mit Range-Unterstützung), der lokale Dateien für Cast-/DLNA-Geräte bereitstellt.</summary>
internal sealed class MediaFileServer : IDisposable
{
    private readonly TcpListener listener = new(IPAddress.Any, 0);
    private readonly ConcurrentDictionary<string, string> files = new();
    private readonly CancellationTokenSource cancellation = new();
    private bool started;

    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    private void EnsureStarted()
    {
        lock (listener)
        {
            if (started)
                return;
            listener.Start();
            started = true;
            _ = Task.Run(AcceptLoopAsync);
        }
    }

    public string Register(string path, string deviceHost)
    {
        EnsureStarted();
        var token = Guid.NewGuid().ToString("N");
        files[token] = path;
        return $"http://{GetLocalAddress(deviceHost)}:{Port}/{token}/{Uri.EscapeDataString(Path.GetFileName(path))}";
    }

    public void Unregister(string url)
    {
        var segments = new Uri(url).AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length > 0)
            files.TryRemove(segments[0], out _);
    }

    private static string GetLocalAddress(string deviceHost)
    {
        try
        {
            var address = IPAddress.TryParse(deviceHost, out var ip)
                ? ip
                : Dns.GetHostAddresses(deviceHost).First(a => a.AddressFamily == AddressFamily.InterNetwork);
            using var socket = new Socket(address.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            socket.Connect(address, 9);
            return ((IPEndPoint)socket.LocalEndPoint!).Address.ToString();
        }
        catch (Exception)
        {
            var local = Dns.GetHostAddresses(Dns.GetHostName()).FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            return local?.ToString() ?? "127.0.0.1";
        }
    }

    private async Task AcceptLoopAsync()
    {
        while (!cancellation.IsCancellationRequested)
        {
            try
            {
                var client = await listener.AcceptTcpClientAsync(cancellation.Token);
                _ = Task.Run(() => HandleAsync(client));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception)
            {
                if (cancellation.IsCancellationRequested)
                    return;
            }
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        try
        {
            await using var stream = client.GetStream();
            var header = await ReadHeaderAsync(stream);
            if (header is null)
                return;

            var lines = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
            var request = lines[0].Split(' ');
            if (request.Length < 2)
                return;

            var method = request[0].ToUpperInvariant();
            var segments = request[1].Split('?')[0].Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0 || !files.TryGetValue(segments[0], out var path) || !File.Exists(path))
            {
                await WriteAsync(stream, "HTTP/1.1 404 Not Found\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                return;
            }

            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var length = file.Length;
            long start = 0, end = length - 1;
            var partial = false;
            var range = lines.FirstOrDefault(l => l.StartsWith("Range:", StringComparison.OrdinalIgnoreCase));
            if (range is not null)
            {
                var spec = range[(range.IndexOf('=') + 1)..].Trim().Split('-');
                if (spec.Length == 2)
                {
                    if (spec[0].Length > 0 && long.TryParse(spec[0], out var s))
                    {
                        start = s;
                        if (spec[1].Length > 0 && long.TryParse(spec[1], out var e))
                            end = Math.Min(e, length - 1);
                    }
                    else if (spec[1].Length > 0 && long.TryParse(spec[1], out var suffix))
                    {
                        start = Math.Max(0, length - suffix);
                    }
                    partial = true;
                }
            }

            if (start >= length || start > end)
            {
                await WriteAsync(stream, $"HTTP/1.1 416 Range Not Satisfiable\r\nContent-Range: bytes */{length}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
                return;
            }

            var count = end - start + 1;
            var response = new StringBuilder();
            response.Append(partial ? "HTTP/1.1 206 Partial Content\r\n" : "HTTP/1.1 200 OK\r\n");
            response.Append($"Content-Type: {GetContentType(path)}\r\n");
            response.Append($"Content-Length: {count}\r\n");
            response.Append("Accept-Ranges: bytes\r\n");
            if (partial)
                response.Append($"Content-Range: bytes {start}-{end}/{length}\r\n");
            response.Append("Access-Control-Allow-Origin: *\r\n");
            response.Append("transferMode.dlna.org: Streaming\r\n");
            response.Append("contentFeatures.dlna.org: DLNA.ORG_OP=01;DLNA.ORG_CI=0;DLNA.ORG_FLAGS=01700000000000000000000000000000\r\n");
            response.Append("Connection: close\r\n\r\n");
            await WriteAsync(stream, response.ToString());

            if (method == "HEAD")
                return;

            file.Seek(start, SeekOrigin.Begin);
            var buffer = new byte[64 * 1024];
            var remaining = count;
            while (remaining > 0)
            {
                var read = await file.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, remaining)), cancellation.Token);
                if (read <= 0)
                    break;
                await stream.WriteAsync(buffer.AsMemory(0, read), cancellation.Token);
                remaining -= read;
            }
        }
        catch (Exception)
        {
            // Verbindung vom Gerät beendet (z. B. beim Seek oder Titelwechsel).
        }
    }

    private static async Task<string?> ReadHeaderAsync(NetworkStream stream)
    {
        var builder = new StringBuilder();
        var buffer = new byte[1];
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (builder.Length < 16 * 1024)
        {
            var read = await stream.ReadAsync(buffer, timeout.Token);
            if (read == 0)
                return null;
            builder.Append((char)buffer[0]);
            if (builder.Length >= 4 && builder.ToString(builder.Length - 4, 4) == "\r\n\r\n")
                return builder.ToString();
        }

        return null;
    }

    private static Task WriteAsync(NetworkStream stream, string text) =>
        stream.WriteAsync(Encoding.ASCII.GetBytes(text)).AsTask();

    public static string GetContentType(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".mp3" => "audio/mpeg",
        ".flac" => "audio/flac",
        ".m4a" => "audio/mp4",
        ".aac" => "audio/aac",
        ".wav" => "audio/wav",
        ".ogg" or ".opus" => "audio/ogg",
        ".wma" => "audio/x-ms-wma",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        _ => "application/octet-stream"
    };

    public void Dispose()
    {
        cancellation.Cancel();
        try
        {
            listener.Stop();
        }
        catch (Exception)
        {
        }
        cancellation.Dispose();
    }
}
