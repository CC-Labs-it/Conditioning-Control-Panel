using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Deeper;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Newtonsoft.Json.Linq;
using Serilog;

namespace ConditioningControlPanel.Services.Chaos
{
    /// <summary>
    /// The DtRH manifest's online half (WPF Services/Chaos/DtrhAssetManifest.cs "remote media", moved here so
    /// both heads share it). A bounded cache of provider urls (dtrh_remote_media.json, 24 h TTL, 30 per kind)
    /// rides on top of the library when the app-wide source has left "local" and the user consented. Each entry
    /// gets a copy on this machine (under <c>&lt;assets&gt;/.temp</c>), and a copied entry goes out as the head's
    /// asset url so the page's WebGL layer may draw it (hostMedia.js decides remote-ness by origin). Three kinds:
    /// stills, moving pictures (loops: a gif post's clip, name marked <c>loop:</c>) and long clips; the neediest
    /// one under its low-water mark gets the next fetch, and a loops fetch that comes back dry or fails sits the
    /// loops kind out for 30 minutes so stills and clips still refill. Never throws.
    /// </summary>
    internal static class DtrhRemoteMedia
    {
        /// <summary><paramref name="IsLoop"/>: a MOVING PICTURE, the clip half of a gif post (FeedMediaKind.GifClip),
        /// shown where a picture goes: silent, looping.</summary>
        internal sealed record RemoteEntry(string Id, string Url, bool IsImage, long AtUnix, bool IsLoop = false);

        internal enum Kind { Loops, Stills, Clips }

        private const string RemoteNamePrefix = "online";
        private const string LoopNamePrefix = "loop:";
        private const string RemoteConsumerId = "dtrh";
        internal const int MaxRemoteEntries = 60;
        internal const int RemoteKindLowWater = 12;
        /// <summary>Licence day limit (owner, 2026-10-21): a remote entry, and any copy of its bytes, lives at most a day.</summary>
        internal const long RemoteEntryTtlSeconds = 24 * 3600;
        internal const int OnlineOnlyMinCopies = 8;
        internal const int MaxClipCopies = 24;
        internal const int CopyReadyEvery = 10;
        internal const long LoopsDrySeconds = 1800;
        /// <summary>WPF RemoteMediaCache.TempPrefix: the startup sweep of <c>.temp/ccp_temp_*</c> takes leftovers.</summary>
        internal const string CopyPrefix = "ccp_temp_remote_";
        /// <summary>WPF RemoteMediaCache.MaxSingleEntryBytes.</summary>
        internal const long MaxCopyBytes = 24L * 1024 * 1024;

        // ------------------------------------------------------------ rules ------------------------------------------------------------

        /// <summary>The remote share stamped on each name (0..100), or null when the online half is off: the app-wide
        /// source is "local" or no consent card was accepted (WPF AppendRemote's two gates).</summary>
        internal static int? Share(AppSettings? s) =>
            s == null || s.MediaSource == "local" || !s.HasRemoteMediaConsent ? null
            : s.MediaSource == "online" ? 100 : Math.Clamp(s.RemoteMediaRatio, 5, 95);

        /// <summary>"online&lt;pct&gt;:[loop:]&lt;sub&gt;-&lt;postId&gt;.&lt;ext&gt;": hostMedia.js reads the share and the loop marker
        /// off it; the prefix also keeps it from colliding with a library filename in the asset stats.</summary>
        internal static string EntryName(int pct, RemoteEntry e) =>
            $"{RemoteNamePrefix}{pct}:{(e.IsLoop ? LoopNamePrefix : "")}{NameFor(e)}";

        /// <summary>"scrolller/EroticHypnosis/12345" + ".webm" -> "EroticHypnosis-12345.webm".</summary>
        internal static string NameFor(RemoteEntry e)
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

        /// <summary>"Online" is online only: once enough copies are here to dress the tube, the library steps aside.</summary>
        internal static bool LibraryStepsAside(int pct, int copies) => pct == 100 && copies >= OnlineOnlyMinCopies;

