using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Blaze.Core.Browser;

public enum RebrandResult { Unchanged, Rebranded }

/// <summary>
/// Makes the pinned Firefox engine present itself as Blaze: error pages, menus, dialogs, "About".
///  - Firefox keeps its product name in a few branding files (brand.ftl / brand.properties / brand.dtd) that ~240 UI
///    strings reference, so changing those renames the whole interface.
///  - Error pages are drawn by a shadow-DOM component that user CSS cannot reach, so Blaze's colour tokens are appended
///    to the engine's own stylesheet, and the mascot illustrations are replaced by Blaze artwork.
///
/// Each jar is REWRITTEN INTO A NEW, CLEAN ZIP (never edited in place). Mozilla ships omni.ja in an optimised layout
/// with a large prefix; patching it in place with .NET produced an archive Firefox would not start from (measured: no
/// start in 120 s, against 0.4 s for the original). A fresh standard zip starts as fast as the original.
/// The original file is replaced only after the new one was written completely. The engine is pinned and
/// checksum-verified, so it can always be reinstalled. The legal line "Firefox and the Firefox logos are trademarks
/// of the Mozilla Foundation" is left intact on purpose.
/// </summary>
public static class Rebrand
{
    private static readonly Regex BrandFtlTerm = new(@"^(-brand-(?:shorter-name|short-name|shortcut-name|full-name|product-name))\s*=(.*)$", RegexOptions.Compiled);
    private static readonly Regex BrandProps = new(@"^(brand(?:ShorterName|ShortName|FullName|ProductName))\s*=.*$", RegexOptions.Compiled | RegexOptions.Multiline);
    private static readonly Regex BrandDtd = new(@"(<!ENTITY\s+brand(?:ShorterName|ShortName|FullName|ProductName)\s+)""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex FtlPath = new(@"^localization/[^/]+/branding/brand\.ftl$", RegexOptions.Compiled);
    private static readonly Regex PropsPath = new(@"^chrome/[^/]+/locale/branding/brand\.properties$", RegexOptions.Compiled);
    private static readonly Regex DtdPath = new(@"^chrome/[^/]+/locale/branding/brand\.dtd$", RegexOptions.Compiled);
    private const string CssMark = "/* ---- Blaze ---- */";
    private const string ErrorCssEntry = "chrome/toolkit/skin/classic/global/aboutNetError.css";

    public static RebrandResult Apply(string firefoxDir, string name, string? errorCss, string? assetsDir = null)
    {
        var marker = Path.Combine(firefoxDir, "distribution", "blaze-rebrand");
        var browserJar = Path.Combine(firefoxDir, "browser", "omni.ja");
        var toolkitJar = Path.Combine(firefoxDir, "omni.ja");
        foreach (var j in new[] { browserJar, toolkitJar })
            if (!File.Exists(j)) throw new FileNotFoundException("Not a Firefox directory (missing " + j + ")");

        var assets = LoadAssets(assetsDir);
        var inputs = InputsHash(name, errorCss, assets);

        // The marker holds the hash of our inputs AND the sizes of the jars we produced, so a reinstalled engine
        // (different sizes) or changed branding/CSS/artwork is detected and patched again.
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == inputs + " " + Sizes(browserJar, toolkitJar))
            return RebrandResult.Unchanged;

        bool IsBrand(string e) => FtlPath.IsMatch(e) || PropsPath.IsMatch(e) || DtdPath.IsMatch(e);
        bool IsAsset(string e) => assets.ContainsKey(Path.GetFileName(e)) && (e.Contains("/illustrations/") || e.Contains("cpm-fox"));

        RewriteJar(browserJar, e => IsBrand(e) || IsAsset(e), (e, data) =>
        {
            if (IsAsset(e)) return assets[Path.GetFileName(e)];
            var text = Utf8(data);
            if (FtlPath.IsMatch(e)) return Bytes(PatchFtl(text, name));
            if (PropsPath.IsMatch(e)) return Bytes(BrandProps.Replace(text.Replace("\r\n", "\n"), m => $"{m.Groups[1].Value}={name}"));
            return Bytes(BrandDtd.Replace(text, m => $"{m.Groups[1].Value}\"{name}\""));
        });

        RewriteJar(toolkitJar, e => (!string.IsNullOrWhiteSpace(errorCss) && e == ErrorCssEntry) || IsAsset(e), (e, data) =>
            IsAsset(e) ? assets[Path.GetFileName(e)] : Bytes(StripBlazeBlock(Utf8(data)) + "\n\n" + CssMark + "\n" + errorCss + "\n"));

        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        File.WriteAllText(marker, inputs + " " + Sizes(browserJar, toolkitJar));
        return RebrandResult.Rebranded;
    }

