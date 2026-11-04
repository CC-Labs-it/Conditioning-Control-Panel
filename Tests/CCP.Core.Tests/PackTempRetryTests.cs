using System;
using System.IO;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
namespace CCP.Core.Tests;
[Collection(SessionStatics.Name)]
public sealed class PackTempRetryTests
{
    [Fact]
    public void Refused_delete_keeps_pool_entry_and_identity_until_retry()
    {
        var root = Directory.CreateTempSubdirectory("ccp-retry-").FullName;
        try
        {
            ContentPackStoreTests.WritePack(root, "p1", "One", ("a.png", "image"));
            var settings = new AppSettings();
            var store = new ContentPackStore(() => root, () => settings, () => { }, tempDir: () => Path.Combine(root, ".temp"));
            var pool = new PackMediaPool(ContentPackStore.ImageType, store: () => store);
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
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Scheduler_reload_and_stop_cleanup_decrypts_and_retry_refusals(bool bubble)
    {
        var root = Directory.CreateTempSubdirectory("ccp-lifecycle-").FullName;
        try
        {
            ContentPackStoreTests.WritePack(root, "p1", "One", ("v.mp4", "video"));
            var settings = new AppSettings();
            var store = new ContentPackStore(() => root, () => settings, () => { }, tempDir: () => Path.Combine(root, ".temp"));
            var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
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
        finally { Directory.Delete(root, true); }
    }
    private sealed class ClosingHost : IMandatoryVideoHost, IBubbleCountHost
    {
        public Action? Closed;
        public string? Shown;
        public bool ExistedAtShow;
        public void Show(string path, bool strict) { Shown = path; ExistedAtShow = File.Exists(path); }
        public void Show(string path, int difficulty, bool strict, Action<bool> onComplete) { }
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
        var root = Directory.CreateTempSubdirectory("ccp-close-").FullName;
        try
        {
            ContentPackStoreTests.WritePack(root, "p1", "One", ("v.mp4", "video"));
            var settings = new AppSettings();
            var store = new ContentPackStore(() => root, () => settings, () => { }, tempDir: () => Path.Combine(root, ".temp"));
            var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
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
        finally { Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reload_during_preroll_preserves_selected_pack_video_until_finish(bool stop)
    {
        var root = Directory.CreateTempSubdirectory("ccp-preroll-").FullName;
        try
        {
            ContentPackStoreTests.WritePack(root, "p1", "One", ("v.mp4", "video"));
            var settings = new AppSettings();
            var store = new ContentPackStore(() => root, () => settings, () => { }, tempDir: () => Path.Combine(root, ".temp"));
            var pool = new PackMediaPool(ContentPackStore.VideoType, store: () => store);
            var old = pool.TryDraw(0)!;
            var host = new ClosingHost();
            var clock = new PopQuizSchedulerTests.FakeClock();
            var video = new MandatoryVideoScheduler(host, clock, () => Array.Empty<string>()) { PackVideos = pool };
            Assert.True(video.Trigger(strictOverride: false));
            var files = Directory.GetFiles(Path.Combine(root, ".temp"));
            var selected = Assert.Single(files, file => file != old);
            video.ReloadAssets();
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(selected));
            if (stop) video.Stop();
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
        finally { Directory.Delete(root, true); }
    }

}
