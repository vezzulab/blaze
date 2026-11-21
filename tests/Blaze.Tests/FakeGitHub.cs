using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Blaze.Core.Updates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Blaze.Tests;

public enum Sig { Good, Missing, OtherKey, Corrupt }

public sealed record FakeRelease(string Tag, bool Draft = false, bool Prerelease = false, string? Body = null,
    bool WithSums = true, bool TamperAsset = false, int AssetSize = 3 << 20, Sig Signature = Sig.Good, bool AttackerRewritesSums = false);

/// <summary>Just enough of the GitHub Releases API (and asset downloads) to test the updater offline.</summary>
public sealed class FakeGitHub : IAsyncDisposable
{
    private WebApplication? _app;
    private readonly Dictionary<string, byte[]> _files = new();
    public string Base { get; private set; } = "";
    public string Repo { get; } = "vezzulab/blaze";
    public int ApiHits;
    public bool RateLimited;
    public ECDsa Key { get; } = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    public ReleaseVerifier Verifier => new(Key.ExportSubjectPublicKeyInfo());
    private string _json = "[]";

    public static string Asset(string tag) => $"Blaze-{tag.TrimStart('v')}-x86_64.AppImage";
    public byte[] AssetBytes(string tag) => _files[Asset(tag)];

    public async Task StartAsync(params FakeRelease[] releases)
    {
        var b = WebApplication.CreateSlimBuilder();
        b.Logging.ClearProviders();
        b.WebHost.UseUrls("http://127.0.0.1:0");
        _app = b.Build();
        // routes must be mapped BEFORE the server starts; they read _json, which is filled in once the port is known
        _app.MapGet($"/repos/{Repo}/releases", () => { Interlocked.Increment(ref ApiHits); return RateLimited ? Results.StatusCode(403) : Results.Text(_json, "application/json"); });
        _app.MapGet("/dl/{name}", (string name) => Results.Bytes(_files["served:" + name], "application/octet-stream", enableRangeProcessing: true));
        _app.MapGet("/sig/{tag}", (string tag) => Results.Bytes(_files["sig:" + tag], "application/octet-stream"));
        _app.MapGet("/sums/{tag}", (string tag) => Results.Bytes(_files["sums:" + tag], "text/plain"));
        await _app.StartAsync();
        Base = _app.Urls.First().TrimEnd('/');

        var list = new List<object>();
        foreach (var r in releases)
        {
            var name = Asset(r.Tag);
            var bytes = new byte[r.AssetSize];
            new Random(r.Tag.GetHashCode()).NextBytes(bytes);
            var goodSha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            _files[name] = bytes;
            var served = (byte[])bytes.Clone();
            if (r.TamperAsset) served[served.Length / 2] ^= 0xFF;     // what a MITM / corrupted mirror would produce
            _files["served:" + name] = served;
            var signedSums = Encoding.UTF8.GetBytes($"{goodSha}  {name}\n");
            // a signature is always made over the ORIGINAL sums; an attacker who edits them afterwards cannot re-sign
            byte[] sig = r.Signature switch
            {
                Sig.OtherKey => ECDsa.Create(ECCurve.NamedCurves.nistP256).SignData(signedSums, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
                _ => Key.SignData(signedSums, HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence),
            };
            if (r.Signature == Sig.Corrupt) sig[^3] ^= 0x01;
            var servedSums = r.AttackerRewritesSums
                ? Encoding.UTF8.GetBytes($"{Convert.ToHexString(SHA256.HashData(served)).ToLowerInvariant()}  {name}\n")
                : signedSums;
            _files["sums:" + r.Tag] = servedSums;
            _files["sig:" + r.Tag] = sig;

            var assets = new List<object> { new { name, browser_download_url = $"{Base}/dl/{name}" } };
            if (r.WithSums) assets.Add(new { name = "SHA256SUMS", browser_download_url = $"{Base}/sums/{r.Tag}" });
            if (r.WithSums && r.Signature != Sig.Missing) assets.Add(new { name = "SHA256SUMS.sig", browser_download_url = $"{Base}/sig/{r.Tag}" });
            list.Add(new { tag_name = r.Tag, draft = r.Draft, prerelease = r.Prerelease, body = r.Body,
                           html_url = $"https://github.com/{Repo}/releases/tag/{r.Tag}", assets });
        }
        _json = JsonSerializer.Serialize(list);
    }

    public async ValueTask DisposeAsync()
    {
        if (_app is not null) { await _app.StopAsync(); await _app.DisposeAsync(); }
    }
}
