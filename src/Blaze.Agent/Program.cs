using Blaze.Core.Browser;
using Blaze.Core.Downloads;
using Blaze.Core.Updates;

// blaze-agent get <url> [--dir DIR] [--name FILE] [--connections N] [--limit KBps] [--state FILE]
// blaze-agent addon-install (--socket PATH | --port N) --path DIR [--wait SECONDS]   (temporary add-on over Firefox's debugger channel)
// blaze-agent list [--state FILE]
// blaze-agent rebrand --dir FIREFOX_DIR [--name Blaze] [--css FILE] [--assets DIR_WITH_SVGS]   -> REBRANDED | UNCHANGED   (engine presents itself as Blaze)
// blaze-agent update check [--force] [--pre]   -> UPDATE_AVAILABLE <v> | UP_TO_DATE | SKIPPED | NOT_DUE ; writes update/{version,notes.txt,url}
// blaze-agent update apply --target PATH        -> download (resumable) + verify SHA-256 + atomic swap, previous kept as .old
// blaze-agent update skip <version>             -> never offer that version again
// Re-running `get` with the same URL+dir resumes the previous download.

static string Opt(string[] a, string name, string def)
{
    int i = Array.IndexOf(a, name);
    return i >= 0 && i + 1 < a.Length ? a[i + 1] : def;
}

static string DefaultStateDir()
{
    var xdg = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
    var root = string.IsNullOrEmpty(xdg)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share")
        : xdg;
    return Path.Combine(root, "blaze");
}

static string Size(double b) => b switch
{
    >= 1L << 30 => $"{b / (1L << 30):F2} GB",
    >= 1L << 20 => $"{b / (1L << 20):F1} MB",
    >= 1L << 10 => $"{b / (1L << 10):F0} KB",
    _ => $"{b:F0} B",
};

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    Console.WriteLine("usage: blaze-agent get <url> [--dir DIR] [--name FILE] [--connections N] [--limit KBps] [--state FILE] [--progress-file FILE] [--quiet]\n       blaze-agent list [--state FILE]\n       blaze-agent addon-install (--socket PATH | --port N) --path DIR [--wait SECONDS]");
    return 2;
}

if (args[0] == "rebrand")
{
    try
    {
        var css = Opt(args, "--css", "");
        var assets = Opt(args, "--assets", "");
        var r = Rebrand.Apply(Path.GetFullPath(Opt(args, "--dir", ".")), Opt(args, "--name", "Blaze"),
            css.Length > 0 ? File.ReadAllText(css) : null, assets.Length > 0 ? Path.GetFullPath(assets) : null);
        Console.WriteLine(r == RebrandResult.Rebranded ? "REBRANDED" : "UNCHANGED");
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine("rebrand failed: " + ex.Message); return 1; }
}

if (args[0] == "version") { Console.WriteLine(UpdateService.CurrentVersion()); return 0; }

if (args[0] == "update")
{
    var dataDir = Path.Combine(DefaultStateDir(), "update");
    Directory.CreateDirectory(dataDir);
    var repo = Environment.GetEnvironmentVariable("BLAZE_GITHUB_REPO") ?? "vezzulab/blaze";
    var api = Environment.GetEnvironmentVariable("BLAZE_GITHUB_API");
    var svc = new UpdateService(repo, ReleaseVerifier.Default, api, allowInsecure: api is not null && api.StartsWith("http://"));
    var sub = args.Length > 1 ? args[1] : "check";
    var current = Environment.GetEnvironmentVariable("BLAZE_CURRENT_VERSION") ?? UpdateService.CurrentVersion();
    var skipFile = Path.Combine(dataDir, "skipped");
    var stampFile = Path.Combine(dataDir, "last-check");

    if (sub == "skip")
    {
        if (args.Length < 3) { Console.Error.WriteLine("usage: blaze-agent update skip <version>"); return 2; }
        File.WriteAllText(skipFile, args[2].TrimStart('v'));
        Console.WriteLine("SKIPPED " + args[2]);
        return 0;
    }

    SemVer.TryParse(current, out var cur);
    bool includePre = cur.Major == 0 || args.Contains("--pre");
    try
    {
        if (sub == "check")
        {
            bool force = args.Contains("--force");
            if (!force && File.Exists(stampFile) && long.TryParse(File.ReadAllText(stampFile), out var last)
                && DateTimeOffset.UtcNow.ToUnixTimeSeconds() - last < 24 * 3600) { Console.WriteLine("NOT_DUE"); return 0; }
            ReleaseInfo? rel;
            try { rel = await svc.CheckAsync(current, includePre); }
            catch (RateLimitedException) { Console.WriteLine("RATE_LIMITED"); return 0; }   // no stamp: try again next start
            File.WriteAllText(stampFile, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString());
            foreach (var f in new[] { "version", "notes.txt", "url" }) { try { File.Delete(Path.Combine(dataDir, f)); } catch { } }
            if (rel is null) { Console.WriteLine("UP_TO_DATE"); return 0; }
            if (!force && File.Exists(skipFile) && File.ReadAllText(skipFile).Trim() == rel.Version) { Console.WriteLine("SKIPPED"); return 0; }
            File.WriteAllText(Path.Combine(dataDir, "version"), rel.Version);
            File.WriteAllText(Path.Combine(dataDir, "notes.txt"), rel.Notes);
            File.WriteAllText(Path.Combine(dataDir, "url"), rel.HtmlUrl);
            Console.WriteLine("UPDATE_AVAILABLE " + rel.Version);
            return 0;
        }
        if (sub == "apply")
        {
            var target = Opt(args, "--target", "");
            if (target.Length == 0) { Console.Error.WriteLine("--target PATH required"); return 2; }
            var rel = await svc.CheckAsync(current, includePre);
            if (rel is null) { Console.WriteLine("UP_TO_DATE"); return 0; }
            Console.Error.WriteLine($"Downloading Blaze {rel.Version} ({rel.AssetName})...");
            var file = await svc.DownloadAsync(rel, Path.Combine(dataDir, "download"), j =>
                Console.Error.Write($"\r{(j.TotalBytes > 0 ? 100.0 * j.DownloadedBytes / j.TotalBytes : 0),5:F1}%  {Size(j.DownloadedBytes)}/{(j.TotalBytes >= 0 ? Size(j.TotalBytes) : "?")}  {Size(j.BytesPerSecond)}/s  [{j.Status}]   "));
            Console.Error.WriteLine();
            UpdateService.ReplaceFile(file, Path.GetFullPath(target));
            try { File.Delete(file); } catch { }
            Console.WriteLine("UPDATED " + rel.Version);
            return 0;
        }
        Console.Error.WriteLine("usage: blaze-agent update check|apply|skip"); return 2;
    }
    catch (Exception ex) { Console.Error.WriteLine("update failed: " + ex.Message); return 1; }
}

