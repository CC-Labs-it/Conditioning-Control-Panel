using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>
/// Enumerates the user's active preset (App.EffectiveAssetsPath images/ + videos/) into the
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
            var root = App.EffectiveAssetsPath;
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
            root = App.EffectiveAssetsPath;
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

    /// <summary>Marker prefix on a remote entry's name; the page mirrors this regex.</summary>
    private const string RemoteNamePrefix = "online";
    private const string RemoteConsumerId = "dtrh";
    private const int MaxRemoteEntries = 60;
    private const int RemoteKindLowWater = 12;           // refill a kind when it drops under this
    /// <summary>Licence day limit (owner, 2026-10-21): a remote entry, and any copy of its bytes,
    /// lives at most a day.</summary>
    internal static readonly TimeSpan RemoteEntryTtl = TimeSpan.FromHours(24);

    /// <summary>The day limit on one entry, pure: fresh while younger than
    /// <see cref="RemoteEntryTtl"/>. No time (0) or a time more than an hour ahead (the clock went
    /// back) is stale.</summary>
    internal static bool RemoteEntryFresh(RemoteEntry e, long nowUnix)
        => e.AtUnix > 0 && e.AtUnix - nowUnix <= 3600 && nowUnix - e.AtUnix < (long)RemoteEntryTtl.TotalSeconds;

    private static readonly object RemoteLock = new();
    private static List<RemoteEntry>? _remoteCache;      // null = not loaded from disk yet
    private static int _remoteFetchInFlight;             // 0/1 via Interlocked

    /// <summary><paramref name="IsLoop"/>: a MOVING PICTURE, the clip half of a gif post
    /// (<see cref="FeedMediaKind.GifClip"/>). A video file, shown by the page where a picture
    /// goes: silent, looping (owner, 2026-10-12: "we got a lot of still images").</summary>
    internal sealed record RemoteEntry(string Id, string Url, bool IsImage, long AtUnix, bool IsLoop = false);

    /// <summary>Marker after the share prefix on a moving picture's name; hostMedia.js mirrors it.</summary>
    private const string LoopNamePrefix = "loop:";

    private static string RemoteCachePath => Path.Combine(App.UserDataPath, "dtrh_remote_media.json");

    /// <summary>
    /// Append the cached remote pool to <paramref name="m"/> and top the cache up in the
    /// background. Returns how many entries were added. Never throws.
    ///
    /// SYNCHRONOUS BY NECESSITY, and therefore ONE LAUNCH BEHIND on a cold cache. Build() is
    /// called from the host's OnPageReady on the UI thread and the manifest is posted exactly
    /// once per page (there is no re-post path), so a network round trip here would either
    /// block the UI thread or arrive after the only frame that could carry it. The cache is
    /// persisted instead: the first ever DTRH launch with remote media enabled shows none and
    /// warms the file; every launch after that is instant.
    /// </summary>
    private static int AppendRemote(Manifest m)
    {
        try
        {
            var s = App.Settings?.Current;
            // Two gates, both required: the app-wide source must have left "local" AND the
            // user must have accepted a consent card (either this one or the FYP feed's -
            // HasRemoteMediaConsent is the property that knows about both).
            if (s == null || s.MediaSource == "local" || !s.HasRemoteMediaConsent) return 0;

            var pool = LoadRemoteCache();
            int pct = s.MediaSource == "online" ? 100 : Math.Clamp(s.RemoteMediaRatio, 5, 95);
            int added = 0;
            // An entry with a copy on this machine goes out as a ccp.assets url: the page
            // decides remote-ness by ORIGIN (hostMedia.js), so that copy is one the tube may
            // draw (walls, cards). The rest stay CDN urls for the page's DOM layer.
            var root = App.EffectiveAssetsPath;
            var entries = new List<(bool IsImage, Entry Entry)>(pool.Count);
            int copies = 0;
            // `pool` is a snapshot, so no lock is needed to walk it.
            foreach (var e in pool)
            {
                var name = $"{RemoteNamePrefix}{pct}:{(e.IsLoop ? LoopNamePrefix : "")}{NameFor(e)}";
                var url = e.Url;
                if (LocalCopies.TryGetValue(e.Url, out var copy) && File.Exists(copy) && !DropStaleCopy(e.Url, copy, DateTime.UtcNow))
                {
                    url = ToAssetUrl(root, copy);
                    copies++;
                }
                entries.Add((e.IsImage, new Entry(name, url)));
            }
            // "Online" is online only: once enough copies are here to dress the tube, the
            // library steps aside (owner, 2026-10-12: Scrolller picked, only local files showed).
            if (pct == 100 && copies >= OnlineOnlyMinCopies)
            {
                m.Images.Clear();
                m.Videos.Clear();
            }
            foreach (var (isImage, entry) in entries)
            {
                (isImage ? m.Images : m.Videos).Add(entry);
                added++;
            }
            KickRemoteRefill();
            KickLocalCopies(pool);
            return added;
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("DtrhAssetManifest: remote append failed: {E}", ex.Message);
            return 0;
        }
    }

    // ---- copies on this machine: what lets the tube draw online media ----
    //
    // A CDN url is CORS-tainted, so the page can only show it in its DOM layer (flash and
    // cascade cards). The bytes are fetched provider -> this machine by RemoteMediaCache and
    // parked in {EffectiveAssetsPath}/.temp, which the page sees as https://ccp.assets/.
    // The files are ours to release (ownerReleases) and the startup sweep takes them.

    /// <summary>Remote url -> the temp file holding its bytes.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> LocalCopies =
        new(StringComparer.Ordinal);
    private static int _copyInFlight;                    // 0/1 via Interlocked
    private const int OnlineOnlyMinCopies = 8;
    private const int MaxClipCopies = 24;                // clips are the heavy ones
    private const int CopyReadyEvery = 10;               // tell the host as they land, not only at the end

    /// <summary>The day limit on one copy: a file whose creation time (its download time) is a
    /// day old at <paramref name="nowUtc"/> leaves the map and is deleted. True when it went.</summary>
    private static bool DropStaleCopy(string url, string path, DateTime nowUtc)
    {
        DateTime born;
        try { born = File.GetCreationTimeUtc(path); } catch { born = DateTime.MinValue; }
        if (!ScrolllerCacheBook.IsExpiredAt(born, nowUtc)) return false;
        if (LocalCopies.TryRemove(new KeyValuePair<string, string>(url, path)))
            RemoteMediaCache.ReleaseTempFile(path);
        return true;
    }

    /// <summary>The hourly sweep: every copy past the day limit, or past it before the next
    /// sweep, is dropped and deleted. <paramref name="all"/> drops every copy (the off switch).
    /// Returns how many went; never throws.</summary>
    internal static int SweepLocalCopies(bool all = false)
    {
        int gone = 0;
        try
        {
            var ahead = DateTime.UtcNow + ScrolllerDiskCache.SweepPeriod;
            foreach (var kvp in LocalCopies.ToArray())
            {
                if (all ? LocalCopies.TryRemove(kvp) && Release(kvp.Value) : DropStaleCopy(kvp.Key, kvp.Value, ahead)) gone++;
            }
        }
        catch (Exception ex) { App.Logger?.Debug("DtrhAssetManifest: copy sweep failed: {E}", ex.Message); }
        return gone;

        static bool Release(string path) { RemoteMediaCache.ReleaseTempFile(path); return true; }
    }

    /// <summary>Tests only: park a copy as <see cref="KickLocalCopies"/> would.</summary>
    internal static bool AddLocalCopyForTests(string url, string path) => LocalCopies.TryAdd(url, path);

    /// <summary>Tests only: is <paramref name="url"/> still mapped to a copy?</summary>
    internal static bool HasLocalCopyForTests(string url) => LocalCopies.ContainsKey(url);

    /// <summary>Raised off the UI thread when new copies landed: the host re-posts the manifest
    /// so a page that is already up starts drawing them.</summary>
    internal static event Action? RemoteCopiesReady;

    /// <summary>Copy the pool's entries to this machine, stills first, single-flight.</summary>
    private static void KickLocalCopies(List<RemoteEntry> pool)
    {
        // Moving pictures first (they are what the tube and the flashes reach for), then the
        // stills, then the long clips.
        var todo = pool.Where(e => e.IsLoop && !LocalCopies.ContainsKey(e.Url))
            .Concat(pool.Where(e => e.IsImage && !LocalCopies.ContainsKey(e.Url)))
            .Concat(pool.Where(e => !e.IsImage && !e.IsLoop).Take(MaxClipCopies).Where(e => !LocalCopies.ContainsKey(e.Url)))
            .ToList();
        if (todo.Count == 0) return;
        if (Interlocked.CompareExchange(ref _copyInFlight, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            int landed = 0, told = 0;
            try
            {
                foreach (var e in todo)
                {
                    var s = App.Settings?.Current;
                    if (s == null || s.MediaSource == "local" || !s.HasRemoteMediaConsent) break;
                    var path = await RemoteMediaCache.MaterializeAsync(e.Url, CancellationToken.None, ownerReleases: true).ConfigureAwait(false);
                    if (path == null) continue;
                    if (!LocalCopies.TryAdd(e.Url, path)) { RemoteMediaCache.ReleaseTempFile(path); continue; }
                    landed++;
                    if (landed - told >= CopyReadyEvery) { told = landed; RaiseCopiesReady(); }
                }
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DtrhAssetManifest: local copies threw: {E}", ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _copyInFlight, 0);
                if (landed > 0) App.Logger?.Information("DtrhAssetManifest: {N} online entries copied for the tube", landed);
                if (landed > told) RaiseCopiesReady();
            }
        });
    }

    private static void RaiseCopiesReady()
    {
        try { RemoteCopiesReady?.Invoke(); }
        catch (Exception ex) { App.Logger?.Debug("DtrhAssetManifest: copies-ready handler threw: {E}", ex.Message); }
    }

    /// <summary>"scrolller/EroticHypnosis/12345" + ".webm" -> "EroticHypnosis-12345.webm".
    /// The page never shows these, but DtrhAssetStatsStore keys engagement by name, so they
    /// must not be able to collide with a preset filename (same reason mod entries carry a
    /// "mod:" prefix). The "online&lt;pct&gt;:" prefix the caller adds does that job.</summary>
    private static string NameFor(RemoteEntry e)
    {
        var parts = e.Id.Split('/');
        var stem = parts.Length >= 3 ? $"{parts[1]}-{parts[2]}" : e.Id.Replace('/', '-');
        var ext = "";
        int cut = e.Url.IndexOfAny(new[] { '?', '#' });
        var clean = cut >= 0 ? e.Url[..cut] : e.Url;
        int dot = clean.LastIndexOf('.');
        if (dot > 0 && clean.Length - dot <= 6) ext = clean[dot..];
        return stem + ext;
    }

    /// <summary>The cache, loaded from disk on first use and pruned of stale entries.
    /// Returns a snapshot the caller can enumerate without holding the lock.</summary>
    private static List<RemoteEntry> LoadRemoteCache()
    {
        lock (RemoteLock)
        {
            if (_remoteCache == null)
            {
                _remoteCache = new List<RemoteEntry>();
                try
                {
                    if (File.Exists(RemoteCachePath))
                    {
                        var o = JObject.Parse(File.ReadAllText(RemoteCachePath));
                        if (o["entries"] is JArray arr)
                        {
                            foreach (var t in arr)
                            {
                                var id = (string?)t["id"];
                                var url = (string?)t["url"];
                                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)) continue;
                                _remoteCache.Add(new RemoteEntry(id!, url!, (bool?)t["image"] ?? false,
                                    (long?)t["at"] ?? 0, (bool?)t["loop"] ?? false));
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    App.Logger?.Debug("DtrhAssetManifest: remote cache load failed: {E}", ex.Message);
                }
            }
            PruneRemoteLocked();
            return new List<RemoteEntry>(_remoteCache);
        }
    }

    /// <summary>
    /// Drop entries older than the TTL (a CDN url is not forever). Caller holds
    /// <see cref="RemoteLock"/>.
    /// </summary>
    private static void PruneRemoteLocked()
    {
        if (_remoteCache == null) return;
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        _remoteCache.RemoveAll(e => !RemoteEntryFresh(e, now));

        // Oldest first, per kind, so a run of clips can never push the last stills out.
        foreach (var kind in new Func<RemoteEntry, bool>[]
                 { e => e.IsImage, e => e.IsLoop, e => !e.IsImage && !e.IsLoop })
        {
            int over = _remoteCache.Count(e => kind(e)) - MaxRemoteEntries / 2;
            if (over > 0) _remoteCache.RemoveAll(e => kind(e) && over-- > 0);
        }
    }

    /// <summary>(stills, clips) in the cache. Caller holds <see cref="RemoteLock"/>.</summary>
    internal static (int Stills, int Clips) CountKinds(IReadOnlyCollection<RemoteEntry>? cache)
    {
        if (cache == null) return (0, 0);
        int stills = cache.Count(e => e.IsImage);
        return (stills, cache.Count - stills);
    }

    /// <summary>Top the cache up off the UI thread, single-flight. Fire-and-forget by design:
    /// whatever lands is for the NEXT manifest, so nothing waits on it.</summary>
    private static void KickRemoteRefill()
    {
        // Each kind has its own low-water mark. One Any batch off a video-only sub used to fill
        // the cache with 30 clips and nothing else, and a full cache never refilled, so the
        // Goon Game's flash bubbles and drain wash had no picture to draw (Sep 23 desk run).
        // Three kinds now: stills, moving pictures (loops) and long clips. The neediest one
        // under its mark gets this fetch; a tie goes loops, stills, clips.
        bool wantStills, wantLoops;
        bool loopsDry = DateTimeOffset.UtcNow.ToUnixTimeSeconds() < Interlocked.Read(ref _loopsDryUntilUnix);
        lock (RemoteLock)
        {
            var (stills, rest) = CountKinds(_remoteCache);
            int loops = _remoteCache?.Count(e => e.IsLoop) ?? 0;
            int clips = rest - loops;
            if (stills >= RemoteKindLowWater && (loopsDry || loops >= RemoteKindLowWater) && clips >= RemoteKindLowWater) return;
            wantLoops = !loopsDry && loops < RemoteKindLowWater && loops <= stills && loops <= clips;
            wantStills = !wantLoops && stills < RemoteKindLowWater && stills <= clips;
        }
        if (Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0) return;
        _ = Task.Run(async () =>
        {
            try
            {
                // Stills come from GIF posts' posters, the same kind the flashes draw; PICTURE
                // pages are mostly memes and screenshots. Separate tenants keep the rotations apart.
                var coord = wantLoops
                    ? FypOnlineCoordinator.For(RemoteConsumerId + "-loops", RemoteChannels, FeedMediaKind.GifClip)
                    : wantStills
                    ? FypOnlineCoordinator.For(RemoteConsumerId + "-stills", RemoteChannels, FeedMediaKind.GifStill)
                    : FypOnlineCoordinator.For(RemoteConsumerId, RemoteChannels, FeedMediaKind.Video);
                var (entries, error) = await coord.FetchBatchAsync(CancellationToken.None).ConfigureAwait(false);
                if (error != null)
                {
                    App.Logger?.Debug("DtrhAssetManifest: remote refill failed ({E})", error);
                    if (wantLoops) MarkLoopsDry();
                    return;
                }
                int kept = 0;
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                List<RemoteEntry> after;
                lock (RemoteLock)
                {
                    _remoteCache ??= new List<RemoteEntry>();
                    var known = new HashSet<string>(_remoteCache.Select(e => e.Id), StringComparer.Ordinal);
                    foreach (var e in entries)
                    {
                        // RemoteMediaFormats is the ONE authority on what a remote entry may
                        // be (blocker B7). The local extension lists at the top of this file
                        // describe a disk the user controls; they do not describe a CDN.
                        if (!RemoteMediaFormats.Validate(e, FeedMediaKind.Any, out var reason))
                        {
                            App.Logger?.Debug("DtrhAssetManifest: rejected remote entry {Id}: {Reason}", e.Id, reason);
                            continue;
                        }
                        if (!known.Add(e.Id)) continue;
                        bool image = e.Type == RemoteMediaFormats.TypeImage;
                        _remoteCache.Add(new RemoteEntry(e.Id, e.Url, image, now, wantLoops && !image));
                        kept++;
                    }
                    PruneRemoteLocked();
                    SaveRemoteCacheLocked();
                    after = new List<RemoteEntry>(_remoteCache);
                }
                if (wantLoops && kept == 0) MarkLoopsDry();
                // Copy what just landed now, so a page that is already up gets it this session
                // (RemoteCopiesReady re-posts the manifest) and not one launch later.
                if (kept > 0) KickLocalCopies(after);
                if (kept > 0)
                    App.Logger?.Information("DtrhAssetManifest: remote pool +{N} (next launch shows them)", kept);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("DtrhAssetManifest: remote refill threw: {E}", ex.Message);
            }
            finally { Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
        });
    }

    /// <summary>When a loops fetch came back empty or failed, the loops kind sits out until
    /// this time, so stills and clips still get their refills (a tie used to go to loops
    /// forever while it stood at zero).</summary>
    private static long _loopsDryUntilUnix;
    private static void MarkLoopsDry() =>
        Interlocked.Exchange(ref _loopsDryUntilUnix, DateTimeOffset.UtcNow.ToUnixTimeSeconds() + 1800);

    /// <summary>DTRH's channel set. NICHE SELECTION IS SHARED APP-WIDE on purpose (see the
    /// AppSettings remarks next to MediaSource): one taxonomy, one selection, many surfaces.
    /// Only the rotation/dwell state is per-consumer, which is what the tenant id buys.</summary>
    private static IReadOnlyList<string> RemoteChannels()
    {
        var s = App.Settings?.Current;
        return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
    }

    /// <summary>Caller holds <see cref="RemoteLock"/>. Never throws.</summary>
    private static void SaveRemoteCacheLocked()
    {
        try
        {
            var arr = new JArray();
            foreach (var e in _remoteCache ?? new List<RemoteEntry>())
                arr.Add(new JObject { ["id"] = e.Id, ["url"] = e.Url, ["image"] = e.IsImage, ["at"] = e.AtUnix, ["loop"] = e.IsLoop });
            Directory.CreateDirectory(App.UserDataPath);
            File.WriteAllText(RemoteCachePath,
                new JObject { ["entries"] = arr }.ToString(Newtonsoft.Json.Formatting.None));
        }
        catch (Exception ex)
        {
            App.Logger?.Debug("DtrhAssetManifest: remote cache save failed: {E}", ex.Message);
        }
    }
}
