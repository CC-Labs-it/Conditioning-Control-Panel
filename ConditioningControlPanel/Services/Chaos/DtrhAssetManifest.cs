using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Enumerates the user's active preset (CorePaths.EffectiveAssets images/ + videos/) into the
/// manifest the DtRH browser game consumes over the bridge (hostMedia.js). Entries become
/// https://ccp.assets/ URLs; the page never sees a disk path.
///
/// Only browser-decodable formats are listed - LibVLC-only containers (wmv/avi/mkv/mov...)
/// are counted as skipped so the game can be honest about what it can't show. Native video
/// payloads keep playing those through VideoService untouched.
///
/// Since the app-wide remote-media work the manifest can also carry a bounded tail of
/// REMOTE entries (absolute CDN urls, no ccp.assets path behind them) when the user has
/// turned the app-wide source away from "local". Those are CORS-tainted and are therefore
/// usable only by the page's DOM layer, never by its WebGL one - see the remote-media
/// section at the foot of this file before touching them.
/// </summary>
internal static class DtrhAssetManifest
{
    private static readonly string[] ImageExts = { ".jpg", ".jpeg", ".png", ".webp", ".gif" };
    private static readonly string[] VideoExts = { ".mp4", ".webm", ".m4v" };
    private const long MaxImageBytes = 50L * 1024 * 1024;    // mirrors the engine's own caps
    private const long MaxVideoBytes = 500L * 1024 * 1024;
    private const int MaxEntries = 5000;                     // media.js precedent
    private const int MaxWalkDepth = 8;

    public sealed record Entry(string Name, string Url);

    public sealed class Manifest
    {
        public List<Entry> Images { get; } = new();
        public List<Entry> Videos { get; } = new();
        public int Skipped { get; set; }
        public bool Truncated { get; set; }
    }

    /// <summary>
    /// One file the scan looked at. <c>Skipped == true</c> means "media-looking but not usable"
    /// (unsupported container, zero-length, over the size cap) — the manifest counts those so the
    /// game can be honest; silently-ignored junk and user-deselected files are never yielded.
    /// </summary>
    private readonly record struct ScanItem(string Full, string Rel, long Bytes, bool IsImage, bool Skipped);

    /// <summary>Build the manifest for the current EffectiveAssetsPath. Never throws.</summary>
    public static Manifest Build()
    {
        var m = new Manifest();
        try
        {
            var root = CorePaths.EffectiveAssets;
            foreach (var it in Scan(root, BuildDisabledSet()))
            {
                if (it.Skipped) { m.Skipped++; continue; }
                var entry = new Entry(Path.GetFileName(it.Full), ToAssetUrl(root, it.Full));
                (it.IsImage ? m.Images : m.Videos).Add(entry);
            }

            int total = m.Images.Count + m.Videos.Count;
            if (total > MaxEntries)
            {
                // Uniform random sample per kind, preserving the image:video ratio.
                var rng = new Random();
                int imgKeep = (int)Math.Round(MaxEntries * (double)m.Images.Count / total);
                int vidKeep = MaxEntries - imgKeep;
                Downsample(m.Images, imgKeep, rng);
                Downsample(m.Videos, vidKeep, rng);
                m.Truncated = true;
            }

            // Remote entries ride ON TOP of the local pool, AFTER the downsample: they are a
            // bounded handful (MaxRemoteEntries) and sampling them against a 5000-file library
            // would leave "online" mode looking broken for exactly the users who asked for it.
            int remote = AppendRemote(m);

            App.Logger?.Information("DtrhAssetManifest: {I} images, {V} videos, {S} skipped{T}{R}",
                m.Images.Count, m.Videos.Count, m.Skipped, m.Truncated ? " (truncated)" : "",
                remote > 0 ? $" (+{remote} remote)" : "");
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhAssetManifest.Build failed: {E}", ex.Message);
        }
        return m;
    }

    /// <summary>
    /// The user's active pool as flat tuples: full path, forward-slashed path relative to
    /// EffectiveAssetsPath, byte length, and image-vs-video. Same filters the DtRH manifest uses
    /// (browser-decodable extensions, DisabledAssetPaths, the 50MB/500MB caps, depth 8, dot-dirs
    /// skipped, 10k walk bound) — shared so the transfer compression planner and the game can
    /// never disagree about what "the active pool" is. Lazy; never throws.
    ///
    /// LOCAL DISK ONLY, deliberately: this yields full paths and byte lengths, which a remote
    /// entry has neither of. The transfer compression planner is the consumer, and remote
    /// media must not become something the app offers to compress or hand to a duel partner.
    /// </summary>
    public static IEnumerable<(string Full, string Rel, long Bytes, bool IsImage)> EnumerateActive()
    {
        string root;
        HashSet<string> disabled;
        try
        {
            root = CorePaths.EffectiveAssets;
            disabled = BuildDisabledSet();
        }
        catch (Exception ex)
        {
            App.Logger?.Warning("DtrhAssetManifest.EnumerateActive setup failed: {E}", ex.Message);
            return Array.Empty<(string, string, long, bool)>();
        }
        return Enumerate(root, disabled);
    }

