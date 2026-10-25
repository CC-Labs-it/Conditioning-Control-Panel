using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Services.Flash
{
    /// <summary>
    /// The ready pool of online clips (WPF Services/Flash/RemoteFlashPool.cs, unchanged). The
    /// caller holds its lock. A selection generation rejects stale downloads; every drawn clip
    /// moves to the SHOWN list, and once fresh runs dry a burst reuses the least recently shown.
    /// </summary>
    internal sealed class RemoteFlashPool
    {
        internal const int ShownMax = 256;

        private readonly List<string> _ready = new();
        private readonly LinkedList<string> _shown = new();
        private string _selection = "";
        internal int Generation { get; private set; }
        internal int Count => _ready.Count;
        internal int Available => _ready.Count + _shown.Count;
        internal bool Select(IEnumerable<string> channels)
        {
            var key = string.Join("|", channels.Select(x => x.ToLowerInvariant()).Distinct().OrderBy(x => x));
            if (key == _selection) return false;
            _selection = key;
            Clear();
            return true;
        }
        internal bool Add(int generation, string url)
        {
            if (generation != Generation || _ready.Contains(url)) return false;
            _shown.Remove(url);
            _ready.Add(url);
            return true;
        }
        internal string? Take(Random random, Func<string, bool> usable)
        {
            while (_ready.Count > 0)
            {
                int index = random.Next(_ready.Count);
                var url = _ready[index];
                _ready.RemoveAt(index);
                if (!usable(url)) continue;
                Remember(url);
                return url;
            }
            return null;
        }
        internal string? TakeShown(Func<string, bool> usable)
        {
            while (_shown.First is { } node)
            {
                _shown.RemoveFirst();
                if (!usable(node.Value)) continue;
                _shown.AddLast(node);
                return node.Value;
            }
            return null;
        }
        internal void Clear() { Generation++; _ready.Clear(); _shown.Clear(); }

        private void Remember(string url)
        {
            _shown.Remove(url);
            _shown.AddLast(url);
            while (_shown.Count > ShownMax) _shown.RemoveFirst();
        }
    }

    /// <summary>One remote flash ready to show: the URL is its identity (history, session log),
    /// the poster and clip are files already on disk.</summary>
    internal readonly record struct RemoteFlashItem(string Url, string PosterPath, string ClipPath);

    /// <summary>
    /// The source rules of WPF FlashService.GetNextImages / ShouldDrawRemote / RemoteFlashesEnabled
    /// (Services/Flash/FlashService.cs:3714, :4021, :4188), pure so both heads and the tests share them.
    /// </summary>
    internal static class FlashSourceRules
    {
        /// <summary>WPF RemoteFlashesEnabled: the source is not "local" AND the user consented
        /// (HasRemoteMediaConsent, so a For You consent counts).</summary>
        internal static bool RemoteEnabled(AppSettings? s)
            => s != null
               && !string.Equals(s.MediaSource, "local", StringComparison.OrdinalIgnoreCase)
               && s.HasRemoteMediaConsent;

        /// <summary>WPF ShouldDrawRemote: no local pool = all remote; "online" = all remote;
        /// "mixed" = RemoteMediaRatio percent remote. Re-reads the gate every pick (#1037).</summary>
        internal static bool ShouldDrawRemote(AppSettings? s, bool haveLocal, Random rng)
        {
            if (!RemoteEnabled(s)) return false;
            if (!haveLocal) return true;
            if (string.Equals(s!.MediaSource, "online", StringComparison.OrdinalIgnoreCase)) return true;
            return rng.Next(100) < Math.Clamp(s.RemoteMediaRatio, 0, 100);
        }

    }

    /// <summary>
    /// The third flash pool, online clips (WPF FlashService "Remote media (Phase 3 - Contract 2)").
    /// Scrolller's GIF feed through the "flashes" tenant of <see cref="FypOnlineCoordinator"/>,
    /// the same niche selection the For You feed uses. Each entry is materialized (clip + poster
    /// on disk) before it enters the ready pool, so a draw never waits on the network. Off unless
    /// <see cref="FlashSourceRules.RemoteEnabled"/>; a source that is down reads as "no remote
    /// content", never as broken flashes.
    /// </summary>
    internal static class RemoteFlashSource
    {
        private const string ConsumerId = "flashes";
        private const int ReadyTarget = 12;
        private const int ReadyMax = 24;
        private const int PrefetchGapSeconds = 1;
        internal const int ClipMaxBytes = 40 * 1024 * 1024;
        internal const int PosterMaxBytes = 8 * 1024 * 1024;

        private static readonly RemoteFlashPool Pool = new();
        private static readonly object Lock = new();
        private static readonly RemoteClipFiles Clips = new();
        private static CancellationTokenSource Cts = new();
        private static bool _inFlight;
        private static DateTime _lastFetchUtc = DateTime.MinValue;
        private static Task _prefetch = Task.CompletedTask;

        /// <summary>The latest background top-up (tests await it instead of polling).</summary>
        internal static Task Prefetching { get { lock (Lock) return _prefetch; } }

        /// <summary>Settings reader (tests swap it).</summary>
        internal static Func<AppSettings?> Settings = () => CoreSettings.Current;

        /// <summary>Batch fetcher (tests swap it). Default = the coordinator tenant.</summary>
        internal static Func<CancellationToken, Task<(List<FypAssetManifest.Entry> Entries, string? Error)>> Fetch = DefaultFetch;

        /// <summary>Poster/clip download (tests swap it). Default = the shared Scrolller device cache,
        /// like WPF RemoteMediaCache (:486): it honours MayFetchFresh and consent, sends the honest
        /// user-agent and charges the allowance; a cached copy costs nothing.</summary>
        internal static Func<string, long, CancellationToken, Task<string?>> Download =
            (url, maxBytes, ct) => ScrolllerDiskCache.GetOrFetchAsync(url, null, maxBytes, ct);

        private static IReadOnlyList<string> Channels()
        {
            var s = Settings();
            return FypOnlineCoordinator.ResolveChannels(s?.FypOnlineNiches, s?.FypOnlineCustomSubs);
        }

        private static Task<(List<FypAssetManifest.Entry> Entries, string? Error)> DefaultFetch(CancellationToken ct)
            => FypOnlineCoordinator.For(ConsumerId, Channels, FeedMediaKind.GifClip).FetchBatchAsync(ct);

        private static void RefreshSelection()
        {
            lock (Lock)
                if (Pool.Select(Channels())) _lastFetchUtc = DateTime.MinValue;
        }

        /// <summary>Clips ready to draw (fresh + reusable shown).</summary>
        internal static int ReadyCount
        {
            get { lock (Lock) return Pool.Available; }
        }

        /// <summary>Kick a background top-up; returns at once. Cheap to call on every draw.</summary>
        internal static void EnsurePrefetch()
        {
            RefreshSelection();
            if (!FlashSourceRules.RemoteEnabled(Settings())) return;
            lock (Lock)
            {
                if (_inFlight || Pool.Count >= ReadyTarget) return;
                if ((DateTime.UtcNow - _lastFetchUtc).TotalSeconds < PrefetchGapSeconds) return;
                _inFlight = true;
                _lastFetchUtc = DateTime.UtcNow;
                var ct = Cts.Token;
                var generation = Pool.Generation;
                _prefetch = Task.Run(() => PrefetchBatchAsync(ct, generation));
            }
        }

        /// <summary>Consume one ready clip (fresh first, then the least recently shown).</summary>
        internal static RemoteFlashItem? TryTake(Random rng)
        {
            RefreshSelection();
            if (!FlashSourceRules.RemoteEnabled(Settings())) return null;
            lock (Lock)
            {
                Func<string, bool> usable = url => Clips.TryUse(url, Environment.TickCount64, out var clip, out var poster) && File.Exists(poster) && File.Exists(clip);
                var url = Pool.Take(rng, usable) ?? Pool.TakeShown(usable);
                return url != null && Clips.TryUse(url, Environment.TickCount64, out var path, out var posterPath)
                    ? new RemoteFlashItem(url, posterPath, path) : null;
            }
        }

        internal static string? ClipPath(string url)
        {
            lock (Lock) return Clips.TryUse(url, Environment.TickCount64, out var path, out _) && File.Exists(path) ? path : null;
        }

        // Panic and final exit (WPF FlashService teardown :3846-3848, :5141-5145) drop the warm pool
        // and invalidate in-flight downloads; owned clip copies are released outside the draw lock.
        // A later user trigger may warm again.
        internal static void CleanupCache()
        {
            List<string> dropped;
            lock (Lock)
            {
                Cts.Cancel();
                Cts = new CancellationTokenSource();
                Pool.Clear();
                dropped = Clips.TakeAll();
                _lastFetchUtc = DateTime.MinValue;
            }
            foreach (var path in dropped) ReleaseClip(path);
        }

        // ponytail: copy only clips after the device-cache consent/allowance gate; its sweep may
        // remove its copy at any time. Posters stay in that cache, matching the WPF still path.
        // Named like WPF RemoteMediaCache (:166) so the startup sweep (ccp_temp_*) and the cache
        // off-switch purge (ccp_temp_remote_*) both reclaim copies a crash or kill left behind.
        private static string OwnClip(string source)
        {
            var folder = CorePaths.MediaTemp();
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, ScrolllerDiskCache.TempPrefix + Guid.NewGuid().ToString("N") + Path.GetExtension(source));
            try { File.Copy(source, path); return path; }
            catch { ReleaseClip(path); throw; }
        }

        private static void ReleaseClip(string path)
        {
            try { File.Delete(path); }
            catch (Exception ex) { Log.Debug("Flash: clip release failed: {Error}", ex.Message); }
        }

        private static async Task PrefetchBatchAsync(CancellationToken ct, int generation)
        {
            try
            {
                var entries = new List<FypAssetManifest.Entry>();
                string? error = null;
                // Small portions from several channels prevent one page dominating the pool.
                for (var batch = 0; batch < 3; batch++)
                {
                    var result = await Fetch(ct).ConfigureAwait(false);
                    entries.AddRange(result.Entries.Take(4));
                    error = result.Error;
                    if (ct.IsCancellationRequested) return;
                }
                if (error != null && entries.Count == 0)
                {
                    Log.Debug("Flash: remote fetch failed ({Error}); staying on the local pool", error);
                    return;
                }

                var warmed = 0;
                foreach (var entry in entries)
                {
                    if (ct.IsCancellationRequested) break;
                    lock (Lock) if (Pool.Count >= ReadyMax) break;
                    if (!RemoteMediaFormats.Validate(entry, FeedMediaKind.GifClip, out var reason))
                    {
                        Log.Debug("Flash: dropped remote entry {Id}: {Reason}", entry?.Id, reason);
                        continue;
                    }
                    if (string.IsNullOrEmpty(entry.PosterUrl)) continue;
                    var url = entry.SmallUrl ?? entry.Url;
                    var held = ClipPath(url);
                    string? path = held;
                    bool ours = false;
                    List<string>? dropped = null;
                    try
                    {
                        if (path == null)
                        {
                            var cached = await Download(url, ClipMaxBytes, ct).ConfigureAwait(false);
                            if (cached == null) continue;
                            path = OwnClip(cached);
                            ours = true;
                        }
                        var poster = await Download(entry.PosterUrl!, PosterMaxBytes, ct).ConfigureAwait(false);
                        if (poster == null) continue;
                        RefreshSelection();
                        lock (Lock)
                        {
                            if (ct.IsCancellationRequested || generation != Pool.Generation) return;
                            dropped = Clips.Put(url, path, poster, Environment.TickCount64);
                            ours = false;
                            if (Pool.Add(generation, url)) warmed++;
                        }
                    }
                    finally
                    {
                        // Even cancellation during poster fetch must release an unpublished copy.
                        if (ours && path != null) ReleaseClip(path);
                        if (dropped != null) foreach (var old in dropped) ReleaseClip(old);
                    }
                }
                if (warmed > 0) Log.Information("Flash: warmed {Warmed} remote clip(s), {Ready} ready", warmed, ReadyCount);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Log.Debug("Flash: remote prefetch failed (non-fatal): {Error}", ex.Message); }
            finally
            {
                lock (Lock) _inFlight = false;
            }
        }
    }
}
