using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FlashClipHostTests
{
    [Fact]
    public void PanicStopsEveryOwnerOnceAndReleasesAllAdmission()
    {
        var budget = new ClipSlotBudget(8, 5);
        int stopped = 0;
        using var lease = budget.TryAcquire(() => stopped++);
        budget.StopAll();
        budget.StopAll();
        Assert.Equal(1, stopped);
        Assert.Equal(0, budget.Live);
        Assert.False(lease!.Release());
    }

    [Fact]
    public void SharedPlayersLeaveThreeSlotsForFlashesAndReleaseIsIdempotent()
    {
        var budget = new ClipSlotBudget(8, 5);
        var leases = new List<ClipSlotBudget.Lease>();
        for (int i = 0; i < 5; i++) leases.Add(budget.TryAcquire()!);
        Assert.Null(budget.TryAcquire());
        for (int i = 0; i < 3; i++) leases.Add(budget.TryAcquire(null, out _, reserved: true)!);
        Assert.Equal(8, budget.Live);
        Assert.False(budget.CanAcquireFor(true));
        foreach (var lease in leases) { Assert.True(lease.Release()); Assert.False(lease.Release()); }
        Assert.Equal(0, budget.Live);
    }

    [Fact]
    public void OldestLeavingOwnerIsEvictedAndCannotReleaseTheReplacement()
    {
        var budget = new ClipSlotBudget(2);
        int stopped = 0;
        var first = budget.TryAcquire(() => stopped++)!;
        var second = budget.TryAcquire()!;
        first.Evictable = second.Evictable = true;
        using var replacement = budget.TryAcquire(null, out var evicted, reserved: true);
        Assert.Same(first, evicted);
        Assert.Equal(1, stopped);
        Assert.False(first.Release());
        Assert.Equal(2, budget.Live);
        second.Dispose();
    }

    [Fact]
    public void RemotePoolRejectsOldSelectionAndReusesLeastRecentlyShown()
    {
        var pool = new RemoteFlashPool();
        pool.Select(new[] { "one" });
        int old = pool.Generation;
        Assert.True(pool.Add(old, "a"));
        Assert.True(pool.Add(old, "b"));
        var first = pool.Take(new Random(1), _ => true);
        var second = pool.Take(new Random(1), _ => true);
        Assert.NotEqual(first, second);
        Assert.Equal(first, pool.TakeShown(_ => true));
        Assert.Equal(second, pool.TakeShown(_ => true));
        pool.Select(new[] { "two" });
        Assert.False(pool.Add(old, "stale"));
        Assert.Null(pool.TakeShown(_ => true));
    }

    [Fact]
    public void RemoteSourceRequiresConsentAndFallsBackWhenLocalPoolIsEmpty()
    {
        var s = new AppSettings { MediaSource = "mixed", RemoteMediaRatio = 0 };
        Assert.False(FlashSourceRules.ShouldDrawRemote(s, false, new Random(1)));
        s.RemoteMediaConsented = true;
        Assert.True(FlashSourceRules.ShouldDrawRemote(s, false, new Random(1)));
        Assert.False(FlashSourceRules.ShouldDrawRemote(s, true, new Random(1)));
        s.MediaSource = "local";
        Assert.False(FlashSourceRules.ShouldDrawRemote(s, false, new Random(1)));
    }
    private sealed class Transport : HttpMessageHandler
    {
        internal int Requests;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Interlocked.Increment(ref Requests);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(new byte[] { 1, 2, 3 }) });
        }
    }

    [Fact]
    public async Task WarmCacheUsesUrlIdentityAndPanicClearsItThenAllowsRewarming()
    {
        var settings = RemoteFlashSource.Settings;
        var fetch = RemoteFlashSource.Fetch;
        var http = RemoteFlashSource._http;
        using var transport = new Transport();
        using var client = new HttpClient(transport);
        var entry = new FypAssetManifest.Entry
        {
            Id = "test", Url = "https://example.invalid/clip.mp4?variant=1",
            PosterUrl = "https://example.invalid/poster.jpg", Origin = "online", Type = "video"
        };
        try
        {
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.Settings = () => new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
            RemoteFlashSource.Fetch = _ => Task.FromResult((new List<FypAssetManifest.Entry> { entry }, (string?)null));
            RemoteFlashSource._http = client;
            for (int cycle = 0; cycle < 2; cycle++)
            {
                var timeout = DateTime.UtcNow.AddSeconds(10);
                do
                {
                    RemoteFlashSource.EnsurePrefetch();
                    await Task.Delay(20);
                } while (RemoteFlashSource.ReadyCount == 0 && DateTime.UtcNow < timeout);
                var item = RemoteFlashSource.TryTake(new Random(1));
                Assert.NotNull(item);
                Assert.Equal(entry.Url, item.Value.Url);
                Assert.True(File.Exists(item.Value.ClipPath));
                Assert.Equal(item.Value.ClipPath, RemoteFlashSource.ClipPath(entry.Url));
                Assert.Equal(item, RemoteFlashSource.TryTake(new Random(2)));
                RemoteFlashSource.CleanupCache();
                Assert.Null(RemoteFlashSource.ClipPath(entry.Url));
                Assert.Equal(0, RemoteFlashSource.ReadyCount);
            }
            Assert.Equal(4, transport.Requests); // three batches share one poster + one clip per cycle
        }
        finally
        {
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.Settings = settings;
            RemoteFlashSource.Fetch = fetch;
            RemoteFlashSource._http = http;
        }
    }

}