if (args[0] == "addon-install")
{
    try
    {
        var wait = TimeSpan.FromSeconds(int.Parse(Opt(args, "--wait", "60")));
        var addon = Path.GetFullPath(Opt(args, "--path", "."));
        var socket = Opt(args, "--socket", "");
        var id = socket.Length > 0
            ? await DevToolsClient.InstallTemporaryAddonAsync(socket, addon, wait)
            : await DevToolsClient.InstallTemporaryAddonAsync(int.Parse(Opt(args, "--port", "6000")), addon, wait);
        Console.WriteLine(id);
        return 0;
    }
    catch (Exception ex) { Console.Error.WriteLine("addon-install failed: " + ex.Message); return 1; }
}

var statePath = Opt(args, "--state", Path.Combine(DefaultStateDir(), "jobs.json"));
var options = new DownloadOptions { SpeedLimitBytesPerSec = long.Parse(Opt(args, "--limit", "0")) * 1024 };
await using var manager = new DownloadManager(statePath, options);
manager.Load();

if (args[0] == "list")
{
    foreach (var j in manager.List())
        Console.WriteLine($"{j.Id[..8]}  {j.Status,-17} {Size(j.DownloadedBytes),9} / {(j.TotalBytes >= 0 ? Size(j.TotalBytes) : "?"),9}  {j.FileName}");
    return 0;
}

if (args[0] != "get" || args.Length < 2) { Console.Error.WriteLine("missing url"); return 2; }

var url = args[1];
var dir = Path.GetFullPath(Opt(args, "--dir", Environment.CurrentDirectory));
var job = manager.FindOpen(url, dir) ?? manager.Add(url, dir, int.TryParse(Opt(args, "--connections", ""), out var c) ? c : null,
    fileName: Opt(args, "--name", ""));
if (job.Status == DownloadStatus.Paused || job.Status == DownloadStatus.Failed) manager.Resume(job.Id);

bool quiet = args.Contains("--quiet");
var progressFile = Opt(args, "--progress-file", "");
using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

try
{
    while (job.Status is not (DownloadStatus.Completed or DownloadStatus.Failed))
    {
        await Task.Delay(500, cts.Token);
        double pct = job.TotalBytes > 0 ? 100.0 * job.DownloadedBytes / job.TotalBytes : 0;
        var eta = job.BytesPerSecond > 1 && job.TotalBytes > 0 ? TimeSpan.FromSeconds((job.TotalBytes - job.DownloadedBytes) / job.BytesPerSecond) : (TimeSpan?)null;
        if (progressFile.Length > 0) { try { File.WriteAllText(progressFile, $"{(int)pct} {job.DownloadedBytes} {job.TotalBytes}\n"); } catch { } }
        if (!quiet) Console.Error.Write($"\r{pct,5:F1}%  {Size(job.DownloadedBytes)}/{(job.TotalBytes >= 0 ? Size(job.TotalBytes) : "?")}  {Size(job.BytesPerSecond)}/s  " +
                            $"ETA {(eta is { } t ? t.ToString(@"hh\:mm\:ss") : "--:--:--")}  [{job.Status}]{(job.Status == DownloadStatus.WaitingForNetwork ? " " + job.Notice : "")}   ");
    }
    if (!quiet) Console.Error.WriteLine();
}
catch (OperationCanceledException) { Console.Error.WriteLine("\nStopped. Run the same command to resume."); return 130; }

if (job.Status == DownloadStatus.Failed) { Console.Error.WriteLine("FAILED: " + job.Error); return 1; }
Console.WriteLine(job.FinalPath);
return 0;