        /// <summary>What to copy next: moving pictures first, then stills, then at most 24 long clips; never one
        /// that already has a copy.</summary>
        internal static List<RemoteEntry> CopyOrder(IReadOnlyList<RemoteEntry> pool, Func<string, bool> hasCopy) =>
            pool.Where(e => e.IsLoop && !hasCopy(e.Url))
                .Concat(pool.Where(e => e.IsImage && !hasCopy(e.Url)))
                .Concat(pool.Where(e => !e.IsImage && !e.IsLoop).Take(MaxClipCopies).Where(e => !hasCopy(e.Url)))
                .ToList();

        /// <summary>Drop entries past the TTL, then the oldest per kind over half the cap, so a run of clips can
        /// never push the last stills out.</summary>
        internal static void Prune(List<RemoteEntry> cache, long nowUnix)
        {
            cache.RemoveAll(e => !RemoteEntryFresh(e, nowUnix));
            foreach (var kind in new Func<RemoteEntry, bool>[] { e => e.IsImage, e => e.IsLoop, e => !e.IsImage && !e.IsLoop })
            {
                int over = cache.Count(e => kind(e)) - MaxRemoteEntries / 2;
                if (over > 0) cache.RemoveAll(e => kind(e) && over-- > 0);
            }
        }

        /// <summary>The day limit on one entry (WPF DtrhAssetManifest.RemoteEntryFresh): fresh while younger than
        /// <see cref="RemoteEntryTtlSeconds"/>. No time (0) or a time more than an hour ahead (the clock went back) is stale.</summary>
        internal static bool RemoteEntryFresh(RemoteEntry e, long nowUnix)
            => e.AtUnix > 0 && e.AtUnix - nowUnix <= 3600 && nowUnix - e.AtUnix < RemoteEntryTtlSeconds;

        /// <summary>(stills, clips) in the cache; loops count as clips.</summary>
        internal static (int Stills, int Clips) CountKinds(IReadOnlyCollection<RemoteEntry>? cache)
        {
            if (cache == null) return (0, 0);
            int stills = cache.Count(e => e.IsImage);
            return (stills, cache.Count - stills);
        }

        /// <summary>The kind the next fetch tops up, or null when every kind (loops excused while dry) is at its
        /// low-water mark. The neediest under its mark wins; a tie goes loops, stills, clips.</summary>
        internal static Kind? PickRefill(IReadOnlyCollection<RemoteEntry>? cache, bool loopsDry)
        {
            var (stills, rest) = CountKinds(cache);
            int loops = cache?.Count(e => e.IsLoop) ?? 0;
            int clips = rest - loops;
            if (stills >= RemoteKindLowWater && (loopsDry || loops >= RemoteKindLowWater) && clips >= RemoteKindLowWater) return null;
            if (!loopsDry && loops < RemoteKindLowWater && loops <= stills && loops <= clips) return Kind.Loops;
            if (stills < RemoteKindLowWater && stills <= clips) return Kind.Stills;
            return Kind.Clips;
        }

        internal static List<RemoteEntry> ParseCache(string json)
        {
            var list = new List<RemoteEntry>();
            if (JObject.Parse(json)["entries"] is not JArray arr) return list;
            foreach (var t in arr)
            {
                var id = (string?)t["id"];
                var url = (string?)t["url"];
                if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(url)) continue;
                list.Add(new RemoteEntry(id!, url!, (bool?)t["image"] ?? false, (long?)t["at"] ?? 0, (bool?)t["loop"] ?? false));
            }
            return list;
        }

        internal static string SerializeCache(IEnumerable<RemoteEntry> cache)
        {
            var arr = new JArray();
            foreach (var e in cache)
                arr.Add(new JObject { ["id"] = e.Id, ["url"] = e.Url, ["image"] = e.IsImage, ["at"] = e.AtUnix, ["loop"] = e.IsLoop });
            return new JObject { ["entries"] = arr }.ToString(Newtonsoft.Json.Formatting.None);
        }

        // ------------------------------------------------------------ state ------------------------------------------------------------

        /// <summary>Test seams (P08): the clock, and the provider fetch for one kind.</summary>
        internal static Func<long> NowUnix = () => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        internal static Func<Kind, CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> Fetch = FetchFromProvider;

        /// <summary>The last background refill / copy pass, so a test awaits the side effect itself (P56).</summary>
        internal static Task RefillTask = Task.CompletedTask, CopyTask = Task.CompletedTask;

