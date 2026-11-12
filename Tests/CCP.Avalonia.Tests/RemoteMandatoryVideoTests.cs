using System;
using System.Linq;
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
        }
        finally
        {
            overlay.CloseAll();
            MandatoryVideoOverlay.CachedScrolllerPath = oldCache;
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
