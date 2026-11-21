using System.Collections.Concurrent;
using System.Buffers;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace Blaze.Core.Downloads;

/// <summary>
/// Resumable, multi-connection download engine.
///  - Ranges + If-Range validation: a changed remote file restarts safely instead of corrupting.
///  - Retries forever (until paused/removed) with backoff; wakes instantly when the OS says the network is back.
///  - State is fsync'd before it is written, so a crash or power cut never claims bytes that are not on disk.
/// </summary>
public sealed class DownloadManager : IAsyncDisposable
{
    private sealed class Runtime(DownloadJob job)
    {
        public DownloadJob Job = job;
        public CancellationTokenSource? Cts;
        public Task? Task;
        public bool UserStopped;
        public bool Removed;
        public SafeFileHandle? Handle;
        public readonly object HandleLock = new();
        public long LastBytes;
        public long LastTicks = Environment.TickCount64;
    }

    private readonly DownloadOptions _o;
    private readonly string _statePath;
    private readonly HttpClient _http;
    private readonly NetworkGate _net;
    private readonly RateLimiter _limit = new();
    private readonly ConcurrentDictionary<string, Runtime> _jobs = new();
    private readonly SemaphoreSlim _slots;
    private readonly Timer _tick;
    private readonly object _saveLock = new();
    private volatile bool _disposed;

    public event Action<DownloadJob>? Changed;

