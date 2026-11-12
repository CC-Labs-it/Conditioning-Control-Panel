using System;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Fyp.Online;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class RemoteMandatoryVideoTests
{
    [Fact]
    public Task Playback_requires_consent_policy_and_a_cached_file_and_shows_the_credit() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        const string url = "https://cdn.scrolller.com/example.mp4";
        var s = CoreSettings.Current;
        var saved = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        var oldCache = MandatoryVideoOverlay.CachedScrolllerPath;
        var oldOpener = MandatoryVideoOverlay.CreditOpener; var opened = 0;
        var started = 0;
        var overlay = new MandatoryVideoOverlay(() => { started++; return new Decoder(); });
        try
        {
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = ("online", true, false);
            ScrolllerPolicy.Apply(true, null);
            MandatoryVideoOverlay.CachedScrolllerPath = _ => null;
            Assert.Null(MandatoryVideoOverlay.ResolvePlaybackPath(url));
            overlay.Show(url, false);
            Assert.Equal(0, started);
            MandatoryVideoOverlay.CachedScrolllerPath = _ => "/cache/example.mp4";
            Assert.Equal("/cache/example.mp4", MandatoryVideoOverlay.ResolvePlaybackPath(url));
            Assert.Equal("/videos/local.mp4", MandatoryVideoOverlay.ResolvePlaybackPath("/videos/local.mp4"));
            s.RemoteMediaConsented = false;
            Assert.Null(MandatoryVideoOverlay.ResolvePlaybackPath(url));
            s.RemoteMediaConsented = true;
            s.MediaSource = "local";
            Assert.Null(MandatoryVideoOverlay.ResolvePlaybackPath(url));
            s.MediaSource = "online";
            ScrolllerPolicy.Apply(false, null);
            Assert.Null(MandatoryVideoOverlay.ResolvePlaybackPath(url));
            ScrolllerPolicy.Apply(true, null);
            overlay.Show(url, false);
            Assert.Equal(1, started);
            var surface = overlay.Build(null, false);
            try
            {
                var mark = Assert.Single(((Panel)surface.Window.Content!).Children.OfType<ScrolllerCreditBadge>());
                Assert.True(mark.Shown);
                Assert.False(mark.IsHitTestVisible);
            }
            finally { surface.Window.Close(); }
            ScrolllerItemIndex.Remember(url, new ScrolllerCacheMeta("clip", "testsub"));
            MandatoryVideoOverlay.CreditOpener = _ => { opened++; return true; };
            overlay.Show(url, false); surface = overlay.Build(null, false);
            try
            {
                var card = Assert.Single(((Panel)surface.Window.Content!).Children.OfType<Button>());
                Assert.False(card.IsVisible);
                Assert.Equal("via Scrolller\nr/testsub", card.Content);
                overlay.UpdateCredit(7000, 7000, 10000); Assert.False(card.IsVisible);
                overlay.UpdateCredit(7500, 7500, 10000); Assert.True(card.IsVisible);
                card.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                overlay.FinishCredit(true);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal(1, opened);
                card.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); overlay.FinishCredit(true); overlay.CloseAll();
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Equal(1, opened);
                card.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent)); overlay.FinishCredit(true); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs(); Assert.Equal(1, opened); Assert.False(MandatoryVideoOverlay.CreditAllowed(true));
            }
            finally { surface.Window.Close(); }
        }
        finally
        {
            overlay.CloseAll();
            MandatoryVideoOverlay.CachedScrolllerPath = oldCache;
            MandatoryVideoOverlay.CreditOpener = oldOpener;
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented) = (saved.MediaSource, saved.RemoteMediaConsented, saved.FypOnlineConsented);
            ScrolllerPolicy.Apply(saved.Enabled, saved.MaxClipShortSide);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Refused_clip_cannot_stop_a_later_playback() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        const string url = "https://cdn.scrolller.com/example.mp4";
        var s = CoreSettings.Current;
        var saved = (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.AttentionChecksEnabled, ScrolllerPolicy.Enabled, ScrolllerPolicy.MaxClipShortSide);
        var cache = MandatoryVideoOverlay.CachedScrolllerPath;
        var post = CoreDispatch.PostProvider;
        var freeze = CoreSubliminal.BambiFreezeProvider;
        var clock = new Clock();
        var overlay = new MandatoryVideoOverlay(() => new Decoder());
        var video = new MandatoryVideoScheduler(overlay, clock, Array.Empty<string>) { RemotePick = () => url };
        overlay.Scheduler = video;
        try
        {
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.AttentionChecksEnabled) = ("online", true, false, false);
            ScrolllerPolicy.Apply(true, null);
            MandatoryVideoOverlay.CachedScrolllerPath = _ => null;
            CoreDispatch.PostProvider = a => a();
            CoreSubliminal.BambiFreezeProvider = null;
            Assert.True(video.Trigger());
            clock.Fire(); // refusal posts the cleanup
            video.Stop();
            Assert.True(video.Trigger(path: "/videos/next.mp4"));
            clock.Fire(); // a later clip is already playing before the post is processed
            Assert.True(video.IsPlaying);
            await Task.Delay(50);
            Assert.True(video.IsPlaying);
            video.Stop(); var picks = 0;
            video = new MandatoryVideoScheduler(overlay, clock, () => new[] { "/videos/fallback.mp4" })
                { RemotePick = () => picks++ == 0 ? url : null };
            overlay.Scheduler = video;
            Assert.True(video.Trigger()); clock.Fire();
            Assert.Equal("/videos/fallback.mp4", video.LastVideoPath);
            Assert.Equal(2, picks);
            video.Stop();
            picks = 0;
            video.RemotePick = () => { picks++; return url; };
            MandatoryVideoOverlay.CachedScrolllerPath = _ => "/cache/clip.mp4";
            Assert.True(video.Trigger()); clock.Fire();
            MandatoryVideoOverlay.CachedScrolllerPath = _ => null;
            overlay.FrameTs = System.Diagnostics.Stopwatch.GetTimestamp() - 10 * System.Diagnostics.Stopwatch.Frequency;
            overlay.GuardTick(); // the output-heal replay must not pick another clip
            Assert.Equal(1, picks);
        }
        finally
        {
            video.Stop();
            CoreDispatch.PostProvider = post;
            CoreSubliminal.BambiFreezeProvider = freeze;
            MandatoryVideoOverlay.CachedScrolllerPath = cache;
            (s.MediaSource, s.RemoteMediaConsented, s.FypOnlineConsented, s.AttentionChecksEnabled) = (saved.MediaSource, saved.RemoteMediaConsented, saved.FypOnlineConsented, saved.AttentionChecksEnabled);
            ScrolllerPolicy.Apply(saved.Enabled, saved.MaxClipShortSide);
        }
    });

    [Fact]
    public void Credit_uses_metadata_reloaded_from_the_device_cache()
    {
        const string url = "https://scrolller.com/restart-test.mp4";
        var dir = Directory.CreateTempSubdirectory("ccp-video-credit-").FullName;
        var root = ScrolllerDiskCache.RootProvider; var inline = ScrolllerDiskCache.WarmInline;
        var enabled = ScrolllerPolicy.Enabled; var max = ScrolllerPolicy.MaxClipShortSide;
        try
        {
            var file = Path.Combine(dir, new string('a', 64) + ".mp4"); File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            var now = DateTime.UtcNow; var book = new ScrolllerCacheBook { FormatChecked = true };
            book.Put(new ScrolllerCacheEntry(url, file, 3, now, new ScrolllerCacheMeta("clip", "savedsub"), now));
            File.WriteAllText(Path.Combine(dir, ScrolllerDiskCache.IndexFileName), book.ToJson());
            ScrolllerDiskCache.RootProvider = () => dir; ScrolllerDiskCache.WarmInline = true;
            ScrolllerPolicy.Apply(true, null);
            Assert.Equal("https://scrolller.com/r/savedsub", MandatoryVideoOverlay.CreditLinkFor(url));
        }
        finally
        {
            ScrolllerDiskCache.RootProvider = root; ScrolllerDiskCache.WarmInline = inline;
            ScrolllerPolicy.Apply(enabled, max); Directory.Delete(dir, true);
        }
    }
    private sealed class Clock : TimeProvider
    {
        private ManualTimer? _timer;
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period)
            => _timer = new ManualTimer(() => callback(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 10000;
        public bool IsPlaying => true;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public void SetPause(bool paused) { }
        public void Stop() { }
        public void Dispose() { }
    }
}
