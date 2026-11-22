using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Tc;

/// <summary>The checked <c>expo-manifest.json</c> of release tag <c>tc-expo</c>.
/// <paramref name="UnpackedBytes"/> is the manifest's <c>unpacked</c>, else <see cref="TcExpoPackRules.UnpackFactor"/> x the zip.</summary>
internal sealed record TcExpoManifest(string File, long Bytes, string Sha256, string Version, string MinApp, long UnpackedBytes);

/// <summary>
/// The Llama Expo pack as pure rules: the manifest (read from the release tag <c>tc-expo</c> BY NAME,
/// never <c>/releases/latest</c>), the folders under the content root, the ready stamp, the room
/// check, zip-slip, and which old folders go. The pack is data only: nothing in it is ever run, it is
/// served read-only as <c>https://ccp.expo/</c> to the T&amp;C window.
/// Layout: <c>&lt;ContentRoot&gt;/tc-expo/&lt;version&gt;/</c> + stamp <c>&lt;version&gt;.ready</c> beside it
/// (written last), partial in <c>&lt;ContentRoot&gt;/.downloads/</c> next to the packs.
/// </summary>
internal static class TcExpoPackRules
{
    public const string Tag = "tc-expo";
    public const string ManifestName = "expo-manifest.json";
    public const string ReleaseBase = "https://github.com/CC-Labs-it/Conditioning-Control-Panel/releases/download/" + Tag + "/";
    public const string ManifestUrl = ReleaseBase + ManifestName;
    public const string FolderName = "tc-expo";
    public const string StampSuffix = ".ready";
    /// <summary>The file every Expo pack has at its root; an unpack without it is refused.</summary>
    public const string RootProbe = "zones/zones.json";
    public const int UnpackFactor = 2;
    public const long MaxManifestChars = 16_384;
    public const long MaxPackBytes = 2L * 1024 * 1024 * 1024;

