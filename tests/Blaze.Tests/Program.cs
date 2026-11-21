using Blaze.Core.Downloads;
using Blaze.Core.Updates;
using Blaze.Tests;

// Standalone server for the kill -9 script:  dotnet run -- serve <port> <sizeMB> <throttleKBps>
if (args.Length > 0 && args[0] == "serve")
{
    var srv = new TestServer { Size = long.Parse(args[2]) << 20, ThrottleBytesPerSec = int.Parse(args[3]) * 1024 };
    await srv.StartAsync(int.Parse(args[1]));
    Console.WriteLine($"serving {srv.Url} sha256={TestServer.Sha(srv.Size, srv.Seed)}");
    await Task.Delay(Timeout.Infinite);
}

int failed = 0;
async Task Test(string name, Func<Task> body)
{
    var sw = System.Diagnostics.Stopwatch.StartNew();
    try { await body(); Console.WriteLine($"PASS  {name}  ({sw.ElapsedMilliseconds} ms)"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL  {name}: {ex.Message}"); }
}
void Check(bool ok, string msg) { if (!ok) throw new Exception(msg); }

string Tmp() { var d = Path.Combine(Path.GetTempPath(), "blaze-test-" + Guid.NewGuid().ToString("N")[..8]); Directory.CreateDirectory(d); return d; }
DownloadOptions Fast() => new()
{
    BaseBackoff = TimeSpan.FromMilliseconds(100), MaxBackoff = TimeSpan.FromMilliseconds(500),
    SaveInterval = TimeSpan.FromMilliseconds(200), IdleTimeout = TimeSpan.FromSeconds(5), WatchOsNetwork = false,
};

await Test("1. basic 4-connection download, hash matches", async () =>
{
    await using var s = new TestServer { Size = 9L << 20 };
    await s.StartAsync();
    var d = Tmp();
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), Fast());
    var j = m.Add(s.Url, d);
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(30));
    Check(j.Status == DownloadStatus.Completed, $"status {j.Status} {j.Error}");
    Check(j.FileName == "test.bin", $"filename {j.FileName}");
    Check(TestServer.FileSha(j.FinalPath) == TestServer.Sha(s.Size, s.Seed), "hash mismatch");
});

await Test("2. connection dropped every 300 KB, still completes intact", async () =>
{
    await using var s = new TestServer { Size = 6L << 20, DropAfterBytes = 300 * 1024 };
    await s.StartAsync();
    var d = Tmp();
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), Fast());
    var j = m.Add(s.Url, d);
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(60));
    Check(j.Status == DownloadStatus.Completed, $"status {j.Status} {j.Error}");
    Check(TestServer.FileSha(j.FinalPath) == TestServer.Sha(s.Size, s.Seed), "hash mismatch");
    Check(s.Requests > 10, $"expected many reconnects, saw {s.Requests} requests");
});

await Test("3. internet goes OFF for 3 s mid-download, resumes without re-downloading", async () =>
{
    await using var s = new TestServer { Size = 8L << 20, ThrottleBytesPerSec = 1 << 20 }; // ~1 MB/s per connection
    await s.StartAsync();
    var d = Tmp();
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), Fast());
    var j = m.Add(s.Url, d);
    while (j.DownloadedBytes < (2L << 20)) await Task.Delay(50);
    s.Offline = true;
    bool sawWaiting = false;
    for (int i = 0; i < 30; i++) { await Task.Delay(100); if (j.Status == DownloadStatus.WaitingForNetwork) sawWaiting = true; }
    long atOutage = j.DownloadedBytes;
    s.Offline = false;
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(60));
    Check(sawWaiting, "never reported WaitingForNetwork");
    Check(j.Status == DownloadStatus.Completed, $"status {j.Status} {j.Error}");
    Check(TestServer.FileSha(j.FinalPath) == TestServer.Sha(s.Size, s.Seed), "hash mismatch");
    Check(s.BytesServed < s.Size * 12 / 10, $"re-downloaded too much: served {s.BytesServed} for {s.Size} (had {atOutage} at outage)");
});

