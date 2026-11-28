using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using System.Text.Json;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Fyp.Online;
using Avalonia.Threading;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF VideoService.FloatingInstances.cs: the multi-window floating host, behind the decoder seam.</summary>
public sealed class MandatoryVideoFloatingHostTests
{
    private sealed class StillTime : TimeProvider, ITimer
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => this;
        public bool Change(TimeSpan due, TimeSpan period) => true;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class Decoder : IMandatoryPlayback
    {
        public long Time { get; set; }
        public long Length => 120000;
        public bool IsPlaying => !Paused && !Stopped;
        public bool IsSeekable => true;
        public bool HasVideo => true;
        public bool Paused, Stopped;
        public Action? BeforeStop;
        public bool? AudioOff;
        public void SetPause(bool paused) => Paused = paused;
        public void Stop() { BeforeStop?.Invoke(); Stopped = true; }
        public void Dispose() { }
        public readonly List<bool> AudioCalls = new();
        public void SetAudioOff(bool off) { AudioOff = off; AudioCalls.Add(off); }
    }

    /// <summary>Always the same spot: only the "taken" rule keeps windows apart.</summary>
    private sealed class SameSpot : Random
    {
        public override int Next(int minValue, int maxValue) => minValue;
        public override int Next(int maxValue) => 0;
        public override double NextDouble() => 0;
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private static Task Run(Action<MandatoryVideoOverlay, List<Decoder>> body, bool together = false, bool keepSound = false) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var saved = (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether, s.MandatoryVideoFloatingKeepSound,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled);
            (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether, s.MandatoryVideoFloatingKeepSound,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled)
                = (true, 3, together, keepSound, false, false, false, false, 0, false);
            var decoders = new List<Decoder>();
            var o = new MandatoryVideoOverlay(() => { var d = new Decoder(); decoders.Add(d); return d; });
            o.FloatingInputRegionWriter = (_, _, _) => true;
            o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), () => new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4", "/v/d.mp4", "/v/e.mp4" });
            try
            {
                // The run's first window: the scheduler accepts, its pre-roll then shows the clip.
                Assert.True(o.Scheduler.Trigger(userInitiated: false));
                o.Show("/v/first.mp4", strict: false);
                body(o, decoders);
            }
            finally
            {
                o.Scheduler.ForceCleanup();
                o.CloseAll();
                (s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether, s.MandatoryVideoFloatingKeepSound,
                    s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled) = saved;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Panic_stops_an_added_window_reveal_on_the_same_tick() => Run((o, decoders) =>
    {
        var settings = CoreSettings.Current;
        var oldLevel = settings.MotionLevel;
        var oldProbe = ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe;
        var previous = CoreEngine.Video;
        try
        {
            settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = () => true;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
            CoreEngine.Video = o.Scheduler;
            var regions = new List<PixelRect[]?>();
            o.FloatingInputRegionWriter = (_, rects, _) => { regions.Add(rects); return true; };
            Assert.True(o.TryFloatingTrigger(userInitiated: true));
            var joined = Assert.Single(o.Floaters);
            var reveal = Assert.IsType<FloatingVideoReveal>(joined.Surface.Reveal);
            Assert.True(reveal.IsAnimating);
            Assert.NotEmpty(regions);
            Assert.NotNull(regions[^1]);
            var stoppedBeforeDecoder = false;
            decoders[^1].BeforeStop = () => stoppedBeforeDecoder = !reveal.IsAnimating && regions[^1] == null;
            PanicSurfaces.All.Single(s => s.Id == "engine").Stop(null);
            Assert.True(stoppedBeforeDecoder);
            Assert.Null(regions[^1]);
            Assert.False(reveal.IsAnimating);
            Assert.False(joined.Window.IsVisible);
            reveal.RevealAt(260);
            Assert.False(joined.Window.IsVisible);
        }
        finally
        {
            CoreEngine.Video = previous;
            settings.MotionLevel = oldLevel;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.OsProbe = oldProbe;
            ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.CacheStamp = long.MinValue;
        }
    });

    [Fact]
    public Task Joined_blur_uses_its_own_frames_and_panic_releases_it() => Run((o, decoders) =>
    {
        Assert.True(o.TryFloatingTrigger(userInitiated: true));
        Assert.True(o.TryFloatingTrigger(userInitiated: true));
        var first = o.Floaters[0];
        var second = o.Floaters[1];
        using var frame = new global::Avalonia.Media.Imaging.WriteableBitmap(new PixelSize(16, 90), new Vector(96, 96),
            global::Avalonia.Platform.PixelFormat.Bgra8888, global::Avalonia.Platform.AlphaFormat.Opaque);
        using (var pixels = frame.Lock())
            for (var y = 0; y < pixels.Size.Height; y++)
                for (var x = 0; x < pixels.Size.Width; x++)
                    System.Runtime.InteropServices.Marshal.WriteInt32(pixels.Address + y * pixels.RowBytes + x * 4, unchecked((int)0xff112233));

        MandatoryVideoOverlay.UpdateFloatingFill(first, frame, true);
        Assert.True(first.Surface.Fill.IsVisible);
        Assert.True(first.Surface.Scrim.IsVisible);
        Assert.Same(first.Fill, first.Surface.Fill.Source);
        Assert.False(second.Surface.Fill.IsVisible);
        using (var pixels = first.Fill!.Lock())
            Assert.Equal(unchecked((int)0xff112233), System.Runtime.InteropServices.Marshal.ReadInt32(pixels.Address));

        MandatoryVideoOverlay.UpdateFloatingFill(second, frame, true);
        Assert.NotSame(first.Fill, second.Fill);
        MandatoryVideoOverlay.UpdateFloatingFill(first, frame, false);
        Assert.Null(first.Fill);
        Assert.Null(first.Surface.Fill.Source);
        Assert.False(first.Surface.Scrim.IsVisible);
        Assert.True(second.Surface.Fill.IsVisible);

        var previous = CoreEngine.Video;
        try
        {
            CoreEngine.Video = o.Scheduler;
            PanicSurfaces.All.Single(x => x.Id == "engine").Stop(null);
            Assert.Empty(o.Floaters);
            Assert.Null(second.Fill);
            Assert.Null(second.Surface.Fill.Source);
            Assert.False(second.Surface.Window.IsVisible);
            Assert.All(decoders, d => Assert.True(d.Stopped));
        }
        finally { CoreEngine.Video = previous; }
    });

    private static List<Window> Up(MandatoryVideoOverlay o) => o.Windows.Where(w => w.IsVisible).ToList();

    [Fact]
    public async Task Joined_credit_test_does_not_change_global_scroller_index()
    {
        var paths = new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4", "/v/d.mp4", "/v/e.mp4" };
        var before = paths.Select(ScrolllerItemIndex.MetaFor).ToArray();
        await Joined_floating_credit_opens_its_own_link_and_panic_cancels_pending_open();
        Assert.Equal(before, paths.Select(ScrolllerItemIndex.MetaFor).ToArray());
    }

    [Fact]
    public Task Joined_floating_credit_opens_its_own_link_and_panic_cancels_pending_open() => Run((o, _) =>
    {
        var paths = new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4", "/v/d.mp4", "/v/e.mp4" };
        o.CreditLookup = path => paths.Contains(path) ? new ScrolllerCacheMeta("clip", "joined_sub") : ScrolllerItemIndex.MetaFor(path);
        var opener = MandatoryVideoOverlay.CreditOpener;
        var previousVideo = CoreEngine.Video;
        var opened = new List<string>();
        try
        {
            MandatoryVideoOverlay.CreditOpener = link => { opened.Add(link); return true; };
            CoreEngine.Video = o.Scheduler;
            Assert.True(o.TryFloatingTrigger(userInitiated: true));
            var joined = Assert.Single(o.Floaters);
            var button = Assert.IsType<Button>(((Panel)((Border)joined.Window.Content!).Child!).Children.Last());
            Assert.True(button.IsHitTestVisible);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "https://scrolller.com/r/joined_sub" }, opened);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            PanicSurfaces.All.Single(s => s.Id == "engine").Stop(null);
            Dispatcher.UIThread.RunJobs();
            Assert.Single(opened);
            Assert.True(joined.Closed);
        }
        finally
        {
            CoreEngine.Video = previousVideo;
            MandatoryVideoOverlay.CreditOpener = opener;
        }
    });

    /// <summary>Timers wait for <see cref="FireLast"/>: the most recently armed one fires.</summary>
    private sealed class Clock : TimeProvider
    {
        public readonly List<Timer> Armed = new();
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new Timer(this, () => callback(state));
        public void FireLast() { var t = Armed[^1]; Armed.Remove(t); t.Fire(); }
        public sealed class Timer(Clock clock, Action fire) : ITimer
        {
            public TimeSpan Due { get; private set; }
            public void Fire() => fire();
            public bool Change(TimeSpan due, TimeSpan period)
            {
                clock.Armed.Remove(this);
                Due = due;
                if (due != Timeout.InfiniteTimeSpan) clock.Armed.Add(this);
                return true;
            }
            public void Dispose() => clock.Armed.Remove(this);
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }

    /// <summary>WPF VideoService.cs:2940-2946 + VideoService.FloatingInstances.cs:117,250 (SchedulerMayTrigger):
    /// a scheduler tick during an open floating run with room opens one more window through the real host.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Scheduler_ticks_join_the_open_run_until_the_limit_and_panic_closes_all(bool together) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var s = CoreSettings.Current;
            var saved = (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled);
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether,
                s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled)
                = (true, 20, true, 3, together, false, false, false, false, 0, false);
            var post = CoreDispatch.PostProvider; var previousVideo = CoreEngine.Video;
            var clock = new Clock(); var decoders = new List<Decoder>();
            var o = new MandatoryVideoOverlay(() => { var d = new Decoder(); decoders.Add(d); return d; });
            o.Scheduler = new MandatoryVideoScheduler(o, clock, () => new[] { "/v/a.mp4", "/v/b.mp4", "/v/c.mp4", "/v/d.mp4" });
            try
            {
                CoreDispatch.PostProvider = a => a(); CoreEngine.Video = o.Scheduler;
                o.Scheduler.DeferTrigger = ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.DeferVideo; o.Scheduler.Start();
                clock.FireLast();   // the schedule fires: pre-roll
                clock.FireLast();   // pre-roll ends: the run's first window
                Assert.True(o.FloatingRunOpen);
                if (together)
                {
                    Assert.Equal(3, Up(o).Count);
                    o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // room again
                    Assert.Equal(2, Up(o).Count);
                    clock.FireLast();   // the next tick
                    Assert.Equal(2, Up(o).Count);   // Pop up together: the run waits to end, no join
                }
                else
                {
                    Assert.Single(Up(o)); ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.BeginWebVideoTakeover();
                    Assert.False(ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.WebVideoClaimed);
                    ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.EndWebVideo();
                    clock.FireLast();   // join tick
                    Assert.Equal(2, Up(o).Count); clock.FireLast(); Assert.Equal(3, Up(o).Count);
                    clock.FireLast();   // at the limit: skipped
                    Assert.Equal(3, Up(o).Count); Assert.Equal(3, decoders.Count);
                }
                o.Scheduler.ForceCleanup();   // the engine stop a panic runs
                Assert.Empty(Up(o)); Assert.Equal(0, o.FloatingUp); Assert.Empty(clock.Armed);
            }
            finally
            {
                o.Scheduler.Stop(); o.CloseAll();
                ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.RetryPendingCard();
                CoreEngine.Video = previousVideo;
                CoreDispatch.PostProvider = post;
                (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount, s.MandatoryVideoFloatingTogether,
                    s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.AttentionChecksEnabled, s.VideoMaxDurationSeconds, s.FlashEnabled) = saved;
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task Browser_media_holds_a_real_floating_join_until_retry() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount,
            s.MandatoryVideoFloatingTogether, s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.FlashEnabled, s.ProtectBrowserVideoPlayback);
        var busy = ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.InterruptingBrowserMedia;
        var stamp = ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.RemixMediaTick;
        var post = CoreDispatch.PostProvider;
        var previousVideo = CoreEngine.Video;
        var clock = new Clock();
        var o = new MandatoryVideoOverlay(() => new Decoder());
        o.Scheduler = new MandatoryVideoScheduler(o, clock, () => new[] { "/v/a.mp4", "/v/b.mp4" });
        try
        {
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount,
                s.MandatoryVideoFloatingTogether, s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.FlashEnabled, s.ProtectBrowserVideoPlayback)
                = (true, 20, true, 3, false, false, false, false, false, false);
            CoreDispatch.PostProvider = a => a(); CoreEngine.Video = o.Scheduler;
            o.Scheduler.DeferTrigger = ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.DeferVideo;
            ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.RemixMediaTick = System.Diagnostics.Stopwatch.GetTimestamp();
            ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.InterruptingBrowserMedia = false;
            ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.BeginWebVideoTakeover();
            o.Scheduler.Start(); clock.FireLast();
            Assert.False(o.Scheduler.IsPlaying);
            Assert.Equal(TimeSpan.FromSeconds(30), clock.Armed[^1].Due);
            ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.EndWebVideo();
            s.ProtectBrowserVideoPlayback = true;
            clock.FireLast(); clock.FireLast();
            Assert.Single(Up(o));
            ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.InterruptingBrowserMedia = true;
            clock.FireLast();
            Assert.Single(Up(o));
            Assert.Equal(TimeSpan.FromSeconds(30), clock.Armed[^1].Due);
            ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.InterruptingBrowserMedia = false;
            clock.FireLast();
            Assert.Equal(2, Up(o).Count);
        }
        finally
        {
            o.Scheduler.Stop(); o.CloseAll();
            ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.EndWebVideo();
            ConditioningControlPanel.Avalonia.Views.Windows.LockCardWindow.RetryPendingCard();
            CoreEngine.Video = previousVideo; CoreDispatch.PostProvider = post;
            ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.InterruptingBrowserMedia = busy;
            ConditioningControlPanel.Avalonia.Views.Tabs.SettingsTabView.RemixMediaTick = stamp;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount,
                s.MandatoryVideoFloatingTogether, s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.FlashEnabled, s.ProtectBrowserVideoPlayback) = saved;
        }
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Cross_origin_frame_reports_hold_and_release_a_scheduled_join(bool failedNavigation) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var saved = (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount,
            s.MandatoryVideoFloatingTogether, s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.FlashEnabled, s.ProtectBrowserVideoPlayback);
        var busy = (SettingsTabView.RemixBrowserMediaBusy, SettingsTabView.InterruptingBrowserMedia, SettingsTabView.RemixMediaTick);
        var post = CoreDispatch.PostProvider; var previousVideo = CoreEngine.Video;
        var clock = new Clock(); var page = new SettingsTabView();
        var web = page.FindControl<WebHost>("BrowserWebHost")!;
        string? id = null;
        web.AudioProbe = () => null; // WKWebView and other engines without an audio probe
        web.ScriptInvoker = script =>
        {
            if (script.Contains("ccpRemixMedia")) id = Regex.Match(script, "const id = \"([^\"]+)\"").Groups[1].Value;
            return Task.FromResult<string?>("true");
        };
        var browser = new Window { Content = page };
        var o = new MandatoryVideoOverlay(() => new Decoder());
        o.Scheduler = new MandatoryVideoScheduler(o, clock, () => new[] { "/v/a.mp4", "/v/b.mp4" });
        try
        {
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount,
                s.MandatoryVideoFloatingTogether, s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.FlashEnabled, s.ProtectBrowserVideoPlayback)
                = (true, 20, true, 3, false, false, false, false, false, true);
            CoreDispatch.PostProvider = a => a(); CoreEngine.Video = o.Scheduler;
            o.Scheduler.DeferTrigger = LockCardWindow.DeferVideo;
            browser.Show();
            web.OnNavigationCompleted(new Uri("https://example.com"), success: !failedNavigation);
            if (!failedNavigation)
            {
                Assert.False(string.IsNullOrEmpty(id));
                web.OnWebMessage(JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, active = false, interrupting = false, unknown = true }));
                void Frame(string name, bool active) => web.OnWebMessage(JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, frame = name, active }));
                Frame("first", false); // an idle unreadable iframe does not block
                Assert.False(o.BrowserMediaBlocksScheduledVideo);
                Frame("first", true); Frame("second", false);
                Assert.True(o.BrowserMediaBlocksScheduledVideo); // idle second frame cannot clear the playing first
                Frame("first", false);
            }
            Assert.False(o.BrowserMediaBlocksScheduledVideo); // also after a failed navigation
            o.Scheduler.Start(); clock.FireLast(); clock.FireLast();
            Assert.Single(Up(o));
            if (!failedNavigation)
            {
                web.OnWebMessage(JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, frame = "first", active = true }));
                clock.FireLast(); Assert.Single(Up(o)); Assert.Equal(TimeSpan.FromSeconds(30), clock.Armed[^1].Due);
                web.OnWebMessage(JsonSerializer.Serialize(new { type = "ccpRemixMedia", id, frame = "first", active = false }));
            }
            clock.FireLast(); Assert.Equal(2, Up(o).Count);
        }
        finally
        {
            o.Scheduler.Stop(); o.CloseAll(); browser.Close(); global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            LockCardWindow.EndWebVideo(); LockCardWindow.RetryPendingCard();
            (SettingsTabView.RemixBrowserMediaBusy, SettingsTabView.InterruptingBrowserMedia, SettingsTabView.RemixMediaTick) = busy;
            CoreEngine.Video = previousVideo; CoreDispatch.PostProvider = post;
            (s.MandatoryVideosEnabled, s.VideosPerHour, s.MandatoryVideoFloating, s.MandatoryVideoFloatingCount,
                s.MandatoryVideoFloatingTogether, s.AudioDuckingEnabled, s.GentleVisuals, s.StrictLockEnabled, s.FlashEnabled, s.ProtectBrowserVideoPlayback) = saved;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Test_presses_add_windows_up_to_the_limit_then_replace_the_oldest() => Run((o, decoders) =>
    {
        Assert.Single(Up(o));
        var first = o.Windows[0];
        Assert.True(o.Scheduler.Trigger(userInitiated: true));
        Assert.True(o.Scheduler.Trigger(userInitiated: true));
        Assert.Equal(3, Up(o).Count);
        Assert.Equal(3, o.FloatingUp);

        Assert.True(o.Scheduler.Trigger(userInitiated: true));   // Test at the limit: the oldest makes room
        var up = Up(o);
        Assert.Equal(3, up.Count);
        Assert.DoesNotContain(first, up);
        Assert.False(first.IsVisible);
        Assert.True(decoders[0].Stopped, "the replaced window's player is stopped");
        Assert.Equal(4, decoders.Count);
        Assert.True(o.Scheduler.IsPlaying);
    });

    [Fact]
    public Task Automatic_triggers_join_up_to_the_limit_and_a_fourth_is_skipped() => Run((o, decoders) =>
    {
        Assert.True(o.FloatingRunOpen);
        Assert.True(o.Scheduler.Trigger());
        Assert.True(o.Scheduler.Trigger());
        Assert.Equal(3, Up(o).Count);
        Assert.True(o.Scheduler.Trigger());   // automatic at the limit: a quiet skip
        Assert.Equal(3, Up(o).Count);
        Assert.Equal(3, decoders.Count);
        Assert.False(decoders.Any(d => d.Stopped));
    });

    [Fact]
    public Task Pop_up_together_opens_every_free_slot_at_once() => Run((o, _) =>
    {
        Assert.Equal(3, Up(o).Count);
        Assert.Equal(2, o.Floaters.Select(f => f.Clip).Distinct().Count());
    }, together: true);

    [Fact]
    public Task No_window_lands_exactly_on_another() => Run((o, _) =>
        {
            typeof(MandatoryVideoOverlay).GetField("_random", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, new SameSpot());
            o.Scheduler.Trigger(userInitiated: true);
            o.Scheduler.Trigger(userInitiated: true);
            var spots = o.Windows.Select(w => w.Position).ToList();
            Assert.Equal(3, spots.Count);
            for (var i = 0; i < spots.Count; i++)
                for (var j = i + 1; j < spots.Count; j++)
                    Assert.False(Math.Abs(spots[i].X - spots[j].X) < MandatoryVideoPresentation.PileOffset
                        && Math.Abs(spots[i].Y - spots[j].Y) < MandatoryVideoPresentation.PileOffset,
                        $"window {j} is on top of window {i}: {spots[i]} / {spots[j]}");
        });

    [Fact]
    public Task Closing_one_clip_leaves_the_others_and_the_run_ends_with_the_last() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        o.Scheduler.Trigger(userInitiated: true);
        var first = o.Windows[0];

        o.OnNaturalVideoEnd();   // the first clip ends: only its window goes
        Assert.False(first.IsVisible);
        Assert.Equal(2, Up(o).Count);
        Assert.True(decoders[0].Stopped);
        Assert.False(decoders[1].Stopped || decoders[2].Stopped);
        Assert.True(o.FloatingUp > 0);
        Assert.True(o.FloatingRunOpen);
        Assert.True(o.Scheduler.IsPlaying);

        var second = o.Floaters[0];
        second.Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(Up(o));
        Assert.True(o.FloatingUp > 0);

        o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Empty(Up(o));
        Assert.Equal(0, o.FloatingUp);
        Assert.False(o.FloatingRunOpen);
        Assert.False(o.Scheduler.IsPlaying, "the last window ends the run");
    });

    [Fact]
    public Task Grace_holds_every_window_and_panic_closes_them_all_with_the_card() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        o.Scheduler.Trigger(userInitiated: true);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        Assert.All(decoders, d => Assert.True(d.Paused));
        var card = o.Surfaces[0].GraceHost;
        Assert.NotNull(card);
        o.Scheduler.Trigger(userInitiated: true);   // no window joins or replaces while the run is held
        Assert.Equal(3, decoders.Count);

        o.Scheduler.ForceCleanup();   // the engine stop a panic runs
        Assert.Empty(o.Windows);
        Assert.False(card!.IsVisible);
        Assert.All(decoders, d => Assert.True(d.Stopped));
        Assert.Equal(0, o.FloatingUp);
        Assert.False(o.FloatingRunOpen);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Exactly_one_window_has_the_sound_and_a_click_takes_it(bool keepSound) => Run((o, decoders) =>
    {
        o.Scheduler.Trigger();
        o.Scheduler.Trigger();
        Assert.Single(decoders, d => d.AudioOff != true);
        // The newest takes the sound; with Keep sound the first window keeps it.
        Assert.Same(keepSound ? decoders[0] : decoders[2], decoders.Single(d => d.AudioOff != true));

        var middle = o.Floaters[0].Window;
        middle.MouseDown(new Point(5, 5), MouseButton.Left);
        middle.MouseUp(new Point(5, 5), MouseButton.Left);
        Assert.Same(decoders[1], decoders.Single(d => d.AudioOff != true));
        Assert.True(o.HasFloatingSound(middle));

        o.Floaters[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the holder goes
        Assert.Single(decoders.Where(d => !d.Stopped), d => d.AudioOff != true);
    }, keepSound: keepSound);

    [Fact]
    public Task Closing_the_first_window_in_a_grace_pause_resumes_the_others() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        o.Surfaces[0].Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Single(Up(o));
        Assert.False(decoders[1].Paused, "the joined window plays on");
        Assert.Equal(GraceDecision.FallThrough, o.EvaluateGraceRequest(true, double.MaxValue));   // the grace pause stays spent
    });

    [Fact]
    public Task A_Lockdown_hides_and_refuses_the_joined_windows_close_button() => Run((o, _) =>
    {
        var (previous, lockdown) = (LockdownService.Current, LockdownService.Current = new LockdownService());
        try
        {
            o.Scheduler.Trigger(userInitiated: true);
            var close = o.Floaters[0].Surface.Buttons.Close;
            Assert.True(close.IsVisible);
            lockdown.Activate(TimeSpan.FromMinutes(30));
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.False(close.IsVisible);
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // refused: the window stays
            Assert.Equal(2, Up(o).Count);
        }
        finally { lockdown.Deactivate(); lockdown.Dispose(); LockdownService.Current = previous; }
    });

    [Fact]
    public Task AI_play_video_refuses_while_a_floating_run_plays() => Run((o, _) =>
    {
        var s = CoreSettings.Current;
        var saved = (CoreEngine.Video, CoreAccount.HasLabAccessProvider, s.CompanionPrompt.AllowAiToControlEffects);
        var seeded = ConditioningControlPanel.Services.Commands.MediaCommand.VideoSurface;
        (CoreEngine.Video, CoreAccount.HasLabAccessProvider, s.CompanionPrompt.AllowAiToControlEffects) = (o.Scheduler, () => true, true);
        global::ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect = start => start();
        try
        {
            // The surface itself, never Seed(): Seed swaps process-wide command delegates other tests rely on.
            Assert.False(global::ConditioningControlPanel.Avalonia.CompanionEffects.PlayVideo("x"));   // WPF App.AiEffects.cs:69-74
            Assert.Equal(1, o.FloatingUp);
            // Held behind the portal bind while the video starts: the released start still refuses.
            System.Action? held = null;
            global::ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect = start => held = start;
            o.Scheduler.ForceCleanup();
            global::ConditioningControlPanel.Avalonia.CompanionEffects.PlayVideo("x");
            Assert.True(o.Scheduler.Trigger(userInitiated: false));
            o.Show("/v/first.mp4", strict: false);
            held!();
            Assert.Equal(1, o.FloatingUp);
            Assert.Same(seeded, ConditioningControlPanel.Services.Commands.MediaCommand.VideoSurface);   // no process-wide seeding left behind
        }
        finally
        {
            global::ConditioningControlPanel.Avalonia.CompanionEffects.StartEffect = global::ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StartEffect;
            (CoreEngine.Video, CoreAccount.HasLabAccessProvider, s.CompanionPrompt.AllowAiToControlEffects) = saved;
        }
    });

    // WPF VideoService.FloatingInstances.cs:534-535: a veiled window is muted before it can play; focus comes at the reveal.
    [Fact]
    public Task A_joined_player_is_silent_before_its_reveal() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger();
        Assert.True(decoders[1].AudioCalls[0], "muted before the reveal's focus pass");
        Assert.False(decoders[1].AudioOff, "then the newest takes the sound");
    });

    // LibVLC volume is one process-wide session on Windows (WPF VideoService.FloatingInstances.cs:74-75): a joined
    // window's own mute and its closing hold silence its audio track, never the shared volume the other window plays at.
    [Fact]
    public Task A_joined_windows_mute_and_close_silence_only_its_own_track() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger();
        o.Scheduler.Trigger();
        Assert.Same(decoders[2], decoders.Single(d => d.AudioOff != true));   // the newest has the sound
        var newest = o.Floaters[1];
        newest.Surface.Buttons.Mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(newest.UserMuted);
        Assert.True(decoders[2].AudioOff, "its own mute deselects its track");
        newest.Surface.Buttons.Mute.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(decoders[2].AudioOff, "unmuted, the focus holder plays again");

        var middle = o.Floaters[0].Window;   // the middle window takes the sound, then the newest closes
        middle.MouseDown(new Point(5, 5), MouseButton.Left);
        middle.MouseUp(new Point(5, 5), MouseButton.Left);
        Assert.False(decoders[1].AudioOff);
        newest.Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(decoders[1].AudioOff, "a closing window's hold leaves the holder's sound alone");
    });

    // WPF VideoService.cs:6444-6456: a Resume card on every screen holding a floating window, kept when its host closes.
    [Fact]
    public Task Grace_shows_a_card_per_screen_and_keeps_one_when_a_window_closes() => Run((o, _) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        o.Scheduler.Trigger(userInitiated: true);
        o.OnNaturalVideoEnd();   // the first window goes: the joined ones carry the run
        o.Scheduler.Trigger(userInitiated: true);
        var f = o.Floaters;
        Assert.Equal(3, f.Count);
        OnOtherScreen(f[2].Surface);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        Assert.Equal(new[] { true, false, true }, f.Select(x => x.Surface.GraceHost != null));   // one card per screen

        var host = f[0].Window;
        f[0].Surface.Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));   // the card's host closes mid-pause
        Assert.False(host.IsVisible);
        Assert.True(o.GracePaused);
        Assert.True(f[0].Surface.GraceHost?.IsVisible, "its screen keeps a Resume card");
    });

    private static void OnOtherScreen(MandatoryVideoOverlay.Surface x)
    {
        // Screen has no public constructor (ChaosDvdOverlayTests.MakeScreen).
        var other = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(5000), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(other, new PixelRect(5000, 0, 1920, 1080));
        typeof(MandatoryVideoOverlay.Surface).GetField("<Screen>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(x, other);
    }

    // WPF VideoService.cs:6325-6335: the resume that ends a grace pause closes every screen's card.
    [Fact]
    public Task Closing_the_first_window_mid_grace_closes_the_card_on_every_screen() => Run((o, decoders) =>
    {
        o.Scheduler.Trigger(userInitiated: true);
        OnOtherScreen(o.Floaters[0].Surface);
        Assert.True(o.TryGracePause(fromPanicKey: false));
        var card = o.Floaters[0].Surface.GraceHost;
        Assert.True(card?.IsVisible, "the other screen has its own card");
        Assert.NotNull(o.Surfaces[0].GraceHost);

        o.Surfaces[0].Buttons.Close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(o.GracePaused);
        Assert.False(card!.IsVisible, "no Resume card is left behind");
        Assert.Null(o.Floaters[0].Surface.GraceHost);
        Assert.False(o.Floaters[0].Surface.Grace.IsVisible);
        Assert.False(decoders[1].Paused, "the joined window plays on");
    });

    // WPF FloatingInstances.cs:534-535: a veiled player stays silent; LibVLC chooses its audio track only after Play.
    [Fact]
    public void A_silenced_player_drops_an_audio_track_chosen_after_the_mute()
    {
        var track = -1;
        var focus = new AudioTrackFocus(() => track, id => { track = id; return true; }, () => new[] { -1, 2 });
        focus.Set(true);   // before Play: no track yet
        track = 2;         // LibVLC selects the clip's track
        focus.Selected();
        Assert.Equal(-1, track);
        focus.Set(false);  // it takes the sound
        Assert.Equal(2, track);
        focus.Selected();
        Assert.Equal(2, track);
    }
}