    private static readonly Regex SafeFile = new("^[A-Za-z0-9][A-Za-z0-9._-]{0,119}[.]zip$", RegexOptions.CultureInvariant);
    private static readonly Regex SafeVersion = new("^[0-9A-Za-z][0-9A-Za-z._-]{0,39}$", RegexOptions.CultureInvariant);
    private static readonly Regex Hex64 = new("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);

    /// <summary>The manifest, or null with a fixed <paramref name="why"/>. Refused: any <c>v</c> but 1, a
    /// <c>file</c> that is not a plain <c>.zip</c> name, a <c>minApp</c> above <paramref name="appVersion"/>.</summary>
    public static TcExpoManifest? Parse(string? json, string appVersion, out string why)
    {
        why = "shape";
        if (string.IsNullOrEmpty(json) || json.Length > MaxManifestChars) return null;
        JObject o;
        try { o = JObject.Parse(json); } catch { return null; }
        if (o["v"] is not JValue { Type: JTokenType.Integer } v || (long)v != 1) { why = "v"; return null; }

        var file = Str(o, "file");
        if (file == null || !SafeFile.IsMatch(file) || file.Contains("..")) { why = "file"; return null; }
        if (o["bytes"] is not JValue { Type: JTokenType.Integer } b || (long)b <= 0 || (long)b > MaxPackBytes) { why = "bytes"; return null; }
        var sha = Str(o, "sha256")?.ToLowerInvariant();
        if (sha == null || !Hex64.IsMatch(sha)) { why = "sha256"; return null; }
        var version = Str(o, "version");
        if (version == null || !SafeVersion.IsMatch(version) || version.Contains("..")) { why = "version"; return null; }
        var minApp = Str(o, "minApp");
        if (minApp == null || !Version.TryParse(minApp, out var min)) { why = "minApp"; return null; }
        if (!Version.TryParse(appVersion, out var app) || min > app) { why = "app-too-old"; return null; }

        var bytes = (long)b;
        var unpacked = o["unpacked"] is JValue { Type: JTokenType.Integer } u && (long)u > 0 ? (long)u : bytes * UnpackFactor;
        why = "";
        return new TcExpoManifest(file, bytes, sha, version, minApp, unpacked);
    }

    /// <summary><c>caps.expo.mb</c>: the zip in whole megabytes (131 for 131,367,569 bytes).</summary>
    public static int Mb(long bytes) => bytes <= 0 ? 0 : Math.Max(1, (int)Math.Round(bytes / 1_000_000.0, MidpointRounding.AwayFromZero));

    /// <summary>The base the page loads the pack from: https and ending in <c>/</c>.</summary>
    public static bool IsBase(string? b)
        => b != null && b.EndsWith('/') && Uri.TryCreate(b, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps
           && u.Host == TcRules.ExpoHost && u.AbsolutePath == "/";

    /// <summary>Free space for the rest of the zip plus the unpack, before a byte is fetched.</summary>
    public static bool HasRoom(long availableBytes, TcExpoManifest m, long partialBytes)
        => availableBytes >= Math.Max(0, m.Bytes - Math.Max(0, partialBytes)) + m.UnpackedBytes;

    /// <summary>The zip's entries add up to no more than the manifest says (no zip bomb).</summary>
    public static bool UnpackFits(long entryBytes, TcExpoManifest m) => entryBytes >= 0 && entryBytes <= m.UnpackedBytes;

    public static string Root(string contentRoot) => Path.Combine(contentRoot, FolderName);
    public static string VersionDir(string contentRoot, string version) => Path.Combine(Root(contentRoot), version);
    public static string StampPath(string contentRoot, string version) => Path.Combine(Root(contentRoot), version + StampSuffix);
    public static string PartialPath(string contentRoot, TcExpoManifest m) => Path.Combine(contentRoot, ".downloads", m.File + ".partial");
    public static string ExtractPath(string contentRoot, TcExpoManifest m) => Path.Combine(contentRoot, ".downloads", FolderName + "-" + m.Version + ".extract");

    /// <summary>Where a zip entry lands under <paramref name="root"/>, or null when it would leave it
    /// (rooted, a drive, a <c>..</c> segment, a control character). Directory entries end in a separator.</summary>
    public static string? EntryTarget(string root, string? entryName)
    {
        if (string.IsNullOrEmpty(entryName) || entryName.Length > 400) return null;
        foreach (var c in entryName) if (char.IsControl(c) || c == ':') return null;
        var name = entryName.Replace('\\', '/');
        if (name.StartsWith('/') || Path.IsPathRooted(name)) return null;
        if (name.Split('/').Any(s => s == "..")) return null;
        var full = Path.GetFullPath(Path.Combine(root, name));
        var rootFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(rootFull, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    /// <summary>The ready version on disk: the newest folder that has its stamp (a folder without one is
    /// absent). Names are bare folder names and stamp names.</summary>
    public static string? ReadyVersion(IEnumerable<string> folders, IEnumerable<string> stamps)
    {
        var stamped = new HashSet<string>(stamps.Where(s => s.EndsWith(StampSuffix, StringComparison.Ordinal))
            .Select(s => s[..^StampSuffix.Length]), StringComparer.OrdinalIgnoreCase);
        return folders.Where(f => SafeVersion.IsMatch(f) && stamped.Contains(f))
            .OrderByDescending(f => Version.TryParse(f, out var v) ? v : new Version(0, 0)).ThenByDescending(f => f, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    /// <summary>What to remove: every folder but <paramref name="keep"/> and the one a window serves
    /// (<paramref name="serving"/>), and every stamp whose folder goes or is gone. Run on a fetch
    /// (unstamped leftovers) and after a newer version is ready and checked (the old one).</summary>
    public static (IReadOnlyList<string> Folders, IReadOnlyList<string> Stamps) Stale(
        IEnumerable<string> folders, IEnumerable<string> stamps, string? keep, string? serving)
    {
        bool Kept(string f) => string.Equals(f, keep, StringComparison.OrdinalIgnoreCase) || string.Equals(f, serving, StringComparison.OrdinalIgnoreCase);
        var dirs = folders.ToList();
        var goDirs = dirs.Where(f => !Kept(f)).ToList();
        var goStamps = stamps.Where(s => s.EndsWith(StampSuffix, StringComparison.Ordinal))
            .Where(s => { var f = s[..^StampSuffix.Length]; return !Kept(f) || !dirs.Contains(f, StringComparer.OrdinalIgnoreCase); })
            .ToList();
        return (goDirs, goStamps);
    }

    private static string? Str(JObject o, string key) => o[key] is JValue { Type: JTokenType.String } v ? (string?)v : null;
}
