using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Blaze.Tests;

/// <summary>Controllable file server: Range/If-Range, drops, outages, throttling, content changes.</summary>
public sealed class TestServer : IAsyncDisposable
{
    private WebApplication? _app;
    public int Port { get; private set; }
    public long Size = 8L << 20;
    public int Seed = 1;
    public bool SupportRanges = true;
    public volatile bool Offline;
    public int DropAfterBytes;       // >0: kill each response after this many bytes
    public int ThrottleBytesPerSec;  // per request; 0 = unlimited
    public int Requests;
    public long BytesServed;

    public string ETag => $"\"v{Seed}\"";
    public string Url => $"http://127.0.0.1:{Port}/f.bin";

    public static byte Gen(long i, int seed) => (byte)(((i * 131) + ((i >> 7) * 17) + seed * 29) ^ (i >> 13));

    public static string Sha(long size, int seed)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var buf = new byte[65536];
        for (long o = 0; o < size; o += buf.Length)
        {
            int n = (int)Math.Min(buf.Length, size - o);
            for (int k = 0; k < n; k++) buf[k] = Gen(o + k, seed);
            h.AppendData(buf, 0, n);
        }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    public static string FileSha(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs));
    }

    public async Task StartAsync(int port = 0)
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls($"http://127.0.0.1:{port}");
        _app = b.Build();
        _app.MapGet("/f.bin", Handle);
        await _app.StartAsync();
        Port = new Uri(_app.Urls.First()).Port;
    }

    private async Task Handle(HttpContext c)
    {
        Interlocked.Increment(ref Requests);
        if (Offline) { c.Abort(); return; }

        long size = Size; int seed = Seed;
        long start = 0, end = size - 1; bool partial = false;
        string range = c.Request.Headers.Range.ToString();
        string ifRange = c.Request.Headers.IfRange.ToString();
        bool rangeOk = SupportRanges && range.StartsWith("bytes=") && (ifRange.Length == 0 || ifRange == ETag);
        if (rangeOk)
        {
            var parts = range[6..].Split('-');
            start = long.Parse(parts[0]);
            end = parts[1].Length > 0 ? Math.Min(long.Parse(parts[1]), size - 1) : size - 1;
            if (start >= size) { c.Response.StatusCode = 416; return; }
            partial = true;
        }

        c.Response.Headers.ETag = ETag;
        if (SupportRanges) c.Response.Headers.AcceptRanges = "bytes";
        c.Response.Headers.ContentDisposition = "attachment; filename=\"test.bin\"";
        if (partial)
        {
            c.Response.StatusCode = 206;
            c.Response.Headers.ContentRange = $"bytes {start}-{end}/{size}";
        }
        c.Response.ContentLength = end - start + 1;
        await c.Response.StartAsync();

        var buf = new byte[16384];
        long sent = 0;
        for (long o = start; o <= end; o += buf.Length)
        {
            if (Offline) { c.Abort(); return; }
            if (DropAfterBytes > 0 && sent >= DropAfterBytes) { c.Abort(); return; }
            int n = (int)Math.Min(buf.Length, end - o + 1);
            for (int k = 0; k < n; k++) buf[k] = Gen(o + k, seed);
            try { await c.Response.Body.WriteAsync(buf.AsMemory(0, n), c.RequestAborted); }
            catch { return; }
            sent += n;
            Interlocked.Add(ref BytesServed, n);
            if (ThrottleBytesPerSec > 0) await Task.Delay((int)(n * 1000L / ThrottleBytesPerSec));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
    }
}
