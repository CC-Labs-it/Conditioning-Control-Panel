using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp.Online;
using Serilog;

namespace ConditioningControlPanel.Services;

/// <summary>WPF VideoService.cs:8142-8369: bounded, non-blocking online video buffer.</summary>
internal sealed class RemoteVideoPool
{
    private readonly object _gate = new();
    private readonly Queue<string> _queue = new();
    private int _fetching;
    internal Func<Task>? FetchForTests;
    internal Func<string, Task<string?>>? DownloadForTests;
    internal Func<string, bool>? CanFetchForTests;
    internal Func<string, bool> Cached = ScrolllerDiskCache.Contains;
    internal Task? PendingForTests;
    internal void SeedForTests(params string[] urls) { lock (_gate) foreach (var url in urls) _queue.Enqueue(url); }
    internal int CountForTests { get { lock (_gate) return _queue.Count; } }
    private static bool Enabled => CoreSettings.Current.MediaSource != "local" && CoreSettings.Current.HasRemoteMediaConsent;

    internal string? Take()
    {
        if (!Enabled) return null;
        string? picked = null;
        lock (_gate)
        {
            if (!ScrolllerPolicy.Enabled) { _queue.Clear(); return null; }
            var count = _queue.Count;
            while (count-- > 0)
            {
                var url = _queue.Dequeue();
                if (!ScrolllerPolicy.IsScrolllerUrl(url)) { picked = url; break; }
                if (Cached(url)) { picked = url; break; }
                if (CanFetchForTests?.Invoke(url) ?? ScrolllerPolicyService.MayFetchFresh(url)) _queue.Enqueue(url);
            }
        }
        Prefetch();
        return picked;
    }

    internal void Prefetch()
    {
        if (!Enabled || !ScrolllerPolicy.Enabled || Interlocked.CompareExchange(ref _fetching, 1, 0) != 0) return;
        PendingForTests = Task.Run(async () =>
        {
            try
            {
                int count;
                lock (_gate) count = _queue.Count;
                if (count < 4)
                {
                    if (FetchForTests != null) await FetchForTests();
                    else
                    {
                        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                        var coordinator = FypOnlineCoordinator.For("videos", () => FypOnlineCoordinator.ResolveChannels(
                            CoreSettings.Current.FypOnlineNiches, CoreSettings.Current.FypOnlineCustomSubs), FeedMediaKind.Video);
                        var (entries, error) = await coordinator.FetchBatchAsync(timeout.Token).ConfigureAwait(false);
                        if (error != null) Log.Warning("VideoService: remote fetch failed: {Error}", error);
                        if (!Enabled || !ScrolllerPolicy.Enabled) return;
                        lock (_gate)
                        {
                            foreach (var e in entries)
                            {
                                if (_queue.Count >= 60) break;
                                if (RemoteMediaFormats.Validate(e, FeedMediaKind.Video, out _) && !_queue.Contains(e.Url)) _queue.Enqueue(e.Url);
                            }
                        }
                    }
                }
                string[] urls;
                lock (_gate) urls = _queue.Where(ScrolllerPolicy.IsScrolllerUrl).ToArray();
                int ready = 0;
                foreach (var url in urls)
                {
                    if (!Enabled || !ScrolllerPolicy.Enabled || ready >= 3) break;
                    if (Cached(url)) { ready++; continue; }
                    if (!(CanFetchForTests?.Invoke(url) ?? ScrolllerPolicyService.MayFetchFresh(url))) continue;
                    if (await (DownloadForTests?.Invoke(url) ?? ScrolllerDiskCache.GetOrFetchAsync(url, ScrolllerItemIndex.MetaFor(url), CancellationToken.None)).ConfigureAwait(false) != null) ready++;
                    else lock (_gate)
                    {
                        var kept = _queue.Where(u => !string.Equals(u, url, StringComparison.Ordinal)).ToArray();
                        _queue.Clear();
                        foreach (var item in kept) _queue.Enqueue(item);
                    }
                }
            }
            catch (Exception ex) { Log.Warning(ex, "VideoService: remote prefetch failed"); }
            finally { Interlocked.Exchange(ref _fetching, 0); }
        });
    }
}
