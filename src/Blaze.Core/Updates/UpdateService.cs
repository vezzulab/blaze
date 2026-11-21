using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Blaze.Core.Downloads;

namespace Blaze.Core.Updates;

/// <summary>GitHub answered 403/429: not an error in Blaze, just "ask again later" (60 anonymous requests/hour/IP).</summary>
public sealed class RateLimitedException() : Exception("GitHub rate limit reached; will retry later.");

public sealed record ReleaseInfo(string Version, string Tag, bool Prerelease, string AssetName, string AssetUrl, string? ChecksumsUrl, string? SignatureUrl, string HtmlUrl, string Notes);

/// <summary>Semantic version compare: 1.2.3-alpha.1 &lt; 1.2.3. Leading "v" allowed.</summary>
public static class SemVer
{
    public static bool TryParse(string? s, out (int Major, int Minor, int Patch, string Pre) v)
    {
        v = default;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var m = Regex.Match(s.Trim(), @"^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.\-]+))?(?:\+.*)?$");
        if (!m.Success) return false;
        v = (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), int.Parse(m.Groups[3].Value), m.Groups[4].Value);
        return true;
    }

    public static int Compare(string a, string b)
    {
        if (!TryParse(a, out var x) || !TryParse(b, out var y)) throw new FormatException($"Bad version: '{a}' / '{b}'");
        int c = x.Major.CompareTo(y.Major); if (c != 0) return c;
        c = x.Minor.CompareTo(y.Minor); if (c != 0) return c;
        c = x.Patch.CompareTo(y.Patch); if (c != 0) return c;
        if (x.Pre.Length == 0 && y.Pre.Length == 0) return 0;
        if (x.Pre.Length == 0) return 1;      // a release is newer than its pre-release
        if (y.Pre.Length == 0) return -1;
        var xp = x.Pre.Split('.'); var yp = y.Pre.Split('.');
        for (int i = 0; i < Math.Max(xp.Length, yp.Length); i++)
        {
            if (i >= xp.Length) return -1;
            if (i >= yp.Length) return 1;
            bool xn = int.TryParse(xp[i], out var xi), yn = int.TryParse(yp[i], out var yi);
            c = xn && yn ? xi.CompareTo(yi) : xn ? -1 : yn ? 1 : string.CompareOrdinal(xp[i], yp[i]);
            if (c != 0) return c;
        }
        return 0;
    }
}

/// <summary>
/// Checks GitHub Releases for a newer Blaze AppImage, downloads it with the resumable engine, verifies SHA-256
/// against the release's SHA256SUMS, and swaps it in atomically (keeping the previous one as ".old" for rollback).
/// Checking is cheap (~1 KB) and automatic; downloading is always an explicit action (users may be on a metered link).
/// </summary>
public sealed class UpdateService
{
    private static readonly Regex AssetPattern = new(@"^Blaze-.*-x86_64\.AppImage$", RegexOptions.Compiled);
    private readonly HttpClient _http;
    private readonly string _repo;
    private readonly string _apiBase;
    private readonly bool _allowInsecure;
    private readonly ReleaseVerifier _verifier;

    public UpdateService(string repo, ReleaseVerifier verifier, string? apiBase = null, HttpClient? http = null, bool allowInsecure = false)
    {
        _repo = repo;
        _verifier = verifier;
        _apiBase = (apiBase ?? "https://api.github.com").TrimEnd('/');
        _allowInsecure = allowInsecure;
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
    }

    public static string CurrentVersion()
    {
        var v = typeof(UpdateService).Assembly.GetName().Version;
        return v is null ? "0.0.0" : $"{v.Major}.{v.Minor}.{Math.Max(0, v.Build)}";
    }

    /// <summary>Newest release strictly newer than <paramref name="current"/>, or null. Drafts are ignored.</summary>
    public async Task<ReleaseInfo?> CheckAsync(string current, bool includePrereleases, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{_apiBase}/repos/{_repo}/releases?per_page=15");
        req.Headers.TryAddWithoutValidation("User-Agent", "Blaze-Updater");
        req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if ((int)resp.StatusCode is 403 or 429) throw new RateLimitedException();
        resp.EnsureSuccessStatusCode();
        await using var body = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(body, cancellationToken: ct).ConfigureAwait(false);

        ReleaseInfo? best = null;
        foreach (var rel in doc.RootElement.EnumerateArray())
        {
            if (rel.TryGetProperty("draft", out var d) && d.GetBoolean()) continue;
            bool pre = rel.TryGetProperty("prerelease", out var p) && p.GetBoolean();
            if (pre && !includePrereleases) continue;
            var tag = rel.GetProperty("tag_name").GetString() ?? "";
            if (!SemVer.TryParse(tag, out _)) continue;
            var version = tag.TrimStart('v');
            if (SemVer.Compare(version, current) <= 0) continue;
            if (best is not null && SemVer.Compare(version, best.Version) <= 0) continue;

            string? asset = null, assetUrl = null, sums = null, sig = null;
            foreach (var a in rel.GetProperty("assets").EnumerateArray())
            {
                var name = a.GetProperty("name").GetString() ?? "";
                var url = a.GetProperty("browser_download_url").GetString() ?? "";
                if (AssetPattern.IsMatch(name)) { asset = name; assetUrl = url; }
                else if (name == "SHA256SUMS") sums = url;
                else if (name == "SHA256SUMS.sig") sig = url;
            }
            if (asset is null || assetUrl is null || !IsTrusted(assetUrl) || (sums is not null && !IsTrusted(sums)) || (sig is not null && !IsTrusted(sig))) continue;
            best = new ReleaseInfo(version, tag, pre, asset, assetUrl, sums, sig, rel.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "",
                TrimNotes(rel.TryGetProperty("body", out var bd) && bd.ValueKind == JsonValueKind.String ? bd.GetString() : null));
        }
        return best;
    }

