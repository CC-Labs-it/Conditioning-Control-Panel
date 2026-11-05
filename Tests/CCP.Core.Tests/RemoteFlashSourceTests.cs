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
    private static async Task WithSource(Func<Task> body, bool clipConsumer = true)
    {
        var (settings, fetch, download, consumer) = (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer);
        try
        {
            RemoteFlashSource.CleanupCache();
            RemoteFlashSource.ClipConsumer = clipConsumer;
            RemoteFlashSource.Settings = () => new AppSettings { MediaSource = "online", RemoteMediaConsented = true };
            RemoteFlashSource.Fetch = _ => Task.FromResult((new List<FypAssetManifest.Entry> { Clip }, (string?)null));
            await body();
        }
        finally
        {
            RemoteFlashSource.CleanupCache();
            (RemoteFlashSource.Settings, RemoteFlashSource.Fetch, RemoteFlashSource.Download, RemoteFlashSource.ClipConsumer) = (settings, fetch, download, consumer);
        }
    }
    [Fact]
    public Task OwnedClipSurvivesDeviceCacheEvictionAndIsReleasedByPanic() => WithSource(async () =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-clip-ownership-").FullName;
        var (root, inline, clock) = (ScrolllerDiskCache.RootProvider, ScrolllerDiskCache.WarmInline, ScrolllerDiskCache.UtcNow);
        try
        {
            var cache = Directory.CreateDirectory(Path.Combine(dir, "cache")).FullName;
            var cached = Path.Combine(cache, ScrolllerDiskCache.FileNameFor(Clip.Url));
            var poster = Path.Combine(dir, "poster.jpg");
            File.WriteAllBytes(cached, new byte[] { 1, 2, 3 });
            File.WriteAllBytes(poster, new byte[] { 4 });
            var born = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
            ScrolllerDiskCache.UtcNow = () => born.AddHours(23);
            var book = new ScrolllerCacheBook { FormatChecked = true };
            book.Put(new ScrolllerCacheEntry(Clip.Url, cached, 3, born, new ScrolllerCacheMeta("clip"), born));
            File.WriteAllText(Path.Combine(cache, ScrolllerDiskCache.IndexFileName), book.ToJson());
            ScrolllerDiskCache.RootProvider = () => cache;
            ScrolllerDiskCache.WarmInline = true;
            Assert.Equal(born, ScrolllerDiskCache.DownloadedUtcOf(Clip.Url));
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
            Assert.Equal(born, File.GetLastWriteTimeUtc(item.ClipPath), TimeSpan.FromSeconds(1));
            Assert.True(ScrolllerDiskCache.CopyDue(item.ClipPath, t => t == born));
            Assert.Equal(1, clips); // repeated URLs keep the same owned file
            File.Delete(cached); // the shared cache sweeps its own copy
            Assert.Equal(new byte[] { 1, 2, 3 }, File.ReadAllBytes(item.ClipPath));
            Assert.Equal(item.ClipPath, RemoteFlashSource.ClipPath(Clip.Url));
            var cts = (CancellationTokenSource)typeof(RemoteFlashSource).GetField("Cts", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            using var registration = cts.Token.Register(() => throw new InvalidOperationException("cancellation probe"));
            Assert.Null(Record.Exception(RemoteFlashSource.CleanupCache));
            Assert.False(File.Exists(item.ClipPath));
            Assert.True(File.Exists(poster)); // never delete device-cache files
        }
        finally { ScrolllerDiskCache.RootProvider = root; ScrolllerDiskCache.WarmInline = inline; ScrolllerDiskCache.UtcNow = clock; Directory.Delete(dir, recursive: true); }
    });
    [Fact]
    public async Task PosterOnlyHeadNeverDownloadsClipsOrDeletesTheDevicePoster()
    {
        Assert.False(RemoteFlashSource.ClipConsumer);
        await WithSource(async () =>
        {
            var dir = Directory.CreateTempSubdirectory("ccp-flash-posters-").FullName;
            try
            {
                var poster = Path.Combine(dir, "poster.jpg");
                File.WriteAllBytes(poster, new byte[] { 4, 5, 6 });
                int downloads = 0;
                RemoteFlashSource.Download = (url, max, _) =>
                {
                    downloads++;
                    Assert.Equal(Clip.PosterUrl, url);
                    Assert.Equal(RemoteFlashSource.PosterMaxBytes, max);
                    return Task.FromResult<string?>(poster);
                };
                RemoteFlashSource.EnsurePrefetch();
                await RemoteFlashSource.Prefetching;
                var item = RemoteFlashSource.TryTake(new Random(1))!.Value;
                Assert.Equal(Clip.Url, item.Url);
                Assert.Equal("", item.ClipPath);
                Assert.Null(RemoteFlashSource.ClipPath(Clip.Url));
                Assert.NotEqual(poster, item.PosterPath);
                Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(item.PosterPath));
                var firstWarm = RemoteFlashSource.Prefetching;
                // Step past the refill throttle without a wall-clock sleep; the first warm has finished.
                typeof(RemoteFlashSource).GetField("_lastFetchUtc", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.SetValue(null, DateTime.MinValue);
                RemoteFlashSource.EnsurePrefetch();
                await RemoteFlashSource.Prefetching;
                Assert.NotSame(firstWarm, RemoteFlashSource.Prefetching);
                Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(item.PosterPath));
                Assert.Equal(item.PosterPath, RemoteFlashSource.TryTake(new Random(2))!.Value.PosterPath);
                Assert.Equal(1, downloads);
                RemoteFlashSource.CleanupCache();
                Assert.False(File.Exists(item.PosterPath));
                Assert.True(File.Exists(poster));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }, clipConsumer: false);
    }
    [Fact]
    public Task FailedClipDownloadSkipsTheEntryAsWpf() => WithSource(async () =>
    {
        // WPF FlashService.cs:3982 `if (path == null) continue;`: no poster-only fallback item.
        var urls = new List<string>();
        RemoteFlashSource.Download = (url, _, _) => { urls.Add(url); return Task.FromResult<string?>(null); };
        RemoteFlashSource.EnsurePrefetch();
        await RemoteFlashSource.Prefetching;
        Assert.Contains(Clip.Url, urls);
        Assert.DoesNotContain(Clip.PosterUrl, urls);
        Assert.Null(RemoteFlashSource.TryTake(new Random(1)));
        Assert.Null(RemoteFlashSource.ClipPath(Clip.Url));
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
    public void ClipTrimmingUsesLastUseRatherThanInsertionOrder()
    {
        var order = new RemoteClipFiles();
        for (int i = 0; i < 64; i++) order.Put("u" + i, "p" + i, "poster", i);
        Assert.True(order.TryUse("u0", 1000, out _, out _));
        Assert.Equal(new[] { "p1" }, order.Put("u64", "p64", "poster", 200_000));
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
