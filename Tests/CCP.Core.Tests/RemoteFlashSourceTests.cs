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
public sealed class RemoteFlashSourceTests
{
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
    public Task OwnedClipSurvivesDeviceCacheEvictionAndIsReleasedByPanic() => WithSource(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-clip-ownership-").FullName;
        try
        {
            var cached = Path.Combine(dir, "clip.mp4");
            var poster = Path.Combine(dir, "poster.jpg");
            File.WriteAllBytes(cached, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(poster, new byte[] { 4 });
            int clips = 0;
            RemoteFlashSource.Download = (url, _, _) =>
            {
                if (url == Clip.Url) clips++;
                return Task.FromResult<string?>(url == Clip.Url ? cached : poster);
            };
            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            var item = RemoteFlashSource.TryTake(new Random(1))!.Value;
            Assert.NotEqual(cached, item.ClipPath);
            Assert.Equal(1, clips); // repeated URLs keep the same owned file
            File.Delete(cached); // the shared cache sweeps its own copy
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(item.ClipPath));
            Assert.Equal(item.ClipPath, RemoteFlashSource.ClipPath(Clip.Url));
            RemoteFlashSource.CleanupCache();
            Assert.False(File.Exists(item.ClipPath));
            Assert.True(File.Exists(poster)); // never delete device-cache files
        }
        finally { Directory.Delete(dir, recursive: true); }
    });
    [Theory]
    [InlineData(0)] // poster failed
    [InlineData(1)] // poster threw cancellation
    [InlineData(2)] // downloader ignored cancellation and returned a stale result
    public Task CancelledOrFailedPosterReleasesUnpublishedClip(int mode) => WithSource(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-clip-cancel-").FullName;
        try
        {
            var ext = ".cancel" + mode + Guid.NewGuid().ToString("N"); // unique, so parallel tests' temp copies never count
            var cached = Path.Combine(dir, "clip" + ext);
            File.WriteAllBytes(cached, new byte[] { 1 });
            bool sawClip = false;
            RemoteFlashSource.Download = (url, _, ct) =>
            {
                if (url == Clip.Url) { sawClip = true; return Task.FromResult<string?>(cached); }
                if (mode > 0)
                {
                    RemoteFlashSource.CleanupCache();
                    if (mode == 1) ct.ThrowIfCancellationRequested();
                    return Task.FromResult<string?>(cached);
                }
                return Task.FromResult<string?>(null);
            };
            RemoteFlashSource.EnsurePrefetch();
            await RemoteFlashSource.Prefetching;
            Assert.True(sawClip);
            Assert.Equal(0, RemoteFlashSource.ReadyCount);
            var owned = ConditioningControlPanel.CorePaths.MediaTemp();
            Assert.Empty(Directory.Exists(owned) ? Directory.GetFiles(owned, ScrolllerDiskCache.TempPrefix + "*" + ext) : Array.Empty<string>());
            Assert.True(File.Exists(cached));
        }
        finally { Directory.Delete(dir, recursive: true); }
    });
    [Fact]
    public void ClipFilesDrawnJustNowSurviveManyMoreWarms()
    {
        var files = new RemoteClipFiles();
        long now = 0;
        var released = new HashSet<string>();
        for (int i = 0; i < 64; i++) foreach (var p in files.Put("u" + i, "p" + i, "poster", now)) released.Add(p);
        Assert.Empty(released);
        // A draw picks the oldest clip, then the next prefetch warms 40 more (the old sweep
        // deleted exactly that file before LibVLC opened it).
        now = 200_000;
        Assert.True(files.TryUse("u0", now, out var drawn, out var poster));
        Assert.Equal("p0", drawn);
        Assert.Equal("poster", poster);
        for (int i = 64; i < 104; i++) foreach (var p in files.Put("u" + i, "p" + i, "poster", now)) released.Add(p);
        Assert.DoesNotContain("p0", released);
        Assert.True(files.TryUse("u0", now, out _, out _));
        Assert.Equal(RemoteClipFiles.SoftCap, files.Count);
        Assert.Contains("p1", released);      // the unused old ones go first
        var order = new RemoteClipFiles();
        for (int i = 0; i < 64; i++) order.Put("u" + i, "p" + i, "poster", i);
        Assert.True(order.TryUse("u0", 1000, out _, out _));
        Assert.Equal(new[] { "p1" }, order.Put("u64", "p64", "poster", 200_000));
    }
    [Fact]
    public void ClipFilesReplaceReleasesTheOldFileAndHardCapHolds()
    {
        var files = new RemoteClipFiles();
        Assert.Empty(files.Put("u", "a", "poster", 0));
        Assert.Equal(new[] { "a" }, files.Put("u", "b", "poster", 0));
        Assert.Empty(files.Put("u", "b", "poster", 0));
        // Everything recent: the set grows past the soft cap but never past the hard cap.
        var dropped = new List<string>();
        for (int i = 0; i < 300; i++) dropped.AddRange(files.Put("r" + i, "rp" + i, "poster", 1000));
        Assert.Equal(RemoteClipFiles.HardCap, files.Count);
        Assert.Equal(301 - RemoteClipFiles.HardCap, dropped.Count);
        Assert.Equal(RemoteClipFiles.HardCap, files.TakeAll().Count);
        Assert.Equal(0, files.Count);
    }
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