    private static IEnumerable<(string Full, string Rel, long Bytes, bool IsImage)> Enumerate(
        string root, HashSet<string> disabled)
    {
        foreach (var it in Scan(root, disabled))
            if (!it.Skipped) yield return (it.Full, it.Rel, it.Bytes, it.IsImage);
    }

    /// <summary>
    /// Honor the user's asset selection: items unchecked in the Assets tree land in
    /// DisabledAssetPaths (relative to EffectiveAssetsPath, forward-slashed). The DtRH game
    /// consumed the raw folder before this, so deselected images/videos still fell through the
    /// tube. Matches FlashService.GetMediaFiles' normalization exactly (case-insensitive,
    /// separator-agnostic) so the same unchecks that hide a flash hide it here too.
    /// </summary>
    private static HashSet<string> BuildDisabledSet() => new(
        (App.Settings?.Current?.DisabledAssetPaths ?? new()).Select(p => p.Replace('\\', '/')),
        StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// images/ then videos/, in walk order. The accepted-count bound spans BOTH folders (it used to
    /// be a shared running total across the two Collect calls) — keep it that way, the manifest's
    /// downsampling assumes a bounded but unbiased-by-folder input.
    /// </summary>
    private static IEnumerable<ScanItem> Scan(string root, HashSet<string> disabled)
    {
        int accepted = 0;
        foreach (var isImage in new[] { true, false })
        {
            var dir = Path.Combine(root, isImage ? "images" : "videos");
            if (!Directory.Exists(dir)) continue;
            var exts = isImage ? ImageExts : VideoExts;
            var otherExts = isImage ? VideoExts : ImageExts;
            long cap = isImage ? MaxImageBytes : MaxVideoBytes;
            foreach (var f in Walk(dir, 0))
            {
                if (accepted >= MaxEntries * 2) yield break; // sample later, but bound the walk
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (!exts.Contains(ext))
                {
                    // A media-looking file the browser can't decode counts as skipped;
                    // random other junk (txt/db) is silently ignored.
                    if (!otherExts.Contains(ext) && IsMediaLike(ext))
                        yield return new ScanItem(f, "", 0, isImage, true);
                    continue;
                }
                string rel;
                try { rel = Path.GetRelativePath(root, f).Replace('\\', '/'); }
                catch { rel = Path.GetFileName(f); }
                // Deselected in the Assets tree -> skip (silently, not "skipped"):
                // it's user intent, not an unsupported file.
                if (disabled.Count > 0 && disabled.Contains(rel)) continue;
                long len;
                try { len = new FileInfo(f).Length; } catch { continue; }
                if (len <= 0 || len > cap) { yield return new ScanItem(f, rel, len, isImage, true); continue; }
                accepted++;
                yield return new ScanItem(f, rel, len, isImage, false);
            }
        }
    }

    private static IEnumerable<string> Walk(string dir, int depth)
    {
        if (depth > MaxWalkDepth) yield break;
        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(dir); }
        catch { yield break; }
        foreach (var f in files) yield return f;
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(dir); }
        catch { yield break; }
        foreach (var d in subs)
        {
            if (Path.GetFileName(d).StartsWith('.')) continue;
            foreach (var f in Walk(d, depth + 1)) yield return f;
        }
    }

    private static bool IsMediaLike(string ext) =>
        ext is ".wmv" or ".avi" or ".mkv" or ".mov" or ".flv" or ".mpg" or ".mpeg" or ".bmp" or ".tiff" or ".heic";

    private static void Downsample<T>(List<T> list, int keep, Random rng)
    {
        if (list.Count <= keep) return;
        // partial Fisher-Yates: shuffle the first `keep` slots, then truncate
        for (int i = 0; i < keep; i++)
        {
            int j = i + rng.Next(list.Count - i);
            (list[i], list[j]) = (list[j], list[i]);
        }
        list.RemoveRange(keep, list.Count - keep);
    }

    private static string ToAssetUrl(string root, string fullPath) =>
        AssetUrl(Path.GetRelativePath(root, fullPath));

    /// <summary>
    /// An EffectiveAssetsPath-relative path (either slash) as the <c>https://ccp.assets/</c> URL a
    /// hosted page sees. Public so a second host can turn <see cref="EnumerateActive"/>'s
    /// <c>Rel</c> into a page URL without re-deriving the escaping - which is the whole point: the
    /// walk, the filters and the URL shape stay in this one file, and a host that wants the active
    /// pool gets it without owning a copy of any of them.
    /// </summary>
    public static string AssetUrl(string rel) =>
        "https://ccp.assets/" + string.Join('/',
            rel.Replace('\\', '/').Split('/').Select(Uri.EscapeDataString));

    // ========================= remote media (Phase 2, Contract 3) =========================
    //
    // Remote entries carry an ABSOLUTE CDN url and deliberately never go through
    // ToAssetUrl - there is no ccp.assets path behind them. They ride the SAME manifest
    // frame the page already consumes, because the frame's shape ({name,url} per entry) is
    // owned by DtrhHostService/GoonHostService and neither is ours to widen. The name
    // therefore carries the marker:
    //
    //     "online<pct>:<sub>-<postId>.<ext>"      pct = the remote share, 0..100
    //
    // hostMedia.js parses that prefix for the mix ratio, but decides remote-vs-local from
    // the URL's ORIGIN (anything not https://ccp.*) - a marker is a hint, an origin is a
    // fact, and only the fact can be trusted to keep tainted media out of WebGL.
    //
    // B3 / CORS: scrolller's CDN sends no Access-Control-Allow-Origin, so these URLs are
    // tainted. Every three.js consumer in the page (spawner wall cards, wallPosters, the
    // Mirror biomes) either fetch()es the bytes or sets crossOrigin='anonymous' before a
    // texture upload, and BOTH of those hard-fail on a tainted source. hostMedia.js keeps
    // remote entries out of draw()/drawKind()/favorite() for that reason and hands them
    // only to the DOM payload layer (game/payloadFx.js). See the header there.
    //
    // BRIGHT LINE: the fetch below goes from this machine straight to scrolller. No CC Labs
    // server is involved, and nothing here writes third-party BYTES to disk - the cache is
    // a list of URLs, which is the same class of data as the niche selection in settings.

    //
    // The cache, the copies, the refill and every rule live in Core DtrhRemoteMedia (shared with the
    // Avalonia head); this head only supplies RemoteMediaCache as the copier and its ccp.assets urls.

    /// <summary>Raised off the UI thread when new copies landed: the host re-posts the manifest
    /// so a page that is already up starts drawing them.</summary>
    internal static event Action? RemoteCopiesReady
    {
        add => DtrhRemoteMedia.CopiesReady += value;
        remove => DtrhRemoteMedia.CopiesReady -= value;
    }

    /// <summary>Licence day limit (owner, 2026-10-21): a remote entry, and any copy of its bytes,
    /// lives at most a day (Core <see cref="DtrhRemoteMedia.RemoteEntryTtlSeconds"/>).</summary>
    internal static TimeSpan RemoteEntryTtl => TimeSpan.FromSeconds(DtrhRemoteMedia.RemoteEntryTtlSeconds);

    /// <summary>Core <see cref="DtrhRemoteMedia.RemoteEntryFresh"/>.</summary>
    internal static bool RemoteEntryFresh(DtrhRemoteMedia.RemoteEntry e, long nowUnix) => DtrhRemoteMedia.RemoteEntryFresh(e, nowUnix);

    /// <summary>The hourly sweep (<see cref="Fyp.Online.ScrolllerCopySweeper"/>): every copy past the day
    /// limit is dropped and deleted; <paramref name="all"/> drops every copy (the off switch).</summary>
    internal static int SweepLocalCopies(bool all = false) => DtrhRemoteMedia.SweepLocalCopies(Copier.Release, all);

    /// <summary>Tests only: park a copy as the copy pass would.</summary>
    internal static bool AddLocalCopyForTests(string url, string path) => DtrhRemoteMedia.AddLocalCopyForTests(url, path);

    /// <summary>Tests only: is <paramref name="url"/> still mapped to a copy?</summary>
    internal static bool HasLocalCopyForTests(string url) => DtrhRemoteMedia.HasLocalCopyForTests(url);

    private static readonly DtrhRemoteMedia.Copier Copier = new(
        (url, ct) => RemoteMediaCache.MaterializeAsync(url, ct, ownerReleases: true),
        RemoteMediaCache.ReleaseTempFile);

    /// <summary>Append the cached remote pool to <paramref name="m"/> (a copied entry as its ccp.assets url;
    /// with the source "online" and 8 copies here the library steps aside) and top the cache up in the
    /// background. One launch behind on a cold cache; copies re-post the manifest as they land.
    /// Returns how many entries were added. Never throws.</summary>
    private static int AppendRemote(Manifest m)
    {
        var root = App.EffectiveAssetsPath;
        if (DtrhRemoteMedia.Take(App.Settings?.Current, copy => ToAssetUrl(root, copy), Copier) is not var (entries, stepAside)) return 0;
        if (stepAside) { m.Images.Clear(); m.Videos.Clear(); }
        foreach (var (isImage, name, url) in entries) (isImage ? m.Images : m.Videos).Add(new Entry(name, url));
        return entries.Count;
    }
}
