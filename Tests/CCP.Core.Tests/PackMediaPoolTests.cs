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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RefusedDeleteKeepsIdentityAndPoolEntryUntilRetry(bool overCap)
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"));
        var store = NewStore();
        var pool = new PackMediaPool(ContentPackStore.ImageType, store: () => store);
        var path = pool.TryDraw(0)!;
        // A directory at the minted path forces a portable delete refusal, even as root.
        File.Delete(path);
        Directory.CreateDirectory(path);
        if (overCap)
            for (int i = 0; i <= PackMediaPool.TempCap; i++) pool.TryDraw(0);
        else pool.CleanupTemps();
        Assert.True(store.TryGetSourceKey(path, out _));
        Assert.True(pool.TempCount > 0);
        Directory.Delete(path);
        File.WriteAllText(path, "released");
        pool.CleanupTemps();
        Assert.False(File.Exists(path));
        Assert.False(store.TryGetSourceKey(path, out _));
        Assert.Equal(0, pool.TempCount);
    }

    [Fact]
    public void StoreShutdownRetriesARefusedDelete()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.png", "image"));
        var store = NewStore();
        var path = new PackMediaPool(ContentPackStore.ImageType, store: () => store).TryDraw(0)!;
        File.Delete(path);
        Directory.CreateDirectory(path);
        store.CleanupTempFiles();
        Assert.True(store.TryGetSourceKey(path, out _));
        Directory.Delete(path);
        File.WriteAllText(path, "released");
        store.CleanupTempFiles();
        Assert.False(File.Exists(path));
        Assert.False(store.TryGetSourceKey(path, out _));
    }

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

    [Fact]
    public void QueueCallers_WeighThePackClipsLeftThisCycle()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", Enumerable.Range(0, 10).Select(i => ($"{i}.png", "image")).ToArray());
        // Real manifests, stubbed file decrypt: PBKDF2 per decrypt would make 4000 draws take minutes.
        var store = new ContentPackStore(() => _root, () => _settings, () => { }, decryptFile: _ => new byte[] { 1 },
            tempDir: () => Path.Combine(_root, ".temp"));
        int Hits(bool remaining)
        {
            var hits = 0;
            for (var n = 0; n < 200; n++)
            {
                var pool = new PackMediaPool(ContentPackStore.ImageType, new Random(n), () => store);
                for (var k = 0; k < 9; k++) pool.TryDraw(0);   // one pack clip left this cycle
                if (pool.TryDraw(99, remaining) != null) hits++;
                pool.CleanupTemps();
            }
            return hits;
        }
        Assert.InRange(Hits(remaining: true), 0, 8);      // WPF VideoService.cs:8017: 1 of 100 -> ~1 %
        Assert.InRange(Hits(remaining: false), 9, 40);    // flash weighs whole lists: 10 of 109 -> ~9 %
    }

    [Fact]
    public void StartupSweep_DeletesOnlyStaleDecrypts()
    {
        var temp = Path.Combine(_root, ".temp");
        Directory.CreateDirectory(temp);
        File.WriteAllText(Path.Combine(temp, "ccp_temp_old.png"), "x");
        File.WriteAllText(Path.Combine(temp, "keep.png"), "x");

        Assert.Equal(1, ContentPackStore.SweepStaleTemps(temp, null, Path.Combine(_root, "missing")));

        Assert.False(File.Exists(Path.Combine(temp, "ccp_temp_old.png")));
        Assert.True(File.Exists(Path.Combine(temp, "keep.png")));
    }
}
