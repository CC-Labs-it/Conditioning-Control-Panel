using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
[Collection(SessionStatics.Name)]
public sealed class PackTempRetryTests
{
    private sealed class PackFixture : IDisposable
    {
        public readonly string Root = Directory.CreateTempSubdirectory("ccp-pack-").FullName;
        public readonly ContentPackStore Store;
        public readonly PackMediaPool Pool;
        public PackFixture(string file = "v.mp4", string type = ContentPackStore.VideoType)
        {
            ContentPackStoreTests.WritePack(Root, "p1", "One", (file, type));
            var settings = new AppSettings();
            Store = new ContentPackStore(() => Root, () => settings, () => { }, tempDir: () => Path.Combine(Root, ".temp"));
            Pool = new PackMediaPool(type, store: () => Store);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
    [Fact]
    public void Refused_delete_keeps_pool_entry_and_identity_until_retry()
    {
        using var fixture = new PackFixture("a.png", ContentPackStore.ImageType);
        var (store, pool) = (fixture.Store, fixture.Pool);
        var path = pool.TryDraw(0)!;
        File.Delete(path);
        Directory.CreateDirectory(path); // Portable delete refusal, even when running as root.
        pool.CleanupTemps();
        Assert.Equal(1, pool.TempCount);
        Assert.True(store.TryGetSourceKey(path, out _));
        Directory.Delete(path);
        File.WriteAllText(path, "released");
        pool.CleanupTemps();
        Assert.Equal(0, pool.TempCount);
        Assert.False(File.Exists(path));
        Assert.False(store.TryGetSourceKey(path, out _));
        var missing = pool.TryDraw(0)!; Assert.True(store.TryGetSourceKey(missing, out _));
        Directory.Delete(Path.GetDirectoryName(missing)!, true);
        Assert.True(ConditioningControlPanel.Services.MediaFiles.MediaTempFileLedger.TryDeleteFile(missing));
        pool.CleanupTemps();
        Assert.Equal(0, pool.TempCount);
        Assert.False(store.TryGetSourceKey(missing, out _));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scheduler_reload_and_stop_cleanup_decrypts_and_retry_refusals(bool bubble)
    {
        using var fixture = new PackFixture();
        var (store, pool) = (fixture.Store, fixture.Pool);
        var video = new MandatoryVideoScheduler(null!) { PackVideos = pool };
        var game = new BubbleCountScheduler(null!) { PackVideos = pool };
        var live = pool.TryDraw(0)!;
        var refused = pool.TryDraw(0)!;
        File.Delete(refused);
        Directory.CreateDirectory(refused);
        if (bubble) game.ReloadAssets(); else video.ReloadAssets();
        Assert.False(File.Exists(live));
        Assert.Equal(1, pool.TempCount);
        Assert.True(store.TryGetSourceKey(refused, out _));
        Directory.Delete(refused);
        File.WriteAllText(refused, "released");
        if (bubble) game.Stop(); else video.Stop();
        Assert.False(File.Exists(refused));
        Assert.Equal(0, pool.TempCount);
        Assert.False(store.TryGetSourceKey(refused, out _));
    }
    private sealed class ClosingHost : IMandatoryVideoHost, IBubbleCountHost
    {
        public Action? Closed;
        public string? Shown;
        public bool ExistedAtShow;
        public Action<bool>? Done;
        public void Show(string path, bool strict) { Shown = path; ExistedAtShow = File.Exists(path); }
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { Shown = path; Done = onComplete; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
        public void ShowMessage(string text, int ms, Action then) { }
        double IMandatoryVideoHost.CloseAll() { Closed?.Invoke(); return 0; }
        void IBubbleCountHost.CloseAll() => Closed?.Invoke();
        public double LastVideoDurationSeconds => 0;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Closing_playback_retries_after_host_releases_the_file(bool bubble)
    {
        using var fixture = new PackFixture();
        var (store, pool) = (fixture.Store, fixture.Pool);
        var path = pool.TryDraw(0)!;
        File.Delete(path);
        Directory.CreateDirectory(path);
        var host = new ClosingHost { Closed = () => { Directory.Delete(path); File.WriteAllText(path, "released"); } };
        var clock = new PopQuizSchedulerTests.FakeClock();
        var video = new MandatoryVideoScheduler(host, clock) { PackVideos = pool };
        var game = new BubbleCountScheduler(host, clock) { PackVideos = pool };
        if (bubble) { game.Trigger(forceTest: true); game.ReloadAssets(); game.ForceCleanup(); }
        else { Assert.True(video.Trigger(strictOverride: false, path: path)); video.ReloadAssets(); video.End(); }
        Assert.False(File.Exists(path));
        Assert.Equal(0, pool.TempCount);
        Assert.False(store.TryGetSourceKey(path, out _));
    }
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Selected_pack_video_survives_reload_or_stop(bool bubble, bool stop, bool force)
    {
        using var fixture = new PackFixture();
        var pool = fixture.Pool;
        var old = pool.TryDraw(0)!;
        var host = new ClosingHost();
        var clock = new PopQuizSchedulerTests.FakeClock();
        var video = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>()) { PackVideos = pool };
        var game = new BubbleCountScheduler(host, clock, () => Array.Empty<string>()) { PackVideos = pool };
        if (bubble) { game.Trigger(forceTest: true); clock.Advance(BubbleCountScheduler.LeadIn); }
        else Assert.True(video.Trigger(strictOverride: false));
        var selected = Assert.Single(Directory.GetFiles(Path.Combine(fixture.Root, ".temp")), file => file != old);
        if (bubble && stop) game.Stop(); else if (bubble) game.ReloadAssets(); else video.ReloadAssets();
        Assert.False(File.Exists(old));
        Assert.Equal("p1/v.mp4", File.ReadAllText(selected)); // Simulate a host opening the file later.
        if (bubble)
        {
            Assert.Equal(selected, host.Shown);
            if (force) game.ForceCleanup(); else host.Done!(true);
        }
        else if (stop) video.Stop();
        else
        {
            clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal(selected, host.Shown);
            Assert.True(host.ExistedAtShow);
            video.End();
        }
        Assert.False(File.Exists(selected));
        Assert.Equal(0, pool.TempCount);
    }
}