        private static readonly object RemoteLock = new();
        private static List<RemoteEntry>? _remoteCache;      // null = not loaded from disk yet
        private static int _remoteFetchInFlight, _copyInFlight;
        private static long _loopsDryUntilUnix;
        /// <summary>Remote url -> the temp file holding its bytes.</summary>
        private static readonly ConcurrentDictionary<string, string> LocalCopies = new(StringComparer.Ordinal);

        /// <summary>Raised off the UI thread when new copies landed: the host re-posts the manifest so a page that
        /// is already up starts drawing them.</summary>
        internal static event Action? CopiesReady;

        /// <summary>Tests: how many hosts listen (a closed window must not, P68).</summary>
        internal static int CopiesReadyListeners => CopiesReady?.GetInvocationList().Length ?? 0;

        private static string CachePath => Path.Combine(CorePaths.UserData, "dtrh_remote_media.json");

        /// <summary>Copies a head hands out. <paramref name="materialize"/> fetches one url to a local file (null =
        /// failed); <paramref name="release"/> deletes a file nobody took.</summary>
        internal sealed record Copier(Func<string, CancellationToken, Task<string?>> Materialize, Action<string> Release);

        /// <summary>The cached pool as (isImage, name, url) for the manifest, plus whether the library steps aside;
        /// null when the online half is off. Tops the cache up and copies in the background.
        /// <paramref name="copyUrl"/> turns a copy's full path into the page's asset url.</summary>
        internal static (List<(bool IsImage, string Name, string Url)> Entries, bool LibraryStepsAside)? Take(
            AppSettings? settings, Func<string, string> copyUrl, Copier copier)
        {
            try
            {
                if (Share(settings) is not int pct) return null;
                var pool = LoadCache();
                var entries = new List<(bool, string, string)>(pool.Count);
                int copies = 0;
                foreach (var e in pool)
                {
                    // The licence switch is off: no Scrolller entry reaches the page, copied or not.
                    if (ScrolllerPolicyService.Blocks(e.Url)) continue;
                    var url = e.Url;
                    if (LocalCopies.TryGetValue(e.Url, out var copy) && File.Exists(copy)
                        && !DropStaleCopy(e.Url, copy, DateTime.UtcNow, copier.Release)) { url = copyUrl(copy); copies++; }
                    entries.Add((e.IsImage, EntryName(pct, e), url));
                }
                KickRefill(copier);
                KickCopies(pool, copier);
                return (entries, LibraryStepsAside(pct, copies));
            }
            catch (Exception ex)
            {
                Log.Debug("DtrhRemoteMedia: remote append failed: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>The Core manifest with the online half on top (WPF DtrhAssetManifest.Build after the downsample).</summary>
        internal static int AppendTo(GameMediaManifest.Manifest m, AppSettings? settings, Func<string, string> copyUrl, Copier copier)
        {
            if (Take(settings, copyUrl, copier) is not var (entries, stepAside)) return 0;
            if (stepAside) { m.Images.Clear(); m.Videos.Clear(); }
            foreach (var (isImage, name, url) in entries) (isImage ? m.Images : m.Videos).Add(new GameMediaManifest.Entry(name, url));
            return entries.Count;
        }

        private static List<RemoteEntry> LoadCache()
        {
            lock (RemoteLock)
            {
                if (_remoteCache == null)
                {
                    try { _remoteCache = File.Exists(CachePath) ? ParseCache(File.ReadAllText(CachePath)) : new(); }
                    catch (Exception ex) { _remoteCache = new(); Log.Debug("DtrhRemoteMedia: cache load failed: {E}", ex.Message); }
                }
                Prune(_remoteCache, NowUnix());
                return new List<RemoteEntry>(_remoteCache);
            }
        }

        private static void KickCopies(List<RemoteEntry> pool, Copier copier)
        {
            var todo = CopyOrder(pool, LocalCopies.ContainsKey);
            if (todo.Count == 0) return;
            if (Interlocked.CompareExchange(ref _copyInFlight, 1, 0) != 0) return;
            CopyTask = Task.Run(async () =>
            {
                int landed = 0, told = 0;
                try
                {
                    foreach (var e in todo)
                    {
                        if (Share(CoreSettings.Current) == null) break;
                        var path = await copier.Materialize(e.Url, CancellationToken.None).ConfigureAwait(false);
                        if (path == null) continue;
                        if (!LocalCopies.TryAdd(e.Url, path)) { copier.Release(path); continue; }
                        landed++;
                        if (landed - told >= CopyReadyEvery) { told = landed; RaiseCopiesReady(); }
                    }
                }
                catch (Exception ex) { Log.Debug("DtrhRemoteMedia: local copies threw: {E}", ex.Message); }
                finally
                {
                    Interlocked.Exchange(ref _copyInFlight, 0);
                    if (landed > 0) Log.Information("DtrhRemoteMedia: {N} online entries copied for the tube", landed);
                    if (landed > told) RaiseCopiesReady();
                }
            });
        }

        /// <summary>The day limit on one copy (WPF DtrhAssetManifest.DropStaleCopy): a file whose download time
        /// (<see cref="ScrolllerDiskCache.BornUtcOf"/>, right on Linux too) is a day old at <paramref name="nowUtc"/>
        /// leaves the map and is released. True when it went.</summary>
        private static bool DropStaleCopy(string url, string path, DateTime nowUtc, Action<string> release)
        {
            DateTime born;
            try { born = ScrolllerDiskCache.BornUtcOf(path); } catch { born = DateTime.MinValue; }
            if (!ScrolllerCacheBook.IsExpiredAt(born, nowUtc)) return false;
            if (LocalCopies.TryRemove(new KeyValuePair<string, string>(url, path))) release(path);
            return true;
        }

        /// <summary>The hourly sweep (WPF DtrhAssetManifest.SweepLocalCopies): every copy past the day limit, or past it
        /// before the next sweep, is dropped and released. <paramref name="all"/> drops every copy (the off switch).
        /// Returns how many went; never throws.</summary>
        internal static int SweepLocalCopies(Action<string> release, bool all = false)
        {
            int gone = 0;
            try
            {
                var ahead = DateTime.UtcNow + ScrolllerDiskCache.SweepPeriod;
                foreach (var kvp in LocalCopies.ToArray())
                {
                    if (all ? LocalCopies.TryRemove(kvp) && Release(kvp.Value) : DropStaleCopy(kvp.Key, kvp.Value, ahead, release)) gone++;
                }
            }
            catch (Exception ex) { Log.Debug("DtrhRemoteMedia: copy sweep failed: {E}", ex.Message); }
            return gone;

            bool Release(string path) { release(path); return true; }
        }

        /// <summary>Tests only: park a copy as <see cref="KickCopies"/> would.</summary>
        internal static bool AddLocalCopyForTests(string url, string path) => LocalCopies.TryAdd(url, path);

        /// <summary>Tests only: is <paramref name="url"/> still mapped to a copy?</summary>
        internal static bool HasLocalCopyForTests(string url) => LocalCopies.ContainsKey(url);

        private static void RaiseCopiesReady()
        {
            try { CopiesReady?.Invoke(); }
            catch (Exception ex) { Log.Debug("DtrhRemoteMedia: copies-ready handler threw: {E}", ex.Message); }
        }

        private static void KickRefill(Copier copier)
        {
            Kind? want;
            bool loopsDry = NowUnix() < Interlocked.Read(ref _loopsDryUntilUnix);
            lock (RemoteLock) want = PickRefill(_remoteCache, loopsDry);
            if (want is not Kind kind) return;
            if (Interlocked.CompareExchange(ref _remoteFetchInFlight, 1, 0) != 0) return;
            RefillTask = Task.Run(async () =>
            {
                try
                {
                    var (entries, error) = await Fetch(kind, CancellationToken.None).ConfigureAwait(false);
                    if (error != null)
                    {
                        Log.Debug("DtrhRemoteMedia: remote refill failed ({E})", error);
                        if (kind == Kind.Loops) MarkLoopsDry();
                        return;
                    }
                    int kept = 0;
                    long now = NowUnix();
                    List<RemoteEntry> after;
                    lock (RemoteLock)
                    {
                        _remoteCache ??= new();
                        var known = new HashSet<string>(_remoteCache.Select(e => e.Id), StringComparer.Ordinal);
                        foreach (var e in entries)
                        {
                            if (!RemoteMediaFormats.Validate(e, FeedMediaKind.Any, out var reason))
                            {
                                Log.Debug("DtrhRemoteMedia: rejected remote entry {Id}: {Reason}", e.Id, reason);
                                continue;
                            }
                            if (!known.Add(e.Id)) continue;
                            bool image = e.Type == RemoteMediaFormats.TypeImage;
                            _remoteCache.Add(new RemoteEntry(e.Id, e.Url, image, now, kind == Kind.Loops && !image));
                            kept++;
                        }
                        Prune(_remoteCache, now);
                        SaveCacheLocked();
                        after = new List<RemoteEntry>(_remoteCache);
                    }
                    if (kind == Kind.Loops && kept == 0) MarkLoopsDry();
                    // Copy what just landed now, so a page that is already up gets it this session.
                    if (kept > 0)
                    {
                        KickCopies(after, copier);
                        Log.Information("DtrhRemoteMedia: remote pool +{N}", kept);
                    }
                }
                catch (Exception ex) { Log.Debug("DtrhRemoteMedia: remote refill threw: {E}", ex.Message); }
                finally { Interlocked.Exchange(ref _remoteFetchInFlight, 0); }
            });
        }

        /// <summary>A loops fetch came back empty or failed: the loops kind sits out for 30 minutes (a tie used to go
        /// to loops forever while it stood at zero).</summary>
        private static void MarkLoopsDry() => Interlocked.Exchange(ref _loopsDryUntilUnix, NowUnix() + LoopsDrySeconds);

        /// <summary>Stills come from gif posts' posters; separate tenants keep the rotations apart. Niche selection is
        /// shared app-wide, only rotation state is per consumer.</summary>
        private static Task<(List<FypAssetManifest.Entry> Entries, string? Error)> FetchFromProvider(Kind kind, CancellationToken ct)
        {
            static IReadOnlyList<string> Channels()
            {
                var s = CoreSettings.Current;
                return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
            }
            var coord = kind switch
            {
                Kind.Loops => FypOnlineCoordinator.For(RemoteConsumerId + "-loops", Channels, FeedMediaKind.GifClip),
                Kind.Stills => FypOnlineCoordinator.For(RemoteConsumerId + "-stills", Channels, FeedMediaKind.GifStill),
                _ => FypOnlineCoordinator.For(RemoteConsumerId, Channels, FeedMediaKind.Video),
            };
            return coord.FetchBatchAsync(ct);
        }

        private static void SaveCacheLocked()
        {
            try
            {
                Directory.CreateDirectory(CorePaths.UserData);
                File.WriteAllText(CachePath, SerializeCache(_remoteCache ?? new()));
            }
            catch (Exception ex) { Log.Debug("DtrhRemoteMedia: cache save failed: {E}", ex.Message); }
        }

        /// <summary>Tests: forget the cache, the copies, the dry mark and the in-flight latches.</summary>
        internal static void ResetForTests()
        {
            lock (RemoteLock) _remoteCache = null;
            LocalCopies.Clear();
            Interlocked.Exchange(ref _loopsDryUntilUnix, 0);
            Interlocked.Exchange(ref _remoteFetchInFlight, 0);
            Interlocked.Exchange(ref _copyInFlight, 0);
        }

        // ------------------------------------------------- the copy on this machine (Avalonia) -------------------------------------------------

        private static readonly HttpClient Http = CreateClient();

        /// <summary>The app's own identity (honest user-agent, no Referer: the licence forbids spoofing a browser or
        /// the CDN's own site), and redirects followed. Each hop still connects through the guarded handler's
        /// connect-time check. Scrolller urls never use it: they go through the device cache.</summary>
        private static HttpClient CreateClient()
        {
            var handler = UrlSafety.CreateGuardedHandler();
            handler.AllowAutoRedirect = true;
            handler.MaxAutomaticRedirections = 5;
            var c = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
            c.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "image/webp,image/jpeg,image/png,video/*,*/*;q=0.8");
            return c;
        }

        /// <summary>The Avalonia head's copier (WPF uses RemoteMediaCache.MaterializeAsync(ownerReleases: true)):
        /// provider -> <c>&lt;assets&gt;/.temp/ccp_temp_remote_&lt;guid&gt;&lt;ext&gt;</c>, a remote image/video url only,
        /// at most 24 MB, nothing left behind on failure. A Scrolller url goes through the device cache, as WPF's.</summary>
        internal static readonly Copier AssetsTempCopier = new(CopyToAssetsTempAsync, ReleaseCopy);

        internal static bool IsCopyFile(string path) => Path.GetFileName(path).StartsWith(CopyPrefix, StringComparison.Ordinal);

        /// <summary>Test seam: the device cache read-or-fetch (default <see cref="ScrolllerDiskCache.GetOrFetchAsync(string, ScrolllerCacheMeta?, long, CancellationToken)"/>).</summary>
        internal static Func<string, long, CancellationToken, Task<string?>> DeviceCacheFetch =
            (url, max, ct) => ScrolllerDiskCache.GetOrFetchAsync(url, null, max, ct);

        private static async Task<string?> CopyToAssetsTempAsync(string url, CancellationToken ct)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return null;
            if (!RemoteMediaFormats.IsRemoteImage(url) && !RemoteMediaFormats.IsRemoteVideo(url)) return null;
            var assets = CorePaths.EffectiveAssets;
            if (string.IsNullOrEmpty(assets)) return null;
            if (ScrolllerPolicy.IsScrolllerUrl(url)) return await CopyScrolllerAsync(url, Path.Combine(assets, ".temp"), ct).ConfigureAwait(false);
            string? path = null;
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.TryAddWithoutValidation("User-Agent", ScrolllerSource.HonestUserAgent);
                using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxCopyBytes) return null;
                var dir = Path.Combine(assets, ".temp");
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, $"{CopyPrefix}{Guid.NewGuid():N}{Path.GetExtension(uri.AbsolutePath).ToLowerInvariant()}");
                await using (var body = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                    if (!await SaveCappedAsync(body, path, ct).ConfigureAwait(false)) { ReleaseCopy(path); return null; }
                return path;
            }
            catch (Exception ex)
            {
                if (path != null) ReleaseCopy(path);
                Log.Debug("DtrhRemoteMedia: copy failed: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// A Scrolller url (WPF RemoteMediaCache.FromScrolllerCacheAsync): refused while the licence switch is off;
        /// a cached copy costs nothing; a fresh one only when <see cref="ScrolllerPolicyService.MayFetchFresh"/> allows
        /// it, fetched by the device cache (honest user-agent, charged against the daily share as bytes arrive). The
        /// page's copy is born at the DOWNLOAD time, so the hourly sweep honours the day limit on it too.
        /// </summary>
        internal static async Task<string?> CopyScrolllerAsync(string url, string dir, CancellationToken ct)
        {
            if (ScrolllerPolicyService.Blocks(url)) return null;
            string? path = null;
            try
            {
                var cached = await DeviceCacheFetch(url, MaxCopyBytes, ct).ConfigureAwait(false);
                if (cached == null) return null;
                var info = new FileInfo(cached);
                if (!info.Exists || info.Length > MaxCopyBytes) return null;
                Directory.CreateDirectory(dir);
                path = Path.Combine(dir, $"{CopyPrefix}{Guid.NewGuid():N}{ScrolllerDiskCache.ExtensionOf(url)}");
                File.Copy(cached, path);
                var born = ScrolllerDiskCache.DownloadedUtcOf(url) ?? info.CreationTimeUtc;
                ScrolllerDiskCache.StampBorn(path, born);
                return path;
            }
            catch (Exception ex)
            {
                if (path != null) ReleaseCopy(path);
                Log.Debug("DtrhRemoteMedia: Scrolller copy failed: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>Writes at most <see cref="MaxCopyBytes"/>; false past the cap.</summary>
        internal static async Task<bool> SaveCappedAsync(Stream body, string path, CancellationToken ct, long cap = MaxCopyBytes)
        {
            await using var file = File.Create(path);
            var buffer = new byte[81920];
            long total = 0;
            int n;
            while ((n = await body.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                if ((total += n) > cap) return false;
                await file.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
            }
            return true;
        }

        /// <summary>Deletes only a copy this class made.</summary>
        private static void ReleaseCopy(string path)
        {
            try { if (IsCopyFile(path)) File.Delete(path); }
            catch (Exception ex) { Log.Debug("DtrhRemoteMedia: copy delete failed: {E}", ex.Message); }
        }
    }
}