await Test("4. app closed mid-download, reopened: resumes from saved state", async () =>
{
    await using var s = new TestServer { Size = 8L << 20, ThrottleBytesPerSec = 1 << 20 };
    await s.StartAsync();
    var d = Tmp(); var state = Path.Combine(d, "jobs.json");
    string id;
    long before;
    await using (var m1 = new DownloadManager(state, Fast()))
    {
        var j = m1.Add(s.Url, d); id = j.Id;
        while (j.DownloadedBytes < (3L << 20)) await Task.Delay(50);
        before = j.DownloadedBytes;
    } // dispose = app exit
    long servedBeforeRestart = s.BytesServed;
    await using var m2 = new DownloadManager(state, Fast());
    m2.Load();
    var j2 = m2.Get(id) ?? throw new Exception("job not restored");
    Check(j2.DownloadedBytes >= before - (1 << 20), $"lost progress: had {before}, restored {j2.DownloadedBytes}");
    await m2.WaitAsync(id, TimeSpan.FromSeconds(60));
    Check(TestServer.FileSha(j2.FinalPath) == TestServer.Sha(s.Size, s.Seed), "hash mismatch");
    Check(s.BytesServed - servedBeforeRestart < s.Size - before + (2 << 20), "second run re-downloaded already saved bytes");
});

await Test("5. remote file replaced mid-download (new ETag): restarts, final file is the NEW version", async () =>
{
    await using var s = new TestServer { Size = 6L << 20, ThrottleBytesPerSec = 1 << 20 };
    await s.StartAsync();
    var d = Tmp();
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), Fast());
    var j = m.Add(s.Url, d);
    while (j.DownloadedBytes < (2L << 20)) await Task.Delay(50);
    s.Seed = 2; s.DropAfterBytes = 200 * 1024; // new content, force every worker to re-request
    await Task.Delay(1500);
    s.DropAfterBytes = 0;
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(60));
    Check(j.Status == DownloadStatus.Completed, $"status {j.Status} {j.Error}");
    Check(TestServer.FileSha(j.FinalPath) == TestServer.Sha(s.Size, 2), "file is not the new version (mixed/corrupt)");
});

await Test("6. server without Range support still completes", async () =>
{
    await using var s = new TestServer { Size = 3L << 20, SupportRanges = false };
    await s.StartAsync();
    var d = Tmp();
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), Fast());
    var j = m.Add(s.Url, d);
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(30));
    Check(j.Status == DownloadStatus.Completed && !j.Resumable, $"status {j.Status} resumable={j.Resumable} {j.Error}");
    Check(TestServer.FileSha(j.FinalPath) == TestServer.Sha(s.Size, s.Seed), "hash mismatch");
});

await Test("7. HTTP 404 fails fast (does not retry forever)", async () =>
{
    await using var s = new TestServer();
    await s.StartAsync();
    var d = Tmp();
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), Fast());
    var j = m.Add(s.Url.Replace("f.bin", "missing.bin"), d);
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(10));
    Check(j.Status == DownloadStatus.Failed && (j.Error ?? "").Contains("404"), $"status {j.Status} {j.Error}");
});

await Test("8. speed limit is honoured (2 MB at 1 MB/s takes ~2 s)", async () =>
{
    await using var s = new TestServer { Size = 3L << 20 };
    await s.StartAsync();
    var d = Tmp();
    var o = Fast(); o.SpeedLimitBytesPerSec = 1 << 20;
    await using var m = new DownloadManager(Path.Combine(d, "jobs.json"), o);
    var sw = System.Diagnostics.Stopwatch.StartNew();
    var j = m.Add(s.Url, d);
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(30));
    Check(j.Status == DownloadStatus.Completed, $"status {j.Status} {j.Error}");
    Check(sw.Elapsed.TotalSeconds > 2.0, $"finished too fast ({sw.Elapsed.TotalSeconds:F1}s): limit ignored");
});


await Test("9. semver ordering (pre-releases, v-prefix, numeric parts)", () =>
{
    Check(SemVer.Compare("0.2.0", "0.1.9") > 0, "0.2.0 > 0.1.9");
    Check(SemVer.Compare("v1.0.0", "1.0.0") == 0, "v prefix ignored");
    Check(SemVer.Compare("1.0.0", "1.0.0-alpha.1") > 0, "release > its pre-release");
    Check(SemVer.Compare("1.0.0-alpha.2", "1.0.0-alpha.10") < 0, "numeric pre-release identifiers compare as numbers");
    Check(SemVer.Compare("0.10.0", "0.9.0") > 0, "0.10 > 0.9 (not string compare)");
    return Task.CompletedTask;
});

