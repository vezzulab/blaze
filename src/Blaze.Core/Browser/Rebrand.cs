using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Blaze.Core.Browser;

public enum RebrandResult { Unchanged, Rebranded }

/// <summary>
/// Makes the pinned Firefox engine present itself as Blaze: error pages, menus, dialogs, "About".
/// Firefox keeps its product name in a handful of branding files (brand.ftl / brand.properties / brand.dtd) that
/// ~240 UI strings reference, so changing those files renames the whole interface. Error pages are drawn by a
/// shadow-DOM component, so user CSS cannot restyle them; we append Blaze's tokens to the engine's own stylesheet.
///
/// Safety: work happens on a copy and the original omni.ja is replaced only after the copy was written completely.
/// The engine is pinned and checksum-verified, so a damaged engine can always be reinstalled. The legal line
/// "Firefox and the Firefox logos are trademarks of the Mozilla Foundation" is left intact on purpose.
/// </summary>
public static class Rebrand
{
    private static readonly Regex BrandFtlTerm = new(@"^(-brand-(?:shorter-name|short-name|shortcut-name|full-name|product-name))\s*=(.*)$", RegexOptions.Compiled);
    private static readonly Regex BrandProps = new(@"^(brand(?:ShorterName|ShortName|FullName|ProductName))\s*=.*$", RegexOptions.Compiled);
    private static readonly Regex BrandDtd = new(@"(<!ENTITY\s+brand(?:ShorterName|ShortName|FullName|ProductName)\s+)""[^""]*""", RegexOptions.Compiled);
    private static readonly Regex FtlPath = new(@"^localization/[^/]+/branding/brand\.ftl$", RegexOptions.Compiled);
    private static readonly Regex PropsPath = new(@"^chrome/[^/]+/locale/branding/brand\.properties$", RegexOptions.Compiled);
    private static readonly Regex DtdPath = new(@"^chrome/[^/]+/locale/branding/brand\.dtd$", RegexOptions.Compiled);
    private const string ErrorCssEntry = "chrome/toolkit/skin/classic/global/aboutNetError.css";

    public static RebrandResult Apply(string firefoxDir, string name, string? errorCss)
    {
        var marker = Path.Combine(firefoxDir, "distribution", "blaze-rebrand");
        var browserJar = Path.Combine(firefoxDir, "browser", "omni.ja");
        var toolkitJar = Path.Combine(firefoxDir, "omni.ja");
        foreach (var j in new[] { browserJar, toolkitJar })
            if (!File.Exists(j)) throw new FileNotFoundException("Not a Firefox directory (missing " + j + ")");

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + "\n" + (errorCss ?? "")))) +
                          $" {new FileInfo(browserJar).Length}:{new FileInfo(toolkitJar).Length}";
        // The marker stores the fingerprint of the result we produced (sizes after patching), so a reinstalled
        // engine (different sizes) or changed branding/CSS is detected and patched again.
        if (File.Exists(marker) && File.ReadAllText(marker).Trim() == fingerprint) return RebrandResult.Unchanged;

        PatchJar(browserJar, (entry, text) =>
        {
            if (FtlPath.IsMatch(entry)) return PatchFtl(text, name);
            if (PropsPath.IsMatch(entry)) return BrandProps.Replace(text.Replace("\r\n", "\n"), m => $"{m.Groups[1].Value}={name}");
            if (DtdPath.IsMatch(entry)) return BrandDtd.Replace(text, m => $"{m.Groups[1].Value}\"{name}\"");
            return null;
        }, e => FtlPath.IsMatch(e) || PropsPath.IsMatch(e) || DtdPath.IsMatch(e));

        if (!string.IsNullOrWhiteSpace(errorCss))
            PatchJar(toolkitJar, (entry, text) => entry == ErrorCssEntry ? text + "\n\n/* ---- Blaze ---- */\n" + errorCss + "\n" : null,
                     e => e == ErrorCssEntry);

        Directory.CreateDirectory(Path.GetDirectoryName(marker)!);
        var after = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(name + "\n" + (errorCss ?? "")))) +
                    $" {new FileInfo(browserJar).Length}:{new FileInfo(toolkitJar).Length}";
        File.WriteAllText(marker, after);
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

    private static void PatchJar(string jar, Func<string, string, string?> transform, Func<string, bool> wanted)
    {
        var tmp = jar + ".blaze-tmp";
        File.Copy(jar, tmp, overwrite: true);
        try
        {
            using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Update))
            {
                foreach (var entry in zip.Entries.Where(e => wanted(e.FullName)).ToList())
                {
                    string text;
                    using (var r = new StreamReader(entry.Open(), new UTF8Encoding(false))) text = r.ReadToEnd();
                    var patched = transform(entry.FullName, text);
                    if (patched is null || patched == text) continue;
                    var name = entry.FullName;
                    entry.Delete();
                    var fresh = zip.CreateEntry(name, CompressionLevel.Optimal);
                    using var w = new StreamWriter(fresh.Open(), new UTF8Encoding(false));
                    w.Write(patched);
                }
            }
            File.Move(tmp, jar, overwrite: true);   // atomic swap, only after a complete, valid archive exists
        }
        finally { try { File.Delete(tmp); } catch { } }
    }
}
