using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

// Remote mandatory videos on the overlay (Mich #3966 + #4138, restack b185): the cached clip keeps
// its URL identity for the remote guards and the "via Scrolller" credit, whose link waits for the end
// of a full-screen clip and is refused under any lock.
public sealed class MandatoryVideoRemoteTests
{
    private const string Cached = "/cache/remote-test.mp4";

    [Fact]
    public void The_shared_overlay_plays_remote_clips() =>
        Assert.True(MandatoryVideoOverlay.Instance.Scheduler.RemoteMedia);

    [Fact]
    public void Scrolller_clip_has_a_credit_mark_but_local_video_does_not() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.True(MandatoryVideoOverlay.CreditFor("https://v.scrolller.com/clip.mp4").Shown);
        Assert.True(MandatoryVideoOverlay.CreditFor("https://example.com/credited-test.mp4", _ => new ScrolllerCacheMeta("clip", "test")).Shown);
        Assert.False(MandatoryVideoOverlay.CreditFor("/local.mp4").Shown);
    });

    [Fact]
    public Task Fullscreen_press_waits_for_the_natural_end_and_floating_opens_at_once() => Run((o, v, opened, url) =>
    {
        var button = o.CreditButton(url);
        Assert.True(button.IsHitTestVisible);
        Assert.False(button.Focusable);
        Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), button.Cursor?.ToString());
        Assert.Equal(ScrolllerCreditRules.Mark, AutomationProperties.GetName(button));
        Assert.False(o.CreditButton("/local.mp4").IsHitTestVisible);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs(); Assert.Empty(opened);
        Assert.True(Tick(button).IsVisible);
        o.OnNaturalVideoEnd(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "https://scrolller.com/r/test_sub" }, opened);
        Assert.False(v.IsPlaying);
    });

    [Fact]
    public Task Floating_press_opens_at_once() => Run((o, v, opened, url) =>
    {
        typeof(MandatoryVideoOverlay).GetProperty("Floating", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o.Overlay, true);
        o.CreditPressed(url); Dispatcher.UIThread.RunJobs();
        Assert.Single(opened);
        o.CreditPressed("/local.mp4"); Dispatcher.UIThread.RunJobs();   // an unrelated clip never opens
        Assert.Single(opened);
    });

    [Fact]
    public Task Queued_tick_shows_on_every_screen_and_panic_drops_the_link() => Run((o, v, opened, url) =>
    {
        var first = o.CreditButton(url); var second = o.CreditButton(url);
        Assert.False(Tick(first).IsVisible);
        o.CreditPressed(url);
        Assert.True(Tick(first).IsVisible); Assert.True(Tick(second).IsVisible);
        v.ForceCleanup();   // panic / stop: the tick clears, the queued link never opens
        Assert.False(Tick(first).IsVisible); Assert.False(Tick(second).IsVisible);
        o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(opened);
    });

    [Fact]
    public Task Locks_refuse_the_link_at_the_press_and_before_it_opens() => Run((o, v, opened, url) =>
    {
        var previous = LockdownService.Current;
        var lockdown = LockdownService.Current = new LockdownService();
        try
        {
            o.CreditPressed(url);
            lockdown.Activate(TimeSpan.FromMinutes(30));   // locked after the press, before the clip end
            o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs();
            Assert.Empty(opened);
            Assert.False(o.CreditButton(url).IsHitTestVisible);
            lockdown.Deactivate();
            o.CreditPressed(url); o.OpenQueuedCreditLink();
            lockdown.Activate(TimeSpan.FromMinutes(30));   // locked between the release and the dispatch
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(opened);
        }
        finally { lockdown.Deactivate(); LockdownService.Current = previous; }
    });

    [Fact]
    public Task A_strict_clip_never_offers_the_link() => Run((o, v, opened, url) =>
    {
        var locked = o.CreditButton(url);
        Assert.False(locked.IsHitTestVisible); Assert.Null(locked.Cursor);
        o.CreditPressed(url); o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs();
        Assert.Empty(opened);
    }, strict: true);

    [Fact]
    public Task The_duration_cap_releases_a_queued_link() => Run((o, v, opened, url) =>
    {
        o.CreditPressed(url);
        o.PlayedSkew = 2;   // past the 1 s cap
        o.GuardTick(); Dispatcher.UIThread.RunJobs();
        Assert.Single(opened);
        Assert.False(v.IsPlaying);
    }, cap: 1);

    [Fact]
    public Task A_cached_remote_clip_ends_without_a_replay_when_frames_stop() => Run((o, v, opened, url) =>
    {
        o.FrameTs = Stopwatch.GetTimestamp() - 10 * Stopwatch.Frequency;   // deterministic output loss
        o.GuardTick();
        Assert.False(v.IsPlaying);
        Assert.Equal(1, o.Plays);   // a local clip would replay once (VideoCutPlaybackIntegrationTests)
    });

    [Fact]
    public Task Credit_fade_keeps_the_longer_gentle_fade() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.GentleVisuals = false; settings.Current.GentleFadeSeconds = 2;
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            var o = new MandatoryVideoOverlay(() => new Decoder());
            var normal = (Border)o.CreditButton("/local.mp4").Content!;
            Assert.Equal(TimeSpan.FromMilliseconds(600), Assert.IsType<DoubleTransition>(Assert.Single(normal.Transitions!)).Duration);
            settings.Current.GentleVisuals = true;
            var gentle = (Border)o.CreditButton("/local.mp4").Content!;
            Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<DoubleTransition>(Assert.Single(gentle.Transitions!)).Duration);
        }
        finally { CoreSettings.ServiceProvider = old; }
        return Task.CompletedTask;
    });

    private static IconGlyph Tick(Button button) => (IconGlyph)((StackPanel)((Border)button.Content!).Child!).Children[1];

    private static Task Run(Action<CountingOverlay, MandatoryVideoScheduler, List<string>, string> body, bool strict = false, int cap = 0)
        => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var s = CoreSettings.Current;
        var saved = (s.VideoMaxDurationSeconds, s.VideoCutRandomStart, s.StrictLockEnabled);
        var post = CoreDispatch.PostProvider;
        var opener = MandatoryVideoOverlay.CreditOpener;
        var url = $"https://v.scrolller.com/remote-{Guid.NewGuid():N}.mp4";
        ScrolllerItemIndex.Remember(url, new ScrolllerCacheMeta("clip", "test_sub"));
        var opened = new List<string>();
        var clock = new Clock();
        var o = new CountingOverlay(clock);
        var v = new MandatoryVideoScheduler(o.Overlay, clock, Array.Empty<string>) { CachedScrolllerPath = u => u == url ? Cached : null };
        o.Overlay.Scheduler = v;
        try
        {
            (s.VideoMaxDurationSeconds, s.VideoCutRandomStart, s.StrictLockEnabled) = (cap, false, false);
            CoreDispatch.PostProvider = a => a();
            MandatoryVideoOverlay.CreditOpener = link => { opened.Add(link); return true; };
            Assert.True(v.Trigger(strictOverride: strict, path: url));
            clock.Fire();   // pre-roll, no sleeping
            Assert.Equal(1, o.Plays);
            Assert.Equal(url, v.ShowingPath);   // the overlay plays the cached file under the URL's identity
            body(o, v, opened, url);
        }
        finally
        {
            v.Stop();
            MandatoryVideoOverlay.CreditOpener = opener;
            CoreDispatch.PostProvider = post;
            (s.VideoMaxDurationSeconds, s.VideoCutRandomStart, s.StrictLockEnabled) = saved;
        }
        return Task.CompletedTask;
    });

    /// <summary>The overlay with an injected decoder; counts the clips it started (the heal is a second Play).</summary>
    private sealed class CountingOverlay
    {
        public readonly MandatoryVideoOverlay Overlay;
        public int Plays;
        public CountingOverlay(TimeProvider time) =>
            Overlay = new MandatoryVideoOverlay(() => { Plays++; return new Decoder(); }) { Time = time };
        public Button CreditButton(string path) => Overlay.CreditButton(path);
        public void CreditPressed(string path) => Overlay.CreditPressed(path);
        public void OpenQueuedCreditLink() => Overlay.OpenQueuedCreditLink();
        public void OnNaturalVideoEnd() => Overlay.OnNaturalVideoEnd();
        public void GuardTick() => Overlay.GuardTick();
        public double PlayedSkew { set => Overlay.PlayedSkew = value; }
        public long FrameTs { set => Overlay.FrameTs = value; }
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => !Stopped;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Stopped;
        public void SetPause(bool paused) { }
        public void Stop() => Stopped = true;
        public void Dispose() { }
    }

    private sealed class Clock : TimeProvider
    {
        private long _ms;
        private ManualTimer? _timer;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => _ms;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_ms);
        public void Fire() => _timer!.Fire();
        public override ITimer CreateTimer(TimerCallback cb, object? state, TimeSpan due, TimeSpan period)
            => _timer = new ManualTimer(() => cb(state));
        private sealed class ManualTimer(Action fire) : ITimer
        {
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }
}