await Test("10. update check: newest wins, drafts/older ignored, notes are shown", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(
        new FakeRelease("v0.3.0", Draft: true),
        new FakeRelease("v0.2.0", Body: "## Novedades\r\n- Lite por sitio\r\n- Actualizador\r\n\r\n\r\n- Arreglos"),
        new FakeRelease("v0.1.5"), new FakeRelease("v0.1.0"));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    var r = await svc.CheckAsync("0.1.0", includePrereleases: true) ?? throw new Exception("no update found");
    Check(r.Version == "0.2.0", $"expected 0.2.0 (draft 0.3.0 must be ignored), got {r.Version}");
    Check(r.Notes.Contains("Lite por sitio") && !r.Notes.Contains("\r") && !r.Notes.Contains("\n\n"), "notes normalised: " + r.Notes);
    Check(await svc.CheckAsync("0.2.0", true) is null, "same version must not be offered");
    Check(await svc.CheckAsync("0.9.0", true) is null, "a NEWER local build must not be 'downgraded'");
});

await Test("11. pre-releases only offered to users on the alpha track", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v1.1.0-beta.1", Prerelease: true), new FakeRelease("v1.0.1"));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    Check((await svc.CheckAsync("1.0.0", includePrereleases: false))?.Version == "1.0.1", "stable users get only 1.0.1");
    Check((await svc.CheckAsync("1.0.0", includePrereleases: true))?.Version == "1.1.0-beta.1", "alpha users get the beta");
});

await Test("12. download is verified (SHA-256) and swapped in atomically, old build kept for rollback", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0"));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    var rel = await svc.CheckAsync("0.1.0", true) ?? throw new Exception("no update");
    var d = Tmp();
    var file = await svc.DownloadAsync(rel, Path.Combine(d, "dl"), timeout: TimeSpan.FromSeconds(30));
    var target = Path.Combine(d, "Blaze.AppImage");
    File.WriteAllText(target, "OLD BUILD");
    UpdateService.ReplaceFile(file, target);
    Check(File.ReadAllText(target + ".old") == "OLD BUILD", "previous build not kept as .old");
    Check(File.ReadAllBytes(target).SequenceEqual(gh.AssetBytes("v0.2.0")), "installed bytes differ from the release");
    Check((File.GetUnixFileMode(target) & UnixFileMode.UserExecute) != 0, "installed AppImage is not executable");
    Check(!File.Exists(target + ".new"), "temp file left behind");
});

await Test("13. TAMPERED download is rejected and deleted; installed build untouched", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0", TamperAsset: true));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    var rel = await svc.CheckAsync("0.1.0", true) ?? throw new Exception("no update");
    var d = Tmp();
    try { await svc.DownloadAsync(rel, Path.Combine(d, "dl"), timeout: TimeSpan.FromSeconds(30)); throw new Exception("tampered file was ACCEPTED"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("Checksum mismatch"), ex.Message); }
    Check(!Directory.EnumerateFiles(Path.Combine(d, "dl"), "*.AppImage").Any(), "tampered file was not deleted");
});

await Test("14. release without SHA256SUMS is refused (never install unverifiable code)", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0", WithSums: false));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    var rel = await svc.CheckAsync("0.1.0", true) ?? throw new Exception("no update");
    try { await svc.DownloadAsync(rel, Path.Combine(Tmp(), "dl")); throw new Exception("unverifiable release was accepted"); }
    catch (InvalidOperationException ex) { Check(ex.Message.Contains("SHA256SUMS"), ex.Message); }
});

await Test("15. plain http asset URLs are ignored in production mode (https only)", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0"));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: false);   // gh serves http://
    Check(await svc.CheckAsync("0.1.0", true) is null, "http:// asset must not be trusted");
});

await Test("16. attacker rewrites SHA256SUMS to match a malicious AppImage: signature check REJECTS it", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0", TamperAsset: true, AttackerRewritesSums: true));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    var rel = await svc.CheckAsync("0.1.0", true) ?? throw new Exception("no update");
    try { await svc.DownloadAsync(rel, Path.Combine(Tmp(), "dl")); throw new Exception("forged release was ACCEPTED"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("signature"), ex.Message); }
});

