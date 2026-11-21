using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace Blaze.Core.Browser;

/// <summary>
/// Minimal Firefox remote-debugging-protocol client (the same channel web-ext uses). Release Firefox only accepts
/// Mozilla-signed add-ons from disk, but accepts a temporary add-on installed over this channel. This lets the
/// Blaze AppImage load its companion extension without Node.js. Start Firefox with `-start-debugger-server PORT`.
/// </summary>
public static class DevToolsClient
{
    /// <summary>Preferred: a Unix domain socket inside a 0700 directory, unreachable by other users and by web pages.</summary>
    public static Task<string> InstallTemporaryAddonAsync(string socketPath, string addonPath, TimeSpan timeout, CancellationToken ct = default)
        => InstallAsync(c => ConnectUnixAsync(socketPath, c), addonPath, timeout, ct);

    /// <summary>Fallback for engines that cannot listen on a socket. Loopback TCP is reachable by every local user.</summary>
    public static Task<string> InstallTemporaryAddonAsync(int port, string addonPath, TimeSpan timeout, CancellationToken ct = default)
        => InstallAsync(c => ConnectTcpAsync(port, c), addonPath, timeout, ct);

    private static async Task<string> InstallAsync(Func<CancellationToken, Task<Stream>> connect, string addonPath, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        await using var stream = await connect(cts.Token).ConfigureAwait(false);

        await ReadPacketAsync(stream, cts.Token).ConfigureAwait(false);            // greeting from "root"
        await SendAsync(stream, w => { w.WriteString("to", "root"); w.WriteString("type", "getRoot"); }, cts.Token).ConfigureAwait(false);

        string? addonsActor = null;
        while (addonsActor is null)
        {
            using var doc = await ReadPacketAsync(stream, cts.Token).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("addonsActor", out var a) && a.ValueKind == JsonValueKind.String)
                addonsActor = a.GetString();
        }

        await SendAsync(stream, w =>
        {
            w.WriteString("to", addonsActor);
            w.WriteString("type", "installTemporaryAddon");
            w.WriteString("addonPath", addonPath);
            w.WriteBoolean("openDevTools", false);
        }, cts.Token).ConfigureAwait(false);

        while (true)
        {
            using var doc = await ReadPacketAsync(stream, cts.Token).ConfigureAwait(false);
            var root = doc.RootElement;
            if (!root.TryGetProperty("from", out var from) || from.GetString() != addonsActor) continue;
            if (root.TryGetProperty("error", out var err))
                throw new InvalidOperationException($"{err.GetString()}: {(root.TryGetProperty("message", out var m) ? m.GetString() : "")}");
            if (root.TryGetProperty("addon", out var addon) && addon.TryGetProperty("id", out var id))
                return id.GetString() ?? "";
        }
    }

    private static async Task<Stream> ConnectUnixAsync(string path, CancellationToken ct)
    {
        Exception? last = null;
        while (!ct.IsCancellationRequested)
        {
            var sock = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try { await sock.ConnectAsync(new UnixDomainSocketEndPoint(path), ct).ConfigureAwait(false); return new NetworkStream(sock, ownsSocket: true); }
            catch (Exception ex) when (ex is SocketException or IOException) { last = ex; sock.Dispose(); }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("Firefox debugger socket never appeared" + (last is null ? "" : ": " + last.Message));
    }

    private static async Task<Stream> ConnectTcpAsync(int port, CancellationToken ct)
    {
        Exception? last = null;
        while (!ct.IsCancellationRequested)
        {
            var tcp = new TcpClient();
            try { await tcp.ConnectAsync("127.0.0.1", port, ct).ConfigureAwait(false); return tcp.GetStream(); }
            catch (Exception ex) when (ex is SocketException or IOException) { last = ex; tcp.Dispose(); }
            await Task.Delay(300, ct).ConfigureAwait(false);
        }
        throw new TimeoutException("Firefox debugger port never opened" + (last is null ? "" : ": " + last.Message));
    }

    private static async Task SendAsync(Stream s, Action<Utf8JsonWriter> body, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms)) { w.WriteStartObject(); body(w); w.WriteEndObject(); }
        var json = ms.ToArray();
        var header = Encoding.ASCII.GetBytes(json.Length + ":");
        await s.WriteAsync(header, ct).ConfigureAwait(false);
        await s.WriteAsync(json, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> ReadPacketAsync(Stream s, CancellationToken ct)
    {
        int len = 0, digits = 0;
        var one = new byte[1];
        while (true)
        {
            if (await s.ReadAsync(one, ct).ConfigureAwait(false) == 0) throw new IOException("debugger connection closed");
            if (one[0] == (byte)':') break;
            if (one[0] is < (byte)'0' or > (byte)'9' || ++digits > 9) throw new IOException("bad debugger packet header");
            len = len * 10 + (one[0] - '0');
        }
        var buf = new byte[len];
        for (int read = 0; read < len;)
        {
            int n = await s.ReadAsync(buf.AsMemory(read, len - read), ct).ConfigureAwait(false);
            if (n == 0) throw new IOException("debugger connection closed");
            read += n;
        }
        return JsonDocument.Parse(buf);
    }
}
