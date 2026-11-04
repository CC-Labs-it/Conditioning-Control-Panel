using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Automation;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.Haptics.Core;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Controls;
using IconKind = FluentIcons.Common.Symbol;
using ConditioningControlPanel.Services.Fyp.Online;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoRemoteTests
{
    [Fact]
    public void Playback_rechecks_the_cache_and_never_streams_an_evicted_scrolller_clip()
    {
        Assert.Null(MandatoryVideoOverlay.PlaybackPath("https://v.scrolller.com/never-cached-test.mp4"));
        Assert.Equal("/cache.mp4", MandatoryVideoOverlay.PlaybackPath("https://v.scrolller.com/cached.mp4", _ => "/cache.mp4"));
        Assert.Equal("/local.mp4", MandatoryVideoOverlay.PlaybackPath("/local.mp4"));
        Assert.Equal("https://example.com/clip.mp4", MandatoryVideoOverlay.PlaybackPath("https://example.com/clip.mp4"));
        Assert.True(MandatoryVideoOverlay.Instance.Scheduler.RemoteMedia);
    }
    [Fact]
    public void Scrolller_clip_has_a_credit_mark_but_local_video_does_not() => AvaloniaTestDispatcher.Run(() =>
    {
        Assert.True(MandatoryVideoOverlay.CreditFor("https://v.scrolller.com/clip.mp4").Shown);
        const string cdn = "https://example.com/credited-test.mp4";
        Assert.True(MandatoryVideoOverlay.CreditFor(cdn, _ => new ScrolllerCacheMeta("clip", "test")).Shown);
        Assert.False(MandatoryVideoOverlay.CreditFor("/local.mp4").Shown);
    });
    [Fact]
    public void CreditLinkOpensForFloatingAndWaitsForFullscreenEnd() => AvaloniaTestDispatcher.Run(() =>
    {
        const string path = "https://v.scrolller.com/credit-link-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MandatoryVideoOverlay).GetField("_path", flags)!.SetValue(o, path);
        var floating = typeof(MandatoryVideoOverlay).GetProperty("Floating", flags)!;
        var old = MandatoryVideoOverlay.CreditOpener;
        var opened = new List<string>();
        MandatoryVideoOverlay.CreditOpener = url => { opened.Add(url); return true; };
        try
        {
            CoreSettings.Current.StrictLockEnabled = false;
            Assert.True(scheduler.Trigger(path: path));
            var button = o.CreditButton(path);
            Assert.True(button.IsHitTestVisible);
            Assert.False(o.CreditButton("/local.mp4").IsHitTestVisible);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Assert.Empty(opened);
            o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { "https://scrolller.com/r/test_sub" }, opened);
            o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs(); Assert.Single(opened);
            floating.SetValue(o, true);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); Assert.Equal(2, opened.Count);
            floating.SetValue(o, false);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            scheduler.ForceCleanup(); // panic/stop does not open a queued link
            o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs(); Assert.Equal(2, opened.Count);
        }
        finally { scheduler.ForceCleanup(); MandatoryVideoOverlay.CreditOpener = old; }
    });

    [Fact]
    public void QueuedCreditShowsTickOnEveryScreenAndClearsOnCleanupAndNextClip() => AvaloniaTestDispatcher.Run(() =>
    {
        const string path = "https://v.scrolller.com/credit-tick-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        typeof(MandatoryVideoOverlay).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, path);
        static StackPanel BadgeContent(Button button) => (StackPanel)((Border)button.Content!).Child!;
        static string? Text(Button button) => ((TextBlock)BadgeContent(button).Children[0]).Text;
        static IconGlyph Tick(Button button) => (IconGlyph)BadgeContent(button).Children[1];
        var floating = typeof(MandatoryVideoOverlay).GetProperty("Floating", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var oldOpener = MandatoryVideoOverlay.CreditOpener;
        var opened = 0; MandatoryVideoOverlay.CreditOpener = _ => { opened++; return true; };
        try
        {
            Assert.True(scheduler.Trigger(strictOverride: true, path: path));
            var locked = o.CreditButton(path); o.CreditPressed(path);
            Assert.Equal("via Scrolller", Text(locked)); Assert.False(Tick(locked).IsVisible);
            scheduler.ForceCleanup();
            Assert.True(scheduler.Trigger(strictOverride: false, path: path));
            var first = o.CreditButton(path); var second = o.CreditButton(path);
            Assert.Equal("via Scrolller", Text(first)); Assert.Equal("via Scrolller", Text(second));
            Assert.Equal(IconKind.Checkmark, Tick(first).Kind); Assert.False(Tick(first).IsVisible); Assert.False(Tick(second).IsVisible);
            floating.SetValue(o, true);
            o.CreditPressed(path); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, opened); Assert.Equal("via Scrolller", Text(first)); Assert.False(Tick(first).IsVisible);
            o.CreditPressed("/local.mp4"); // unrelated or uncredited presses do not change the badge
            Assert.Equal("via Scrolller", Text(first));
            floating.SetValue(o, false);
            first.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("via Scrolller", Text(first)); Assert.Equal("via Scrolller", Text(second));
            Assert.True(Tick(first).IsVisible); Assert.True(Tick(second).IsVisible);
            scheduler.ForceCleanup();
            Assert.Equal("via Scrolller", Text(first)); Assert.Equal("via Scrolller", Text(second));
            Assert.False(Tick(first).IsVisible); Assert.False(Tick(second).IsVisible);

            Assert.True(scheduler.Trigger(strictOverride: false, path: path));
            var next = o.CreditButton(path);
            Assert.Equal("via Scrolller", Text(next)); Assert.False(Tick(next).IsVisible);
            next.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("via Scrolller", Text(next)); Assert.True(Tick(next).IsVisible);
            // A new clip whose cache entry has vanished tears down the old run and its feedback.
            o.Show($"https://v.scrolller.com/missing-{Guid.NewGuid():N}.mp4", false);
            Assert.Equal("via Scrolller", Text(next)); Assert.False(Tick(next).IsVisible);
            Assert.False(scheduler.IsPlaying);
        }
        finally { scheduler.ForceCleanup(); MandatoryVideoOverlay.CreditOpener = oldOpener; }
    });

    private static void PrepareBadgeFont(Border mark)
    {
        // Use installed test fonts where the headless app's Inter composite font is unavailable.
        var family = OperatingSystem.IsLinux() ? "DejaVu Sans" : OperatingSystem.IsWindows() ? "Arial" : null;
        if (family != null)
            ((TextBlock)((StackPanel)mark.Child!).Children[0]).FontFamily = new FontFamily(family);
    }

    [Fact]
    public void CreditBadgeUsesBarePresenterAccessibleNameAndHandCursor() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        const string path = "https://v.scrolller.com/credit-style-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        typeof(MandatoryVideoOverlay).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, path);
        var button = o.CreditButton(path); var mark = (Border)button.Content!;
        PrepareBadgeFont(mark);
        var window = new Window { Width = 320, Height = 180, Content = new Panel { Background = Brushes.Black, Children = { button } } };
        try
        {
            Assert.True(scheduler.Trigger(strictOverride: false, path: path));
            window.Show(); Dispatcher.UIThread.RunJobs();
            var presenter = Assert.IsType<ContentPresenter>(Assert.Single(button.GetVisualChildren()));
            Assert.Same(mark, presenter.Content);
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brushes.Transparent, presenter.Background);
            Assert.Equal("via Scrolller", AutomationProperties.GetName(button));
            Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), button.Cursor?.ToString());
            window.MouseDown(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.Equal(Brushes.Transparent, presenter.Background);
            window.MouseUp(point, MouseButton.Left); Dispatcher.UIThread.RunJobs();
            Assert.True(((IconGlyph)((StackPanel)mark.Child!).Children[1]).IsVisible); // real press queues the link
            scheduler.ForceCleanup();
            Assert.True(scheduler.Trigger(strictOverride: true, path: path));
            var locked = o.CreditButton(path);
            Assert.False(locked.IsHitTestVisible); Assert.Null(locked.Cursor);
            Assert.Equal("via Scrolller", AutomationProperties.GetName(locked));
        }
        finally { window.Close(); scheduler.ForceCleanup(); }
    });

    [Fact]
    public void CreditBadgeHasHandCursorOnlyWhenClickable() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        const string path = "https://v.scrolller.com/credit-cursor-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        try
        {
            Assert.Equal(new Cursor(StandardCursorType.Hand).ToString(), o.CreditButton(path).Cursor?.ToString());
            Assert.True(scheduler.Trigger(strictOverride: true, path: path));
            Assert.Null(o.CreditButton(path).Cursor);
        }
        finally { scheduler.ForceCleanup(); }
    });

    [Fact]
    public void CreditBadgeHoverChangesOpacityOnlyWhenClickable() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        const string path = "https://v.scrolller.com/credit-hover-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        var button = o.CreditButton(path); var mark = (Border)button.Content!;
        PrepareBadgeFont(mark);
        var fade = Assert.IsType<DoubleTransition>(Assert.Single(mark.Transitions!));
        Assert.Equal(TimeSpan.FromMilliseconds(ConditioningControlPanel.Services.Safety.GentleVisuals.FadeMs(600)), fade.Duration);
        mark.Transitions = null; mark.Opacity = 0.75;
        var panel = new Panel { Background = Brushes.Black, Children = { button } };
        var window = new Window { Width = 320, Height = 180, Content = panel };
        try
        {
            window.Show(); Dispatcher.UIThread.RunJobs();
            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); Dispatcher.UIThread.RunJobs(); Assert.Equal(1, mark.Opacity);
            window.MouseMove(new Point(1, 1)); Dispatcher.UIThread.RunJobs(); Assert.Equal(0.75, mark.Opacity);
            Assert.True(scheduler.Trigger(strictOverride: true, path: path));
            panel.Children.Clear();
            var locked = o.CreditButton(path); var lockedMark = (Border)locked.Content!;
            PrepareBadgeFont(lockedMark);
            lockedMark.Transitions = null; lockedMark.Opacity = 0.75;
            panel.Children.Add(locked); Dispatcher.UIThread.RunJobs();
            point = locked.TranslatePoint(new Point(locked.Bounds.Width / 2, locked.Bounds.Height / 2), window)!.Value;
            window.MouseMove(point); Dispatcher.UIThread.RunJobs(); Assert.Equal(0.75, lockedMark.Opacity);
        }
        finally { window.Close(); scheduler.ForceCleanup(); }
    });

    [Fact]
    public void CreditBadgeKeepsLongerGentleVisualsFade() => AvaloniaTestDispatcher.Run(() =>
    {
        var old = CoreSettings.ServiceProvider;
        var settings = new SettingsService();
        settings.Current.GentleVisuals = false; settings.Current.GentleFadeSeconds = 2;
        CoreSettings.ServiceProvider = () => settings;
        try
        {
            var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
            var normal = (Border)o.CreditButton("/local.mp4").Content!;
            Assert.Equal(TimeSpan.FromMilliseconds(600), Assert.IsType<DoubleTransition>(Assert.Single(normal.Transitions!)).Duration);
            settings.Current.GentleVisuals = true;
            var mark = (Border)o.CreditButton("/local.mp4").Content!;
            Assert.Equal(TimeSpan.FromSeconds(2), Assert.IsType<DoubleTransition>(Assert.Single(mark.Transitions!)).Duration);
        }
        finally { CoreSettings.ServiceProvider = old; }
    });

    [Fact]
    public void CreditLinkRefusesLocksAtPressAndBeforeDispatch() => AvaloniaTestDispatcher.Run(() =>
    {
        const string path = "https://v.scrolller.com/credit-lock-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var scheduler = new MandatoryVideoScheduler(o, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        typeof(MandatoryVideoOverlay).GetField("_path", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, path);
        var old = MandatoryVideoOverlay.CreditOpener;
        var opened = 0; MandatoryVideoOverlay.CreditOpener = _ => { opened++; return true; };
        var previousLockdown = LockdownService.Current;
        var lockdown = LockdownService.Current = new LockdownService();
        try
        {
            Assert.True(scheduler.Trigger(strictOverride: true, path: path));
            o.CreditPressed(path); o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs(); Assert.Equal(0, opened);
            scheduler.ForceCleanup();
            Assert.True(scheduler.Trigger(strictOverride: false, path: path));
            o.CreditPressed(path);
            lockdown.Activate(TimeSpan.FromMinutes(30));
            o.OpenQueuedCreditLink(); Dispatcher.UIThread.RunJobs(); Assert.Equal(0, opened);
            lockdown.Deactivate();
            o.CreditPressed(path); o.OpenQueuedCreditLink();
            lockdown.Activate(TimeSpan.FromMinutes(30)); // changes after queue release, before dispatch
            Dispatcher.UIThread.RunJobs(); Assert.Equal(0, opened);
        }
        finally { lockdown.Deactivate(); LockdownService.Current = previousLockdown; scheduler.ForceCleanup(); MandatoryVideoOverlay.CreditOpener = old; }
    });

    [Fact]
    public void DurationCapReleasesQueuedCreditLink() => AvaloniaTestDispatcher.Run(() =>
    {
        const string path = "https://v.scrolller.com/credit-cap-test.mp4";
        ScrolllerItemIndex.Remember(path, new ScrolllerCacheMeta("clip", "test_sub"));
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var host = new Host(); var scheduler = new MandatoryVideoScheduler(host, new StillTime(), Array.Empty<string>); o.Scheduler = scheduler;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(MandatoryVideoOverlay).GetField("_path", flags)!.SetValue(o, path);
        typeof(MandatoryVideoOverlay).GetProperty("FirstFrameMs", flags)!.SetValue(o, 0L);
        var surfaces = (List<MandatoryVideoOverlay.Surface>)o.Surfaces;
        surfaces.Add(new(null!, null!, null!, null!, null!, null!, null!, null!));
        var old = MandatoryVideoOverlay.CreditOpener; var oldCap = CoreSettings.Current.VideoMaxDurationSeconds;
        var opened = 0; MandatoryVideoOverlay.CreditOpener = _ => { opened++; return true; };
        try
        {
            CoreSettings.Current.StrictLockEnabled = false; CoreSettings.Current.VideoMaxDurationSeconds = 1;
            Assert.True(scheduler.Trigger(path: path)); o.CreditPressed(path); o.PlayedSkew = 2;
            o.GuardTick(2); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, opened); Assert.False(scheduler.IsPlaying);
        }
        finally { surfaces.Clear(); scheduler.ForceCleanup(); CoreSettings.Current.VideoMaxDurationSeconds = oldCap; MandatoryVideoOverlay.CreditOpener = old; }
    });

    private sealed class Host : IMandatoryVideoHost
    {
        public int Closed;
        public void Show(string path, bool strict) { }
        public double CloseAll() { Closed++; return 0; }
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) { }
    }

    private sealed class StillTime : TimeProvider
    {
        private Never? _timer;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => _timer = new Never(callback, state);
        public void Fire() => _timer!.Fire();
        private sealed class Never(TimerCallback callback, object? state) : ITimer
        {
            public void Fire() => callback(state);
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => default;
        }
    }

    [Fact]
    public void EvictedClipNeverAnnouncesOrStartsHapticsAfterPreRoll() => AvaloniaTestDispatcher.Run(() =>
    {
        using var h = new HapticService(new HapticSettings { VideoEnabled = true, VideoIntensity = 0.8, FunScriptEnabled = true });
        var oldHaptics = CoreHaptics.Service; var oldPost = CoreDispatch.PostProvider;
        CoreHaptics.Service = h; CoreDispatch.PostProvider = action => action();
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var clock = new StillTime(); var v = new MandatoryVideoScheduler(o, clock, Array.Empty<string>); o.Scheduler = v;
        var started = 0; v.VideoStarted += () => started++;
        try
        {
            h.StartVideoBackgroundVibeAsync().GetAwaiter().GetResult();
            Assert.True(h.GetLayer(HapticLayer.Video) > 0); // positive control
            h.StopVideoBackgroundVibeAsync().GetAwaiter().GetResult();
            var seq = typeof(FunScriptService).GetField("_loadSeq", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var before = (long)seq.GetValue(h.FunScript)!;
            Assert.True(v.Trigger(path: $"https://v.scrolller.com/evicted-{Guid.NewGuid():N}.mp4"));
            clock.Fire(); Dispatcher.UIThread.RunJobs();
            Assert.False(v.IsPlaying); Assert.Equal(0, h.GetLayer(HapticLayer.Video));
            Assert.Null(v.LastVideoPath); Assert.Equal(0, started); Assert.False(h.FunScript.IsActive);
            Assert.Equal(before + 1, (long)seq.GetValue(h.FunScript)!); // stop only, no pending script load
        }
        finally { v.ForceCleanup(); CoreHaptics.Service = oldHaptics; CoreDispatch.PostProvider = oldPost; }
    });

    // No native player is needed to exercise GuardTick: the scheduler's frozen pre-roll never fires.
    // The source path is the URL selected by the scheduler, even when playback resolves to a cache file.
    private static void WithRemoteGuard(Action<MandatoryVideoOverlay, MandatoryVideoScheduler, Host> check)
    {
        var o = (MandatoryVideoOverlay)Activator.CreateInstance(typeof(MandatoryVideoOverlay), nonPublic: true)!;
        var fields = BindingFlags.Instance | BindingFlags.NonPublic;
        var path = typeof(MandatoryVideoOverlay).GetField("_path", fields)!;
        var surfaces = (List<MandatoryVideoOverlay.Surface>)o.Surfaces;
        Assert.Empty(surfaces);
        var host = new Host();
        var scheduler = new MandatoryVideoScheduler(host, new StillTime(), Array.Empty<string>);
        const string url = "https://v.scrolller.com/cached.mp4";
        Assert.Equal("/cache.mp4", MandatoryVideoOverlay.PlaybackPath(url, _ => "/cache.mp4"));
        o.Scheduler = scheduler;
        surfaces.Add(new(null!, null!, null!, null!, null!, null!, null!, null!));
        path.SetValue(o, url);
        try
        {
            Assert.True(scheduler.Trigger(path: url));
            scheduler.NoteSpawn();
            check(o, scheduler, host);
        }
        finally
        {
            scheduler.ForceCleanup();
            surfaces.Clear();
        }
    }

    [Fact]
    public void CachedRemoteUrlKeepsItsTwentySecondStartGrace() => AvaloniaTestDispatcher.Run(() =>
        WithRemoteGuard((o, scheduler, host) =>
        {
            var first = typeof(MandatoryVideoOverlay).GetProperty("FirstFrameMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            first.SetValue(o, -1L);
            // Ten seconds of window/player setup precede Play; the watchdog starts at Play.
            typeof(MandatoryVideoOverlay).GetField("_guardStartedAt", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(o, 10d);
            o.GuardTick(18); Assert.True(scheduler.IsPlaying);
            o.GuardTick(29.99); Assert.True(scheduler.IsPlaying);
            o.GuardTick(30); Assert.False(scheduler.IsPlaying);
            Assert.Equal(1, host.Closed);
            Assert.Equal(1, scheduler.AttentionSpawned); // ending does not enter the replay path
        }));

    [Fact]
    public void CachedRemoteUrlEndsWithoutReplayWhenFramesStop() => AvaloniaTestDispatcher.Run(() =>
        WithRemoteGuard((o, scheduler, host) =>
        {
            var first = typeof(MandatoryVideoOverlay).GetProperty("FirstFrameMs", BindingFlags.Instance | BindingFlags.NonPublic)!;
            first.SetValue(o, 0L);
            o.FrameTs = Stopwatch.GetTimestamp() - 6 * Stopwatch.Frequency;
            o.GuardTick(25);
            Assert.False(scheduler.IsPlaying);
            Assert.Equal(1, host.Closed);
            Assert.Equal(1, scheduler.AttentionSpawned); // a replay would reset the checks
        }));

}