    public DownloadManager(string statePath, DownloadOptions? options = null)
    {
        _o = options ?? new DownloadOptions();
        _statePath = statePath;
        _limit.BytesPerSecond = _o.SpeedLimitBytesPerSec;
        _slots = new SemaphoreSlim(Math.Max(1, _o.MaxParallelJobs));
        _net = new NetworkGate(_o.WatchOsNetwork);
        _http = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.None, // byte ranges must address the raw bytes
            AllowAutoRedirect = true,
            MaxAutomaticRedirections = 10,
            ConnectTimeout = TimeSpan.FromSeconds(15),
            PooledConnectionIdleTimeout = TimeSpan.FromSeconds(30),
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            EnableMultipleHttp2Connections = true,
            MaxConnectionsPerServer = 16,
        })
        { Timeout = Timeout.InfiniteTimeSpan };

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(statePath))!);
        _tick = new Timer(_ => Tick(), null, _o.SaveInterval, _o.SaveInterval);
    }

    public long SpeedLimitBytesPerSec { get => _limit.BytesPerSecond; set => _limit.BytesPerSecond = value; }

    // ------------------------------------------------------------------ public API

    public IReadOnlyList<DownloadJob> List() => _jobs.Values.Select(r => r.Job).OrderBy(j => j.CreatedAt).ToList();

    public DownloadJob? Get(string id) => _jobs.TryGetValue(id, out var r) ? r.Job : null;

    public DownloadJob? FindOpen(string url, string dir) => _jobs.Values.Select(r => r.Job)
        .FirstOrDefault(j => j.Url == url && j.Directory == dir && j.Status != DownloadStatus.Completed);

    public DownloadJob Add(string url, string directory, int? connections = null, string? referer = null,
        string? cookie = null, string? userAgent = null, string? fileName = null)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
            throw new ArgumentException("Only http(s) URLs are supported.", nameof(url));
        var job = new DownloadJob
        {
            Url = url, Directory = Path.GetFullPath(directory), Referer = referer, Cookie = cookie, UserAgent = userAgent,
            Connections = Math.Clamp(connections ?? _o.DefaultConnections, 1, 16),
            FileName = string.IsNullOrWhiteSpace(fileName) ? "" : FileNames.Sanitize(fileName),
        };
        var rt = new Runtime(job);
        _jobs[job.Id] = rt;
        Start(rt);
        Save();
        return job;
    }

    public void Pause(string id)
    {
        if (!_jobs.TryGetValue(id, out var rt)) return;
        rt.UserStopped = true;
        rt.Cts?.Cancel();
    }

    public void Resume(string id)
    {
        if (!_jobs.TryGetValue(id, out var rt)) return;
        if (rt.Job.Status is DownloadStatus.Completed) return;
        Start(rt);
    }

    public async Task RemoveAsync(string id, bool deleteFiles)
    {
        if (!_jobs.TryRemove(id, out var rt)) return;
        rt.Removed = true;
        rt.UserStopped = true;
        rt.Cts?.Cancel();
        if (rt.Task is not null) { try { await rt.Task.ConfigureAwait(false); } catch { } }
        if (deleteFiles)
        {
            TryDelete(rt.Job.PartPath);
            if (rt.Job.Status == DownloadStatus.Completed) TryDelete(rt.Job.FinalPath);
        }
        Save();
    }

    /// <summary>Test/CLI helper: wait until the job is Completed or Failed.</summary>
    public async Task<DownloadJob> WaitAsync(string id, TimeSpan timeout, CancellationToken ct = default)
    {
        var rt = _jobs[id];
        var until = DateTime.UtcNow + timeout;
        while (rt.Job.Status is not (DownloadStatus.Completed or DownloadStatus.Failed))
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"Job {id} still {rt.Job.Status} after {timeout}");
            await Task.Delay(50, ct).ConfigureAwait(false);
        }
        return rt.Job;
    }

    /// <summary>Load previous state and (optionally) resume whatever was in flight.</summary>
    public void Load()
    {
        if (!File.Exists(_statePath)) return;
        List<DownloadJob>? jobs;
        try { jobs = JsonSerializer.Deserialize(File.ReadAllText(_statePath), BlazeJson.Default.ListDownloadJob); }
        catch { return; } // corrupt state file: start clean rather than crash the browser
        if (jobs is null) return;
        foreach (var j in jobs)
        {
            var rt = new Runtime(j);
            _jobs[j.Id] = rt;
            if (_o.AutoResumeOnLoad && j.Status is DownloadStatus.Queued or DownloadStatus.Probing
                or DownloadStatus.Downloading or DownloadStatus.WaitingForNetwork)
                Start(rt);
        }
    }

    public void WakeNetwork() => _net.Wake();

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await _tick.DisposeAsync().ConfigureAwait(false);
        foreach (var rt in _jobs.Values) rt.Cts?.Cancel();
        foreach (var rt in _jobs.Values) if (rt.Task is not null) { try { await rt.Task.ConfigureAwait(false); } catch { } }
        Save();
        _http.Dispose();
    }

    // ------------------------------------------------------------------ job lifecycle

    private void Start(Runtime rt)
    {
        lock (rt)
        {
            if (rt.Task is { IsCompleted: false }) return;
            rt.UserStopped = false;
            rt.Job.Error = null;
            rt.Cts = new CancellationTokenSource();
            var ct = rt.Cts.Token;
            rt.Task = Task.Run(() => RunAsync(rt, ct));
        }
    }

    private async Task RunAsync(Runtime rt, CancellationToken ct)
    {
        var job = rt.Job;
        SetStatus(job, DownloadStatus.Queued);
        try
        {
            await _slots.WaitAsync(ct).ConfigureAwait(false);
            try { await ExecuteAsync(rt, ct).ConfigureAwait(false); }
            finally { _slots.Release(); }
        }
        catch (OperationCanceledException)
        {
            // User pause -> Paused. Shutdown -> keep the in-flight status so the next start auto-resumes.
            if (rt.UserStopped && job.Status != DownloadStatus.Completed) SetStatus(job, DownloadStatus.Paused);
        }
        catch (FatalDownloadException ex) { job.Error = ex.Message; SetStatus(job, DownloadStatus.Failed); }
        catch (Exception ex) { job.Error = ex.Message; SetStatus(job, DownloadStatus.Failed); }
        finally { if (!rt.Removed && !_disposed) Save(); }
    }

    private async Task ExecuteAsync(Runtime rt, CancellationToken ct)
    {
        var job = rt.Job;
        for (int restart = 0; ; restart++)
        {
            if (job.Segments.Count > 0 && !File.Exists(job.PartPath) && job.DownloadedBytes > 0)
                ResetJob(job); // partial file vanished: cannot trust the saved progress

            if (job.Segments.Count == 0)
            {
                SetStatus(job, DownloadStatus.Probing);
                await ProbeAsync(job, ct).ConfigureAwait(false);
                PlanSegments(job);
            }

            try
            {
                SetStatus(job, DownloadStatus.Downloading);
                await TransferAsync(rt, ct).ConfigureAwait(false);
                break;
            }
            catch (ContentChangedException ex)
            {
                if (restart >= 3) throw new FatalDownloadException("The remote file keeps changing: " + ex.Message);
                job.Notice = "Remote file changed; restarting: " + ex.Message;
                ResetJob(job);
            }
        }
        FinalizeJob(job);
    }

    private static void ResetJob(DownloadJob job)
    {
        lock (job.Segments) job.Segments.Clear();
        TryDelete(job.PartPath);
        job.TotalBytes = -1;
        job.ETag = null;
        job.LastModified = null;
    }

    private void FinalizeJob(DownloadJob job)
    {
        long len = new FileInfo(job.PartPath).Length;
        if (job.TotalBytes >= 0 && len != job.TotalBytes)
            throw new FatalDownloadException($"Size mismatch: expected {job.TotalBytes}, got {len}");
        var final = FileNames.Unique(job.Directory, job.FileName);
        File.Move(job.PartPath, final);
        job.FileName = Path.GetFileName(final);
        job.TotalBytes = len;
        lock (job.Segments) job.Segments.Clear();
        job.CompletedAt = DateTimeOffset.UtcNow;
        job.Cookie = null;       // privacy: a finished download keeps no session cookie
        job.Referer = null;
        job.BytesPerSecond = 0;
        SetStatus(job, DownloadStatus.Completed);
    }

    // ------------------------------------------------------------------ probe

    private async Task ProbeAsync(DownloadJob job, CancellationToken ct)
    {
        for (int attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var req = NewRequest(job);
                req.Headers.Range = new RangeHeaderValue(0, 0);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.PartialContent)
                {
                    job.Resumable = true;
                    job.TotalBytes = resp.Content.Headers.ContentRange?.Length ?? -1;
                    if (job.TotalBytes < 0) job.Resumable = false;
                }
                else if (resp.StatusCode == HttpStatusCode.OK)
                {
                    job.Resumable = false; // server ignores Range
                    job.TotalBytes = resp.Content.Headers.ContentLength ?? -1;
                }
                else if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                {
                    job.Resumable = false; job.TotalBytes = 0; // empty file
                }
                else Classify(resp);

                var etag = resp.Headers.ETag;
                job.ETag = etag is { IsWeak: false } ? etag.Tag : null;
                job.LastModified = resp.Content.Headers.LastModified?.ToString("R");
                if (string.IsNullOrEmpty(job.FileName)) job.FileName = ChooseFileName(resp, job.Url);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (IsTransient(ex))
            {
                SetWaiting(job, ex);
                await _net.DelayAsync(Backoff(attempt, ex), ct).ConfigureAwait(false);
            }
        }
    }

    private static string ChooseFileName(HttpResponseMessage r, string url)
    {
        var cd = r.Content.Headers.ContentDisposition;
        string? n = cd?.FileNameStar ?? cd?.FileName;
        if (string.IsNullOrWhiteSpace(n))
        {
            var uri = r.RequestMessage?.RequestUri ?? new Uri(url);
            n = Uri.UnescapeDataString(Path.GetFileName(uri.AbsolutePath));
        }
        return FileNames.Sanitize(n);
    }

    private void PlanSegments(DownloadJob job)
    {
        lock (job.Segments)
        {
            job.Segments.Clear();
            if (job.Resumable && job.TotalBytes > 0)
            {
                int n = (int)Math.Clamp(job.TotalBytes / _o.MinSegmentBytes, 1, job.Connections);
                long size = (job.TotalBytes + n - 1) / n;
                for (long start = 0; start < job.TotalBytes; start += size)
                    job.Segments.Add(new Segment { Start = start, End = Math.Min(start + size, job.TotalBytes) - 1 });
            }
            else
            {
                // single stream; unknown length uses a huge sentinel End
                job.Segments.Add(new Segment { Start = 0, End = job.TotalBytes > 0 ? job.TotalBytes - 1 : long.MaxValue / 2 });
            }
        }
    }

    // ------------------------------------------------------------------ transfer

    private async Task TransferAsync(Runtime rt, CancellationToken ct)
    {
        var job = rt.Job;
        Directory.CreateDirectory(job.Directory);
        SafeFileHandle fh;
        try
        {
            fh = File.OpenHandle(job.PartPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.Read, FileOptions.Asynchronous);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new FatalDownloadException("Cannot open destination: " + ex.Message);
        }
        lock (rt.HandleLock) rt.Handle = fh;
        try
        {
            if (job.TotalBytes > 0 && RandomAccess.GetLength(fh) != job.TotalBytes)
                try { RandomAccess.SetLength(fh, job.TotalBytes); }
                catch (IOException ex) { throw new FatalDownloadException("Cannot allocate file: " + ex.Message); }

            if (!job.Resumable) { await StreamAsync(job, fh, ct).ConfigureAwait(false); return; }

            using var work = CancellationTokenSource.CreateLinkedTokenSource(ct);
            ExceptionDispatchInfo? fault = null;
            var workers = Enumerable.Range(0, job.Connections).Select(_ => Task.Run(async () =>
            {
                try
                {
                    while (!work.IsCancellationRequested)
                    {
                        var seg = Claim(job);
                        if (seg is null) break;
                        try { await DownloadSegmentAsync(job, seg, fh, work.Token).ConfigureAwait(false); }
                        finally { seg.Assigned = false; }
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref fault, ExceptionDispatchInfo.Capture(ex), null);
                    work.Cancel();
                }
            })).ToArray();
            await Task.WhenAll(workers).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            fault?.Throw();
            lock (job.Segments)
                if (job.Segments.Any(s => s.Remaining > 0)) throw new IOException("Transfer ended with missing ranges");
            try { RandomAccess.FlushToDisk(fh); } catch (IOException) { }
        }
        finally
        {
            lock (rt.HandleLock) { rt.Handle = null; fh.Dispose(); }
        }
    }

    /// <summary>Pick an unfinished segment; if none is free, split the biggest one in flight (work stealing).</summary>
    private Segment? Claim(DownloadJob job)
    {
        lock (job.Segments)
        {
            foreach (var s in job.Segments)
                if (!s.Assigned && s.Remaining > 0) { s.Assigned = true; return s; }

            Segment? best = null;
            foreach (var s in job.Segments)
                if (s.Assigned && s.Remaining >= 2 * _o.MinSegmentBytes && (best is null || s.Remaining > best.Remaining)) best = s;
            if (best is null) return null;

            long mid = best.Position + best.Remaining / 2;
            var stolen = new Segment { Start = mid, End = best.End, Assigned = true };
            best.End = mid - 1;
            job.Segments.Add(stolen);
            return stolen;
        }
    }

    private async Task DownloadSegmentAsync(DownloadJob job, Segment seg, SafeFileHandle fh, CancellationToken ct)
    {
        int fails = 0;
        while (seg.Remaining > 0)
        {
            ct.ThrowIfCancellationRequested();
            var buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                using var req = NewRequest(job);
                req.Headers.Range = new RangeHeaderValue(seg.Position, seg.End);
                var validator = job.ETag ?? job.LastModified;
                if (validator is not null) req.Headers.TryAddWithoutValidation("If-Range", validator);

                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.StatusCode == HttpStatusCode.OK)
                    throw new ContentChangedException("server answered 200 to a range request");
                if (resp.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable)
                    throw new ContentChangedException("416 Range Not Satisfiable");
                if (resp.StatusCode != HttpStatusCode.PartialContent) Classify(resp);
                if (resp.Content.Headers.ContentRange?.From is long from && from != seg.Position)
                    throw new ContentChangedException("server returned a different range");

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                while (seg.Remaining > 0)
                {
                    idle.CancelAfter(_o.IdleTimeout);
                    int want = (int)Math.Min(buf.Length, seg.Remaining);
                    int n;
                    try { n = await stream.ReadAsync(buf.AsMemory(0, want), idle.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    { throw new IOException("No data received (idle timeout)"); }
                    if (n == 0) throw new IOException("Connection closed before the range completed");

                    await _limit.AcquireAsync(n, ct).ConfigureAwait(false);
                    await WriteAsync(fh, buf.AsMemory(0, n), seg.Position, ct).ConfigureAwait(false);
                    seg.Downloaded += n;
                    fails = 0;
                    if (job.Status == DownloadStatus.WaitingForNetwork) SetStatus(job, DownloadStatus.Downloading);
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (IsTransient(ex))
            {
                SetWaiting(job, ex);
                await _net.DelayAsync(Backoff(fails++, ex), ct).ConfigureAwait(false);
            }
            finally { ArrayPool<byte>.Shared.Return(buf); }
        }
    }

    /// <summary>Servers that cannot resume (or do not report a size): one stream, restart from zero after a drop.</summary>
    private async Task StreamAsync(DownloadJob job, SafeFileHandle fh, CancellationToken ct)
    {
        Segment seg;
        lock (job.Segments) seg = job.Segments[0];
        int fails = 0;
        for (; ; )
        {
            ct.ThrowIfCancellationRequested();
            var buf = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                seg.Downloaded = 0;
                using var req = NewRequest(job);
                using var resp = await _http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (resp.StatusCode != HttpStatusCode.OK) Classify(resp);
                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
                for (; ; )
                {
                    idle.CancelAfter(_o.IdleTimeout);
                    int n;
                    try { n = await stream.ReadAsync(buf.AsMemory(), idle.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
                    { throw new IOException("No data received (idle timeout)"); }
                    if (n == 0) break;
                    await _limit.AcquireAsync(n, ct).ConfigureAwait(false);
                    await WriteAsync(fh, buf.AsMemory(0, n), seg.Position, ct).ConfigureAwait(false);
                    seg.Downloaded += n;
                    if (job.Status == DownloadStatus.WaitingForNetwork) SetStatus(job, DownloadStatus.Downloading);
                }
                if (job.TotalBytes >= 0 && seg.Downloaded < job.TotalBytes)
                    throw new IOException("Connection closed before the file completed");
                job.TotalBytes = seg.Downloaded;
                seg.End = seg.Downloaded - 1;
                RandomAccess.SetLength(fh, seg.Downloaded);
                RandomAccess.FlushToDisk(fh);
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex) when (IsTransient(ex))
            {
                SetWaiting(job, ex);
                job.Notice = "Server cannot resume; restarting this file from the beginning";
                await _net.DelayAsync(Backoff(fails++, ex), ct).ConfigureAwait(false);
            }
            finally { ArrayPool<byte>.Shared.Return(buf); }
        }
    }

    private static async Task WriteAsync(SafeFileHandle fh, ReadOnlyMemory<byte> data, long offset, CancellationToken ct)
    {
        try { await RandomAccess.WriteAsync(fh, data, offset, ct).ConfigureAwait(false); }
        catch (IOException ex) { throw new FatalDownloadException("Disk write failed: " + ex.Message); }
    }

    // ------------------------------------------------------------------ http helpers

    private HttpRequestMessage NewRequest(DownloadJob job)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, job.Url);
        req.Headers.TryAddWithoutValidation("User-Agent", job.UserAgent ?? _o.UserAgent);
        req.Headers.TryAddWithoutValidation("Accept-Encoding", "identity");
        req.Headers.TryAddWithoutValidation("Accept", "*/*");
        if (!string.IsNullOrEmpty(job.Referer)) req.Headers.TryAddWithoutValidation("Referer", job.Referer);
        if (!string.IsNullOrEmpty(job.Cookie)) req.Headers.TryAddWithoutValidation("Cookie", job.Cookie);
        return req;
    }

    private static void Classify(HttpResponseMessage resp)
    {
        int code = (int)resp.StatusCode;
        if (code is 408 or 425 or 429 or >= 500)
        {
            TimeSpan? ra = resp.Headers.RetryAfter?.Delta ??
                (resp.Headers.RetryAfter?.Date is { } d ? d - DateTimeOffset.UtcNow : null);
            throw new TransientHttpException($"HTTP {code}", ra);
        }
        throw new FatalDownloadException($"HTTP {code} {resp.ReasonPhrase}");
    }

    private static bool IsTransient(Exception ex) =>
        ex is HttpRequestException or IOException or System.Net.Sockets.SocketException
            or TimeoutException or TransientHttpException or OperationCanceledException;

    private TimeSpan Backoff(int attempt, Exception ex)
    {
        double s = Math.Min(_o.MaxBackoff.TotalSeconds, _o.BaseBackoff.TotalSeconds * Math.Pow(2, Math.Min(attempt, 8)));
        s *= 0.75 + Random.Shared.NextDouble() * 0.5; // jitter
        var delay = TimeSpan.FromSeconds(s);
        if (ex is TransientHttpException { RetryAfter: { } ra } && ra > delay) delay = ra < TimeSpan.FromMinutes(2) ? ra : TimeSpan.FromMinutes(2);
        return delay;
    }

    private void SetWaiting(DownloadJob job, Exception ex)
    {
        job.Notice = ex.Message;
        if (job.Status == DownloadStatus.Downloading) SetStatus(job, DownloadStatus.WaitingForNetwork);
    }

    private void SetStatus(DownloadJob job, DownloadStatus status)
    {
        if (job.Status == status) return;
        job.Status = status;
        try { Changed?.Invoke(job); } catch { }
    }

    private static void TryDelete(string path) { try { File.Delete(path); } catch { } }

    // ------------------------------------------------------------------ persistence + speed

    private void Tick()
    {
        if (_disposed) return;
        long now = Environment.TickCount64;
        foreach (var rt in _jobs.Values)
        {
            long bytes = rt.Job.DownloadedBytes;
            double dt = Math.Max(1, now - rt.LastTicks) / 1000.0;
            double inst = Math.Max(0, bytes - rt.LastBytes) / dt;
            rt.Job.BytesPerSecond = rt.Job.Status == DownloadStatus.Downloading ? rt.Job.BytesPerSecond * 0.6 + inst * 0.4 : 0;
            rt.LastBytes = bytes; rt.LastTicks = now;
        }
        Save();
    }

    private void Save()
    {
        lock (_saveLock)
        {
            try
            {
                // 1) snapshot progress, 2) make sure those bytes are really on disk, 3) only then persist the snapshot.
                var all = _jobs.Values.Select(r => r.Job).OrderBy(j => j.CreatedAt).ToList();
                string json = SerializeSnapshot(all);
                foreach (var rt in _jobs.Values)
                    lock (rt.HandleLock)
                        if (rt.Handle is { IsClosed: false } h) { try { RandomAccess.FlushToDisk(h); } catch { } }

                var tmp = _statePath + ".tmp";
                File.WriteAllText(tmp, json);
                // privacy: the state may hold cookies of in-flight authenticated downloads, so only the owner may read it
                if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                File.Move(tmp, _statePath, overwrite: true);
            }
            catch { /* never take the browser down because the state file is busy */ }
        }
    }

    private static string SerializeSnapshot(List<DownloadJob> jobs)
    {
        // Keep the completed history short (newest 200).
        var dropped = jobs.Where(j => j.Status == DownloadStatus.Completed)
            .OrderByDescending(j => j.CompletedAt).Skip(200).Select(j => j.Id).ToHashSet();
        var sb = new System.Text.StringBuilder("[");
        foreach (var j in jobs)
        {
            if (dropped.Contains(j.Id)) continue;
            // Serialize under the segment lock so ranges and progress are mutually consistent.
            string one;
            lock (j.Segments) one = JsonSerializer.Serialize(j, BlazeJson.Default.DownloadJob);
            if (sb.Length > 1) sb.Append(',');
            sb.Append(one);
        }
        return sb.Append(']').ToString();
    }
}
