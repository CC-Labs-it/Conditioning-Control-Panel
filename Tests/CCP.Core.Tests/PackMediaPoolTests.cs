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
[Collection(SessionStatics.Name)]
public sealed class PackMediaPoolTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ccp-packpool-" + Guid.NewGuid().ToString("N"));
    private readonly AppSettings _settings = new();

    public PackMediaPoolTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { }
    }

    private ContentPackStore NewStore(Action<string>? delete = null) =>
        new(() => _root, () => _settings, () => { }, tempDir: () => Path.Combine(_root, ".temp"), deleteTempFile: delete);

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

    // WPF VideoService.cs:1568-1574 and BubbleCountService.cs:986-990.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReloadingSchedulersResetsPackCyclesAndDeletesDecrypts(bool useVideo)
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"), ("b.mp4", "video"), ("c.mp4", "video"));
        var store = NewStore();
        var rng = new CountingRandom();
        var pack = new PackMediaPool(ContentPackStore.VideoType, rng, () => store);
        var video = new MandatoryVideoScheduler(null!, library: () => Array.Empty<string>()) { PackVideos = pack };
        var bubble = new BubbleCountScheduler(null!, library: () => Array.Empty<string>()) { PackVideos = pack };
        var temp = (useVideo ? video.PickNext() : bubble.PickNext())!;
        Assert.True(File.Exists(temp));
        if (useVideo) video.ReloadAssets(); else bubble.ReloadAssets();
        // The next roll weighs all three pack clips, not the two left in the old cycle.
        pack.TryDraw(20, remaining: true);
        Assert.Equal(23, rng.LastMaximum);
        Assert.False(File.Exists(temp));
        Assert.Equal(0, pack.TempCount);
        Assert.Equal(3, store.GetAllActivePackVideos().Count);
    }

    // WPF VideoService.cs:8553-8556: a file still in use stays tracked for the next cleanup.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void RefusedPackTempDeletionIsRetried(bool checkIdentity)
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        bool refuse = true;
        var store = NewStore(p => { if (!OperatingSystem.IsWindows() && refuse) throw new IOException("held"); File.Delete(p); });
        var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
        var temp = pool.TryDraw(0)!;
        // Windows exercises a real open file; other platforms simulate the same refused delete.
        using (OperatingSystem.IsWindows() ? File.Open(temp, FileMode.Open, FileAccess.Read, FileShare.Read) : null)
        {
            pool.Reset();
            Assert.True(File.Exists(temp));
            if (checkIdentity) Assert.True(store.TryGetSourceKey(temp, out _));
            else Assert.Equal(1, pool.TempCount);
        }
        refuse = false;
        pool.Reset();
        Assert.False(File.Exists(temp));
        Assert.Equal(0, pool.TempCount);
        Assert.False(store.TryGetSourceKey(temp, out _));
    }

    [Fact]
    public void CapCleanupRetainsARefusedFileForTheNextReset()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        string? blocked = null; bool refuse = true;
        var store = new ContentPackStore(() => _root, () => _settings, () => { },
            decryptFile: _ => new byte[] { 1 }, tempDir: () => Path.Combine(_root, ".temp"),
            deleteTempFile: p => { if (refuse && p == blocked) throw new IOException("held"); File.Delete(p); });
        var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
        blocked = pool.TryDraw(0)!;
        for (int i = 0; i <= PackMediaPool.TempCap; i++) Assert.NotNull(pool.TryDraw(0));
        Assert.True(File.Exists(blocked));
        refuse = false;
        pool.Reset();
        Assert.False(File.Exists(blocked));
        Assert.False(store.TryGetSourceKey(blocked, out _));
        Assert.Equal(0, pool.TempCount);
    }

    [Fact]
    public void CapCleanupDefersAPlayingClipUntilRelease()
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        var store = new ContentPackStore(() => _root, () => _settings, () => { },
            decryptFile: _ => new byte[] { 1 }, tempDir: () => Path.Combine(_root, ".temp"));
        var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
        var playing = pool.TryDraw(0, retain: true)!;
        for (int i = 0; i <= PackMediaPool.TempCap; i++) Assert.NotNull(pool.TryDraw(0));
        Assert.True(File.Exists(playing));
        pool.Release(playing);
        Assert.False(File.Exists(playing));
        pool.CleanupTemps();
    }

    // WPF VideoService.cs:3090,7035-7051: the saved clip is needed again for output recovery.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadKeepsPendingAndPlayingVideoUntilEndOrPanic(bool cancelPending)
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        var store = NewStore(); var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
        var clock = new PopQuizSchedulerTests.FakeClock(); var host = new VideoHost();
        var video = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>()) { PackVideos = pool };
        Assert.True(video.Trigger(strictOverride: false));
        var path = Assert.Single(Directory.GetFiles(Path.Combine(_root, ".temp")));
        video.ReloadAssets();
        Assert.True(File.Exists(path)); // the pre-roll has not opened the clip yet
        if (!cancelPending)
        {
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(path, host.Path);
            video.ReloadAssets();
            Assert.Equal("p1/a.mp4", File.ReadAllText(host.Path!)); // recovery reopens this same path
            video.End();
        }
        else video.ForceCleanup();
        Assert.False(File.Exists(path));
        Assert.Equal(0, pool.TempCount);
    }

    // WPF BubbleCountService.cs:465-466,486: the chosen path stays live until the game closes.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReloadKeepsBubbleVideoUntilCompletionOrPanic(bool panic)
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        var store = NewStore(); var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
        var clock = new PopQuizSchedulerTests.FakeClock(); var host = new BubbleHost();
        var game = new BubbleCountScheduler(host, clock, () => Array.Empty<string>()) { PackVideos = pool };
        var s = ConditioningControlPanel.CoreSettings.Current; var strict = s.BubbleCountStrictLock;
        try
        {
            s.BubbleCountStrictLock = false;
            host.OnShow = () => game.ReloadAssets(); // selection is live even while Show is starting
            game.Trigger(forceTest: true); clock.Advance(BubbleCountScheduler.LeadIn);
            var path = Assert.IsType<string>(host.Path);
            Assert.Equal("p1/a.mp4", File.ReadAllText(path));
            game.ReloadAssets();
            Assert.True(File.Exists(path));
            if (panic) game.ForceCleanup(); else host.Done!(false);
            Assert.False(File.Exists(path));
            Assert.Equal(0, pool.TempCount);
        }
        finally { game.ForceCleanup(); s.BubbleCountStrictLock = strict; }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public void FailedCloseKeepsTheClipUntilThePlayerCloses(bool useVideo, bool retryPanic)
    {
        ContentPackStoreTests.WritePack(_root, "p1", "One", ("a.mp4", "video"));
        var store = NewStore(); var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
        var clock = new PopQuizSchedulerTests.FakeClock();
        var vh = new VideoHost { ThrowClose = true }; var bh = new BubbleHost { ThrowClose = true };
        var video = new MandatoryVideoScheduler(vh, clock, () => Array.Empty<string>()) { PackVideos = pool };
        var bubble = new BubbleCountScheduler(bh, clock, () => Array.Empty<string>()) { PackVideos = pool };
        string? path = null;
        try
        {
            if (useVideo) Assert.True(video.Trigger());
            else { bubble.Trigger(forceTest: true); clock.Advance(BubbleCountScheduler.LeadIn); }
            path = Assert.Single(Directory.GetFiles(Path.Combine(_root, ".temp")));
            if (useVideo) { video.ReloadAssets(); video.ForceCleanup(); }
            else { bubble.ReloadAssets(); bubble.ForceCleanup(); }
            Assert.True(File.Exists(path)); // a throwing close did not release the player
            vh.ThrowClose = false; bh.ThrowClose = false;
            if (useVideo) { if (!retryPanic) Assert.True(video.Trigger()); video.ForceCleanup(); }
            else { bubble.Trigger(forceTest: true); bubble.ForceCleanup(); }
            Assert.False(File.Exists(path));
        }
        finally { vh.ThrowClose = false; bh.ThrowClose = false; video.ForceCleanup(); bubble.ForceCleanup(); pool.Release(path); pool.CleanupTemps(); }
    }

    private sealed class VideoHost : IMandatoryVideoHost
    {
        public string? Path; public bool ThrowClose;
        public void Show(string path, bool strict) => Path = path;
        public double CloseAll() => ThrowClose ? throw new InvalidOperationException("close failed") : 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private sealed class BubbleHost : IBubbleCountHost
    {
        public string? Path; public Action? OnShow; public Action<bool>? Done; public bool ThrowClose;
        public double LastVideoDurationSeconds => 0;
        public void Show(string path, int difficulty, bool strict, Action<bool> done) { Path = path; Done = done; OnShow?.Invoke(); }
        public void ShowMessage(string text, int ms, Action then) { }
        public void CloseAll() { if (ThrowClose) throw new InvalidOperationException("close failed"); }
    }

    private sealed class CountingRandom : Random
    {
        public int LastMaximum { get; private set; }
        public override int Next(int maxValue) { LastMaximum = maxValue; return 0; }
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