    /// <summary>Release notes shown to the user before they decide: normalised and capped so a dialog stays readable.</summary>
    public static string TrimNotes(string? body, int maxLines = 14, int maxChars = 1200)
    {
        if (string.IsNullOrWhiteSpace(body)) return "";
        var lines = body.Replace("\r", "").Split('\n').Select(l => l.TrimEnd()).Where(l => l.Length > 0).ToList();
        var text = string.Join("\n", lines.Take(maxLines));
        if (lines.Count > maxLines || text.Length > maxChars) text = text[..Math.Min(text.Length, maxChars)].TrimEnd() + "\n…";
        return text;
    }

    private bool IsTrusted(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttps || (_allowInsecure && u.Scheme == Uri.UriSchemeHttp));

    /// <summary>Downloads (resumable) and verifies. Returns the path of the verified file. Throws if the checksum is missing or wrong.</summary>
    public async Task<string> DownloadAsync(ReleaseInfo release, string directory, Action<DownloadJob>? progress = null,
        TimeSpan? timeout = null, CancellationToken ct = default)
    {
        if (release.ChecksumsUrl is null) throw new InvalidOperationException("Release has no SHA256SUMS; refusing to install an unverifiable update.");
        if (release.SignatureUrl is null) throw new InvalidOperationException("Release has no SHA256SUMS.sig signature; refusing to install an unsigned update.");
        Directory.CreateDirectory(directory);

        var sumsBytes = await GetBytesAsync(release.ChecksumsUrl, ct).ConfigureAwait(false);
        var sigBytes = await GetBytesAsync(release.SignatureUrl, ct).ConfigureAwait(false);
        if (!_verifier.Verify(sumsBytes, sigBytes))
            throw new InvalidDataException("The release signature is INVALID: SHA256SUMS was not signed by the Blaze maintainer. Update refused.");
        var expected = ParseChecksum(System.Text.Encoding.UTF8.GetString(sumsBytes), release.AssetName)
            ?? throw new InvalidOperationException($"SHA256SUMS has no entry for {release.AssetName}.");

        await using var manager = new DownloadManager(Path.Combine(directory, "update-jobs.json"),
            new DownloadOptions { WatchOsNetwork = true, AutoResumeOnLoad = true });
        manager.Load();
        var job = manager.FindOpen(release.AssetUrl, directory) ?? manager.Add(release.AssetUrl, directory, fileName: release.AssetName);
        if (job.Status is DownloadStatus.Paused or DownloadStatus.Failed) manager.Resume(job.Id);

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromHours(6));
        while (job.Status is not (DownloadStatus.Completed or DownloadStatus.Failed))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("Update download timed out.");
            progress?.Invoke(job);
            await Task.Delay(500, ct).ConfigureAwait(false);
        }
        if (job.Status == DownloadStatus.Failed) throw new IOException("Update download failed: " + job.Error);

        var path = job.FinalPath;
        var actual = await Sha256Async(path, ct).ConfigureAwait(false);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            try { File.Delete(path); } catch { }
            throw new InvalidDataException($"Checksum mismatch for {release.AssetName}: expected {expected}, got {actual}. The file was deleted.");
        }
        return path;
    }

    private async Task<byte[]> GetBytesAsync(string url, CancellationToken ct)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("User-Agent", "Blaze-Updater");
        using var r = await _http.SendAsync(req, ct).ConfigureAwait(false);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
    }

    public static string? ParseChecksum(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var m = Regex.Match(line.Trim(), @"^([0-9a-fA-F]{64})\s+\*?(?:\./)?(.+)$");
            if (m.Success && m.Groups[2].Value.Trim() == fileName) return m.Groups[1].Value;
        }
        return null;
    }

    public static async Task<string> Sha256Async(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
    }

    /// <summary>Atomic swap: target.new -> target, previous target kept as target.old (one-step rollback).</summary>
    public static void ReplaceFile(string verifiedFile, string target)
    {
        var tmp = target + ".new";
        var old = target + ".old";
        File.Copy(verifiedFile, tmp, overwrite: true);
        File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                                  UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
        if (File.Exists(target)) File.Move(target, old, overwrite: true);   // running AppImages keep their inode: safe on Linux
        File.Move(tmp, target);
    }
}
