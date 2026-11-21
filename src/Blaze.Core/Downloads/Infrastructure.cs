using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Blaze.Core.Downloads;

/// <summary>Lets a backoff sleep end early when the OS reports that the network is back.</summary>
public sealed class NetworkGate
{
    private TaskCompletionSource _wake = New();
    private static TaskCompletionSource New() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public NetworkGate(bool watchOs)
    {
        if (!watchOs) return;
        try
        {
            NetworkChange.NetworkAvailabilityChanged += (_, e) => { if (e.IsAvailable) Wake(); };
            NetworkChange.NetworkAddressChanged += (_, _) => Wake();
        }
        catch { /* platform without netlink access: plain backoff still works */ }
    }

    public void Wake() => Interlocked.Exchange(ref _wake, New()).TrySetResult();

    public async Task DelayAsync(TimeSpan delay, CancellationToken ct)
    {
        var wake = Volatile.Read(ref _wake).Task;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var timer = Task.Delay(delay, cts.Token);
        await Task.WhenAny(timer, wake).ConfigureAwait(false);
        cts.Cancel();
        ct.ThrowIfCancellationRequested();
    }
}

/// <summary>Global download speed cap so browsing stays responsive on slow links.</summary>
public sealed class RateLimiter
{
    private long _bps;
    private long _next;
    private readonly object _l = new();

    public long BytesPerSecond { get => Volatile.Read(ref _bps); set => Volatile.Write(ref _bps, value); }

    public async ValueTask AcquireAsync(int bytes, CancellationToken ct)
    {
        long bps = BytesPerSecond;
        if (bps <= 0) return;
        long wait;
        lock (_l)
        {
            long now = Stopwatch.GetTimestamp();
            if (_next < now) _next = now;
            wait = _next - now;
            _next += bytes * Stopwatch.Frequency / bps;
        }
        long ms = wait * 1000 / Stopwatch.Frequency;
        if (ms > 1) await Task.Delay((int)Math.Min(ms, int.MaxValue), ct).ConfigureAwait(false);
    }
}

[JsonSourceGenerationOptions(WriteIndented = false, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, Converters = [typeof(JsonStringEnumConverter<DownloadStatus>)])]
[JsonSerializable(typeof(List<DownloadJob>))]
[JsonSerializable(typeof(DownloadJob))]
public sealed partial class BlazeJson : JsonSerializerContext;

public static class FileNames
{
    public static string Sanitize(string? name)
    {
        name = (name ?? "").Trim().Trim('"');
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Select(c => char.IsControl(c) || Array.IndexOf(invalid, c) >= 0 ? '_' : c).ToArray();
        name = new string(chars).Trim().TrimStart('.');
        if (name.Length == 0) return "download";
        if (name.Length > 200)
        {
            var ext = Path.GetExtension(name);
            if (ext.Length > 20) ext = "";
            name = name[..(200 - ext.Length)] + ext;
        }
        return name;
    }

    public static string Unique(string dir, string fileName)
    {
        var path = Path.Combine(dir, fileName);
        if (!File.Exists(path)) return path;
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        for (int i = 1; ; i++)
        {
            path = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(path)) return path;
        }
    }
}
