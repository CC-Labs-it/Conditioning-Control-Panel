using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>PackMediaPool: the content-pack half of the flash, video and bubble pools (WPF FlashService
/// PackBag/_tempPackFiles, VideoService _packVideoQueue, BubbleCountService _packVideos).</summary>
public sealed class PackMediaPoolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packpool-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();

    public PackMediaPoolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private ContentPackStore NewStore() =>
        new(() => _root, () => _settings, () => { }, tempDir: () => Path.Combine(_root, ".temp"));

    private static string Plain(string path) => System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(path));

    [Fact]
    public void Weighting_FollowsWpf()
    {
        var rng = new Random(3);
        Assert.False(PackMediaPool.ShouldDrawPack(5, 0, rng));
        Assert.True(PackMediaPool.ShouldDrawPack(0, 1, rng));
        var packWins = Enumerable.Range(0, 4000).Count(_ => PackMediaPool.ShouldDrawPack(30, 10, rng));
        Assert.InRange(packWins, 800, 1200);   // 10 of 40 -> ~25 %
    }

    [Fact]
    public void PacksOnly_DealsEveryImageOnceBeforeARepeat_AsDecrypts()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"), ("b.png", "image"), ("c.png", "image"), ("v.mp4", "video"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.ImageType, new Random(1), () => store);

        var dealt = Enumerable.Range(0, 3).Select(_ => pool.TryDraw(0)!).ToList();

        Assert.Equal(new[] { "p1/a.png", "p1/b.png", "p1/c.png" }, dealt.Select(Plain).OrderBy(x => x));
        Assert.Equal(3, pool.TempCount);
        pool.CleanupTemps();
        Assert.All(dealt, p => Assert.False(File.Exists(p)));
        Assert.Equal(3, store.GetAllActivePackImages().Count);   // only decrypts go, never the pack
    }

    [Fact]
    public void DeactivatedPack_LeavesThePool_OnTheNextDraw()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.ImageType, new Random(1), () => store);
        Assert.NotNull(pool.TryDraw(0));

        store.DeactivatePack("p1");

        Assert.Null(pool.TryDraw(0));
        Assert.Null(new PackMediaPool(ContentPackStore.ImageType, store: () => null).TryDraw(0));
    }

    [Fact]
    public void TempsPastTheCap_OldestHalfDeleted()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.ImageType, new Random(1), () => store);
        var temps = Enumerable.Range(0, PackMediaPool.TempCap + 2).Select(_ => pool.TryDraw(0)!).ToList();

        Assert.True(pool.TempCount <= PackMediaPool.TempCap);
        Assert.False(File.Exists(temps[0]));
        Assert.True(File.Exists(temps[^1]));
        pool.CleanupTemps();
    }

    [Fact]
    public void VideoAndBubbleSchedulers_PlayPackClips_WhenTheLibraryIsEmpty()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("v.mp4", "video"), ("a.png", "image"));
        var store = NewStore();
        var video = new MandatoryVideoScheduler(null!, null, () => Array.Empty<string>())
            { PackVideos = new PackMediaPool(ContentPackStore.VideoType, new Random(1), () => store) };
        var bubble = new BubbleCountScheduler(null!, null, () => Array.Empty<string>())
            { PackVideos = new PackMediaPool(ContentPackStore.VideoType, new Random(1), () => store) };

        Assert.Equal("p1/v.mp4", Plain(video.PickNext()!));
        Assert.Equal("p1/v.mp4", Plain(bubble.PickNext()!));
        video.PackVideos.CleanupTemps();
        bubble.PackVideos.CleanupTemps();

        store.DeactivatePack("p1");
        Assert.Null(video.PickNext());
        Assert.Null(bubble.PickNext());
    }
}