    /// <summary>Replace the value of the -brand-* terms; keep attributes (e.g. grammatical gender) and every other line.</summary>
    public static string PatchFtl(string text, string name)
    {
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var sb = new StringBuilder();
        for (int i = 0; i < lines.Length; i++)
        {
            var m = BrandFtlTerm.Match(lines[i]);
            if (!m.Success) { sb.Append(lines[i]).Append('\n'); continue; }
            sb.Append(m.Groups[1].Value).Append(" = ").Append(name).Append('\n');
            // multi-line terms: drop selector bodies, keep ".attribute = value" lines
            while (i + 1 < lines.Length && lines[i + 1].Length > 0 && char.IsWhiteSpace(lines[i + 1][0]))
            {
                i++;
                if (lines[i].TrimStart().StartsWith('.')) sb.Append(lines[i]).Append('\n');
            }
        }
        return sb.ToString().TrimEnd('\n') + "\n";
    }

    /// <summary>Re-applying on an already-patched engine must replace our block, not stack a second one.</summary>
    private static string StripBlazeBlock(string css)
    {
        var i = css.IndexOf(CssMark, StringComparison.Ordinal);
        return i < 0 ? css : css[..i].TrimEnd('\n', '\r', ' ');
    }

    private static Dictionary<string, byte[]> LoadAssets(string? dir)
    {
        var d = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return d;
        foreach (var f in Directory.GetFiles(dir, "*.svg")) d[Path.GetFileName(f)] = File.ReadAllBytes(f);
        return d;
    }

    private static string InputsHash(string name, string? css, Dictionary<string, byte[]> assets)
    {
        using var h = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        h.AppendData(Encoding.UTF8.GetBytes(name + "\n" + (css ?? "") + "\n"));
        foreach (var kv in assets.OrderBy(k => k.Key, StringComparer.Ordinal)) { h.AppendData(Encoding.UTF8.GetBytes(kv.Key)); h.AppendData(kv.Value); }
        return Convert.ToHexString(h.GetHashAndReset());
    }

    private static string Sizes(string a, string b) => $"{new FileInfo(a).Length}:{new FileInfo(b).Length}";
    private static string Utf8(byte[] b) => new UTF8Encoding(false).GetString(b);
    private static byte[] Bytes(string s) => new UTF8Encoding(false).GetBytes(s);

    private static void RewriteJar(string jar, Func<string, bool> wants, Func<string, byte[], byte[]> transform)
    {
        var tmp = jar + ".blaze-tmp";
        try
        {
            bool any;
            using (var src = ZipFile.OpenRead(jar))
            {
                any = src.Entries.Any(e => wants(e.FullName));
                if (!any) return;
                using var dstStream = new FileStream(tmp, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
                using var dst = new ZipArchive(dstStream, ZipArchiveMode.Create);
                foreach (var e in src.Entries)
                {
                    if (e.FullName.EndsWith('/')) continue;             // directory entries are not needed (same as `zip -D`)
                    byte[] data;
                    using (var s = e.Open()) { using var ms = new MemoryStream(); s.CopyTo(ms); data = ms.ToArray(); }
                    if (wants(e.FullName)) data = transform(e.FullName, data);
                    var ne = dst.CreateEntry(e.FullName, CompressionLevel.Optimal);
                    ne.LastWriteTime = e.LastWriteTime;
                    using var w = ne.Open();
                    w.Write(data, 0, data.Length);
                }
            }
            File.Move(tmp, jar, overwrite: true);                       // only after a complete, valid archive exists
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
