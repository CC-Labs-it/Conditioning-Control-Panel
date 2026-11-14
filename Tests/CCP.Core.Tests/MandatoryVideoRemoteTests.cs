using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;
using FakeClock = CCP.Core.Tests.PopQuizSchedulerTests.FakeClock;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class MandatoryVideoRemoteTests
{
    private const string Url = "https://v.scrolller.com/clip.mp4";
    private static FypAssetManifest.Entry Clip(string url = Url) => new() { Id = "scrolller/test/1", Url = url, Type = "video" };
    private sealed class Host : IMandatoryVideoHost
    {
        public string? Path;
        public void Show(string path, bool strict) => Path = path;
        public double CloseAll() => 0;
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    [Fact]
    public async Task Consented_online_clip_is_cached_then_reaches_the_video_host_and_local_takes_effect()
    {
        var s = CoreSettings.Current;
        var old = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.FypOnlineNiches, s.FypOnlineCustomSubs);
        try
        {
            s.MediaSource = "online"; s.RemoteMediaConsented = false; s.FypOnlineConsented = false;
            s.FypOnlineNiches = new() { "hypno" }; s.FypOnlineCustomSubs = new();
            var cached = false; var calls = 0; var downloads = 0;
            var clock = new FakeClock(); var host = new Host();
            var v = new MandatoryVideoScheduler(host, clock, () => new[] { "/local.mp4" })
            {
                RemoteMedia = true,
                FetchRemote = _ => { calls++; return Task.FromResult((new List<FypAssetManifest.Entry> { Clip(), Clip("https://v.scrolller.com/still.jpg") }, (string?)null)); },
                CachedScrolllerPath = _ => cached ? "/cache.mp4" : null,
                MayFetchRemote = _ => true,
                DownloadRemote = (_, _) => { downloads++; cached = true; return Task.FromResult<string?>("/cache.mp4"); },
            };
            await v.RefillRemoteForTest();
            Assert.Equal(0, calls); // no consent means no network
            s.FypOnlineConsented = true; // prior feed consent also counts
            await v.RefillRemoteForTest();
            Assert.Equal(1, calls); Assert.Equal(1, downloads); // the still was rejected
            Assert.True(v.Trigger()); clock.Advance(MandatoryVideoScheduler.PreRoll);
            Assert.Equal("/cache.mp4", host.Path); // the host plays the cached file, never the URL
            Assert.Equal(Url, v.ShowingPath); // and the source URL identity survives beside it
            Assert.Equal(Url, v.LastVideoPath);
            v.ForceCleanup();
            s.MediaSource = "local";
            Assert.Equal("/local.mp4", v.PickNext());
            if (v.RemoteTask != null) await v.RemoteTask;
        }
        finally { (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.FypOnlineNiches, s.FypOnlineCustomSubs) = old; }
    }

    [Fact]
    public async Task Uncached_or_blocked_clip_falls_back_to_local_without_streaming()
    {
        var s = CoreSettings.Current; var old = (s.MediaSource, s.RemoteMediaConsented);
        try
        {
            s.RemoteMediaConsented = true; s.MediaSource = "online";
            var downloads = 0; var host = new Host();
            var v = new MandatoryVideoScheduler(host, library: () => new[] { "/local.mp4" })
            {
                RemoteMedia = true,
                FetchRemote = _ => Task.FromResult((new List<FypAssetManifest.Entry> { Clip() }, (string?)null)),
                CachedScrolllerPath = _ => null, MayFetchRemote = _ => false,
                DownloadRemote = (_, _) => { downloads++; return Task.FromResult<string?>(null); },
            };
            await v.RefillRemoteForTest();
            Assert.Equal(0, downloads);
            Assert.Equal("/local.mp4", v.PickNext());
            if (v.RemoteTask != null) await v.RemoteTask;
        }
        finally { (s.MediaSource, s.RemoteMediaConsented) = old; }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task QueuedClipDownloadsWhileTheNextFetchFails(bool throws)
    {
        var s = CoreSettings.Current; var old = (s.MediaSource, s.RemoteMediaConsented);
        var pending = new TaskCompletionSource<(List<FypAssetManifest.Entry>, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            s.MediaSource = "online"; s.RemoteMediaConsented = true;
            var allow = false; var cached = false; var calls = 0; var downloads = 0;
            var v = new MandatoryVideoScheduler(new Host(), library: Array.Empty<string>)
            {
                RemoteMedia = true, MayFetchRemote = _ => allow, CachedScrolllerPath = _ => cached ? "/cache.mp4" : null,
                FetchRemote = _ => ++calls == 1 ? Task.FromResult((new List<FypAssetManifest.Entry> { Clip() }, (string?)null)) : pending.Task,
                DownloadRemote = (_, _) => { downloads++; cached = true; return Task.FromResult<string?>("/cache.mp4"); },
            };
            await v.RefillRemoteForTest(); allow = true;
            var fill = v.RefillRemoteForTest();
            var readyBeforeFailure = cached && !fill.IsCompleted;
            if (throws) pending.SetException(new InvalidOperationException("offline")); else pending.SetResult((new List<FypAssetManifest.Entry>(), "offline"));
            try { await fill; } catch (InvalidOperationException) when (throws) { }
            Assert.True(readyBeforeFailure); Assert.Equal(1, downloads); Assert.Equal(Url, v.PickNext());
            s.MediaSource = "local"; if (v.RemoteTask != null) await v.RemoteTask;
        }
        finally { (s.MediaSource, s.RemoteMediaConsented) = old; }
    }

    [Fact]
    public async Task Changed_channels_discard_a_pending_batch_before_downloading()
    {
        var s = CoreSettings.Current;
        var old = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineNiches, s.FypOnlineCustomSubs);
        try
        {
            s.RemoteMediaConsented = true; s.MediaSource = "online";
            s.FypOnlineNiches = new() { "hypno" }; s.FypOnlineCustomSubs = new();
            var pending = new TaskCompletionSource<(List<FypAssetManifest.Entry>, string?)>(TaskCreationOptions.RunContinuationsAsynchronously);
            var downloads = 0; var host = new Host();
            var v = new MandatoryVideoScheduler(host, library: () => new[] { "/local.mp4" })
            {
                RemoteMedia = true, FetchRemote = _ => pending.Task, CachedScrolllerPath = _ => null,
                MayFetchRemote = _ => true,
                DownloadRemote = (_, _) => { downloads++; return Task.FromResult<string?>("/cache.mp4"); },
            };
            var refill = v.RefillRemoteForTest();
            s.FypOnlineNiches = new() { "bimbo" };
            pending.SetResult((new List<FypAssetManifest.Entry> { Clip() }, null));
            await refill;
            Assert.Equal(0, downloads);
            s.MediaSource = "local";
            Assert.Equal("/local.mp4", v.PickNext());
            if (v.RemoteTask != null) await v.RemoteTask;
        }
        finally { (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineNiches, s.FypOnlineCustomSubs) = old; }
    }
}