await Test("17. release signed with somebody else's key is rejected", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0", Signature: Sig.OtherKey));
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    var rel = await svc.CheckAsync("0.1.0", true) ?? throw new Exception("no update");
    try { await svc.DownloadAsync(rel, Path.Combine(Tmp(), "dl")); throw new Exception("foreign-key release was ACCEPTED"); }
    catch (InvalidDataException ex) { Check(ex.Message.Contains("INVALID"), ex.Message); }
});

await Test("18. unsigned and corrupt-signature releases are refused", async () =>
{
    foreach (var mode in new[] { Sig.Missing, Sig.Corrupt })
    {
        await using var gh = new FakeGitHub();
        await gh.StartAsync(new FakeRelease("v0.2.0", Signature: mode));
        var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
        var rel = await svc.CheckAsync("0.1.0", true) ?? throw new Exception("no update");
        try { await svc.DownloadAsync(rel, Path.Combine(Tmp(), "dl")); throw new Exception($"{mode} release was ACCEPTED"); }
        catch (Exception ex) when (ex is InvalidDataException or InvalidOperationException) { }
    }
});

await Test("19. finished downloads keep no cookie; state file is private (0600)", async () =>
{
    await using var s = new TestServer { Size = 2L << 20 };
    await s.StartAsync();
    var d = Tmp(); var state = Path.Combine(d, "jobs.json");
    await using var m = new DownloadManager(state, Fast());
    var j = m.Add(s.Url, d, cookie: "session=SECRET123", referer: "https://example.org/private");
    await m.WaitAsync(j.Id, TimeSpan.FromSeconds(30));
    await Task.Delay(500);
    Check(j.Cookie is null && j.Referer is null, "cookie/referer kept after completion");
    Check(!File.ReadAllText(state).Contains("SECRET123"), "cookie value persisted in the state file");
    Check((File.GetUnixFileMode(state) & (UnixFileMode.GroupRead | UnixFileMode.OtherRead)) == 0, "state file is readable by other users");
});

await Test("20. a signature made by the openssl CLI (the release pipeline) verifies in C#; any edit breaks it", async () =>
{
    var d = Tmp();
    string Run(string args) { var ps = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("openssl", args) { RedirectStandardOutput = true, RedirectStandardError = true, WorkingDirectory = d })!; var o = ps.StandardOutput.ReadToEnd(); ps.WaitForExit(); if (ps.ExitCode != 0) throw new Exception("openssl " + args + ": " + ps.StandardError.ReadToEnd()); return o; }
    Run("ecparam -name prime256v1 -genkey -noout -out k.pem");
    Run("ec -in k.pem -pubout -outform DER -out pub.der");
    File.WriteAllText(Path.Combine(d, "SHA256SUMS"), "abc123  Blaze-9.9.9-x86_64.AppImage\n");
    Run("dgst -sha256 -sign k.pem -out SHA256SUMS.sig SHA256SUMS");
    var verifier = new ReleaseVerifier(File.ReadAllBytes(Path.Combine(d, "pub.der")));
    var data = File.ReadAllBytes(Path.Combine(d, "SHA256SUMS")); var sig = File.ReadAllBytes(Path.Combine(d, "SHA256SUMS.sig"));
    Check(verifier.Verify(data, sig), "valid openssl signature rejected: signing and verifying disagree on the format");
    data[0] ^= 1; Check(!verifier.Verify(data, sig), "an edited SHA256SUMS still verified");
    Check(!ReleaseVerifier.Default.Verify(File.ReadAllBytes(Path.Combine(d, "SHA256SUMS.sig")), sig), "garbage must not verify");
    await Task.CompletedTask;
});

await Test("21. GitHub rate limit (403) is reported as RateLimited, not as a crash", async () =>
{
    await using var gh = new FakeGitHub();
    await gh.StartAsync(new FakeRelease("v0.2.0"));
    gh.RateLimited = true;
    var svc = new UpdateService(gh.Repo, gh.Verifier, gh.Base, allowInsecure: true);
    try { await svc.CheckAsync("0.1.0", true); throw new Exception("403 was not reported"); }
    catch (RateLimitedException) { }
});

Console.WriteLine(failed == 0 ? "\nALL TESTS PASSED" : $"\n{failed} TEST(S) FAILED");
return failed == 0 ? 0 : 1;
