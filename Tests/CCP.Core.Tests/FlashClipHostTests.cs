using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
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
    private static readonly FypAssetManifest.Entry Clip = new()
    {
        Id = "test", Url = "https://example.invalid/clip.mp4?variant=1",
        PosterUrl = "https://example.invalid/poster.jpg", Origin = "online", Type = "video"
    };

    /// <summary>Settings/fetch/download seams restored and the pool emptied after every test (P02).</summary>
    private static async Task WithSource(Func<Task> body)
    {
        var (settings, fetch, download) = (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download);
        try
        {
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.Settings = () => new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
            RemoteFlashSource.Fetch = _ => Task.FromResult((new List<FypAssetManifest.Entry> { Clip }, (string?)null));
            await body();
        }
        finally
        {
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download) = (settings, fetch, download);
        }
    }

    [Fact]
    public Task WarmCacheUsesUrlIdentityAndPanicClearsItThenAllowsRewarming() => WithSource(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-flash-clip-").FullName;
        var asked = new List<(string Url, long Max)>();
        try
        {
            RemoteFlashSource.Download = (url, max, _) =>
            {
                lock (asked) asked.Add((url, max));
                var path = Path.Combine(dir, url.EndsWith(".jpg") ? "poster.jpg" : "clip.mp4");
                File.WriteAllBytes(path, new byte[] { 1, 2, 3 });
                return Task.FromResult<string?>(path);
            };
            for (int cycle = 0; cycle < 2; cycle++)
            {
                RemoteFlashSource.EnsurePrefetch();
                await RemoteFlashSource.Prefetching;
                var item = RemoteFlashSource.TryTake(new Random(1));
                Assert.NotNull(item);
                Assert.Equal(Clip.Url, item.Value.Url);
                Assert.True(File.Exists(item.Value.ClipPath));
                Assert.Equal(item.Value.ClipPath, RemoteFlashSource.ClipPath(Clip.Url));
                Assert.Equal(item, RemoteFlashSource.TryTake(new Random(2)));
                RemoteFlashSource.CleanupCache();
                Assert.Null(RemoteFlashSource.ClipPath(Clip.Url));
                Assert.Equal(0, RemoteFlashSource.ReadyCount);
            }
            Assert.Contains((Clip.Url, (long)RemoteFlashSource.ClipMaxBytes), asked);
            Assert.Contains((Clip.PosterUrl!, (long)RemoteFlashSource.PosterMaxBytes), asked);
        }
        finally { Directory.Delete(dir, recursive: true); }
    });

    /// <summary>The licence path: downloads go through ScrolllerDiskCache.GetOrFetchAsync, which asks
    /// consent (then MayFetchFresh) before any byte; refused, nothing warms.</summary>
    [Fact]
    public Task DownloadsGoThroughTheScrolllerDeviceCacheGate() => WithSource(async () =>
    {
        var consent = ScrolllerDiskCache.RemoteConsent;
        int asked = 0;
        try
        {
            ScrolllerDiskCache.RemoteConsent = () => { Interlocked.Increment(ref asked); return false; };
            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            Assert.True(asked > 0);
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
        }
        finally { ScrolllerDiskCache.RemoteConsent = consent; }
    });
}
