using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
using System.Linq;
using ConditioningControlPanel.Services;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class MandatoryVideoGentleTests
{
    [Fact]
    public async Task Entrance_fades_from_black_and_only_natural_exit_lifts_a_curtain()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var settings = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = true;
            settings.Current.GentleFadeSeconds = 0.75;
            settings.Current.GentleSpeed = 0.25;
            var o = MandatoryVideoOverlay.Instance;
            var previousClickThrough = o.MakeGentleClickThrough;
            o.MakeGentleClickThrough = _ => true;
            var panel = new Panel();
            var window = new Window { Content = panel, Width = 200, Height = 100 };
            window.Show();
            try
            {
                o.CoverGentleEntrance(panel);
                var cover = Assert.IsType<Border>(Assert.Single(panel.Children));
                Assert.Equal(Brushes.Black, cover.Background);
                Assert.Equal(1, cover.Opacity);
                Assert.False(cover.IsHitTestVisible);
                o.RevealGentleEntrance();
                var fade = Assert.IsType<DoubleTransition>(Assert.Single(cover.Transitions!));
                Assert.Equal(TimeSpan.FromSeconds(0.75), fade.Duration);
                // Transitions interpolate the effective value; an unattached cover exposes the target.
                var detached = new Panel();
                o.CoverGentleEntrance(detached);
                o.RevealGentleEntrance();
                Assert.Equal(0, detached.Children[0].Opacity);
                o.CloseGentleSurface(window, naturalEnd: true);
                Assert.False(window.IsVisible);
                var curtain = Assert.Single(o.ExitCurtains);
                Assert.NotSame(window, curtain);
                Assert.True(curtain.IsVisible);
                Assert.IsType<Border>(curtain.Content);
                Assert.Equal(Brushes.Transparent, curtain.Background);
                Assert.Contains(WindowTransparencyLevel.Transparent, curtain.TransparencyLevelHint);
                Assert.False(curtain.IsHitTestVisible);
                Assert.False(curtain.ShowActivated);
                Assert.False(curtain.Focusable);
                Assert.Equal(TimeSpan.FromSeconds(0.75), Assert.IsType<DoubleTransition>(Assert.Single(curtain.Transitions!)).Duration);
                o.CloseAll();   // panic or engine stop also drops an already fading exit
                Assert.False(window.IsVisible);

                var forced = new Window();
                forced.Show();
                o.CloseGentleSurface(forced, naturalEnd: false);
                Assert.False(forced.IsVisible);
                settings.Current.GentleVisuals = false;
                var plain = new Panel();
                o.CoverGentleEntrance(plain);
                Assert.Empty(plain.Children);
                var off = new Window();
                off.Show();
                o.CloseGentleSurface(off, naturalEnd: true);
                Assert.False(off.IsVisible);
            }
            finally { o.CloseAll(); window.Close(); o.MakeGentleClickThrough = previousClickThrough; }
            return Task.CompletedTask;
        });
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_fading_curtain_cannot_end_the_next_clip_or_veto_its_own_close(bool strict)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            EnsureApp();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = true;
            settings.Current.GentleFadeSeconds = 0.25;
            settings.Current.AttentionChecksEnabled = false;
            var o = MandatoryVideoOverlay.Instance;
            var oldScheduler = o.Scheduler;
            var oldClickThrough = o.MakeGentleClickThrough;
            o.MakeGentleClickThrough = _ => true;
            o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), () => Array.Empty<string>());
            try
            {
                Assert.True(o.Scheduler.Trigger(strict, "/test/first.mp4"));
                var first = o.Build(null, strict);
                ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(first);
                o.FirstFrameMs = 0;
                o.OnNaturalVideoEnd();
                Assert.False(o.Scheduler.IsPlaying);
                var curtain = Assert.Single(o.ExitCurtains);
                Assert.True(curtain.IsVisible);

                Assert.True(o.Scheduler.Trigger(strict, "/test/next.mp4"));
                var next = o.Build(null, strict);
                ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(next);
                await MandatoryVideoOverlayTests.Until(() => o.ExitCurtains.Count == 0);
                Assert.False(curtain.IsVisible);   // strict must not veto the timer's close
                Assert.Empty(o.ExitCurtains);
                Assert.True(o.Scheduler.IsPlaying); // non-strict must not dismiss the next clip
                Assert.True(next.Window.IsVisible);
                Assert.False(first.Window.IsVisible);
            }
            finally
            {
                o.Scheduler.ForceCleanup();
                o.CloseAll();
                o.Scheduler = oldScheduler;
                o.MakeGentleClickThrough = oldClickThrough;
            }
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unsupported_or_failed_native_click_through_closes_the_curtain_immediately(bool throws)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = true;
            var o = MandatoryVideoOverlay.Instance;
            var previous = o.MakeGentleClickThrough;
            Window? attempted = null;
            o.MakeGentleClickThrough = w =>
            {
                attempted = w;
                Assert.True(w.IsVisible); // the native handle is ready before the call
                if (throws) throw new PlatformNotSupportedException();
                return false;
            };
            var video = new Window { Width = 200, Height = 100 };
            video.Show();
            try
            {
                o.CloseGentleSurface(video, naturalEnd: true);
                Assert.NotNull(attempted);
                Assert.False(attempted!.IsVisible);
                Assert.False(video.IsVisible);
                Assert.Empty(o.ExitCurtains);
            }
            finally { o.CloseAll(); video.Close(); o.MakeGentleClickThrough = previous; }
            return Task.CompletedTask;
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_end_cancels_its_entrance_and_the_next_clip_gets_a_fresh_backstop(bool rendered)
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = true;
            settings.Current.AttentionChecksEnabled = false;
            var o = MandatoryVideoOverlay.Instance;
            var old = (o.Scheduler, o.MakeGentleClickThrough, o.ArmGentleBackstop);
            var timers = new List<Backstop>();
            o.ArmGentleBackstop = (fire, delay) => { var t = new Backstop(fire, delay); timers.Add(t); return t; };
            o.MakeGentleClickThrough = _ => true;
            o.Scheduler = new MandatoryVideoScheduler(o, new StillTime(), () => Array.Empty<string>());
            try
            {
                Assert.True(o.Scheduler.Trigger(path: "/test/first.mp4"));
                var first = o.Build(null, false);
                ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(first);
                o.FirstFrameMs = rendered ? 0 : -1;
                o.OnNaturalVideoEnd();
                Assert.Equal(rendered ? 1 : 0, o.ExitCurtains.Count);
                Assert.True(Assert.Single(timers).Disposed);
                var panel = new Panel();
                o.CoverGentleEntrance(panel);
                Assert.Equal(2, timers.Count);
                Assert.All(timers, t => Assert.Equal(TimeSpan.FromSeconds(3), t.Delay));
                timers[0].Fire();
                Assert.Equal(1, Assert.Single(panel.Children).Opacity);
                timers[1].Fire();
                Assert.Equal(0, Assert.Single(panel.Children).Opacity);
            }
            finally
            {
                o.Scheduler.ForceCleanup(); o.CloseAll();
                (o.Scheduler, o.MakeGentleClickThrough, o.ArmGentleBackstop) = old;
            }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Attention_fail_and_mercy_close_without_a_soft_exit()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = settings.Current.AttentionChecksEnabled = true;
            settings.Current.MercySystemEnabled = true;
            settings.Current.MercyAfterFails = 2;
            var o = MandatoryVideoOverlay.Instance;
            var old = (o.Scheduler, o.MakeGentleClickThrough);
            var host = new VerdictHost(o);
            var covers = 0;
            o.MakeGentleClickThrough = _ => { covers++; return true; };
            o.Scheduler = new MandatoryVideoScheduler(host, new StillTime(), () => Array.Empty<string>());
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    Assert.True(o.Scheduler.Trigger(path: "/test/fail.mp4"));
                    var surface = o.Build(null, false);
                    ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(surface);
                    o.FirstFrameMs = 0;
                    o.Scheduler.NoteSpawn();
                    o.OnNaturalVideoEnd();
                    Assert.False(surface.Window.IsVisible);
                    Assert.Empty(o.ExitCurtains);
                }
                Assert.Equal(new[] { AttentionVerdict.Fail, AttentionVerdict.Mercy }, host.Verdicts);
                Assert.Equal(0, covers);
            }
            finally { o.Scheduler.ForceCleanup(); o.CloseAll(); (o.Scheduler, o.MakeGentleClickThrough) = old; }
            return Task.CompletedTask;
        });
    }

    private sealed class Backstop(Action fire, TimeSpan delay) : IDisposable
    {
        public bool Disposed;
        public TimeSpan Delay => delay;
        public void Fire() { if (!Disposed) fire(); }
        public void Dispose() => Disposed = true;
    }

    private sealed class VerdictHost(MandatoryVideoOverlay overlay) : IMandatoryVideoHost
    {
        public readonly List<AttentionVerdict> Verdicts = new();
        public void Show(string path, bool strict) { }
        public double CloseAll() => overlay.CloseAll();
        public void ShowMessage(AttentionVerdict verdict, int ms, Action then) => Verdicts.Add(verdict);
    }

    [Fact]
    public async Task Playing_reveals_before_the_first_frame_and_ignores_stale_players()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = true;
            var o = MandatoryVideoOverlay.Instance;
            var old = (o.ArmGentleBackstop, o.FirstFrameMs);
            var timers = new List<Backstop>();
            o.ArmGentleBackstop = (fire, delay) => { var t = new Backstop(fire, delay); timers.Add(t); return t; };
            try
            {
                o.FirstFrameMs = -1;
                var panel = new Panel();
                o.CoverGentleEntrance(panel);
                var cover = Assert.Single(panel.Children);
                var timer = Assert.Single(timers);
                Assert.Equal(TimeSpan.FromSeconds(3), timer.Delay);
                Assert.Equal(1, cover.Opacity);
                Assert.False(o.OnPlayerPlaying(current: false));
                Assert.False(timer.Disposed);
                Assert.Equal(1, cover.Opacity);
                Assert.True(o.OnPlayerPlaying(current: true));
                Assert.Equal(-1, o.FirstFrameMs);
                Assert.True(timer.Disposed);
                Assert.Equal(0, cover.Opacity);
                var fallback = new Panel();
                o.CoverGentleEntrance(fallback);
                Assert.Equal(2, timers.Count);
                timers[1].Fire();
                Assert.Equal(0, Assert.Single(fallback.Children).Opacity);
            }
            finally { o.CloseAll(); (o.ArmGentleBackstop, o.FirstFrameMs) = old; }
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Grace_attention_and_controls_stay_above_the_opaque_entrance_cover()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            EnsureApp();
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;
            settings.Current.GentleVisuals = true;
            var o = MandatoryVideoOverlay.Instance;
            var surface = o.Build(null, false);
            ((List<MandatoryVideoOverlay.Surface>)o.Surfaces).Add(surface);
            try
            {
                var panel = Assert.IsType<Panel>(surface.Window.Content);
                var cover = Assert.Single(panel.Children.OfType<Border>(), b => ReferenceEquals(b.Background, Brushes.Black));
                Assert.Equal(1, cover.Opacity); // paused before a first frame: the entrance is still black
                surface.Grace.IsVisible = true;
                surface.Layer.Children.Add(new Border { Width = 20, Height = 20, Background = Brushes.Magenta });
                var coverIndex = panel.Children.IndexOf(cover);
                Assert.True(coverIndex < panel.Children.IndexOf(surface.Grace), "the grace card must draw above the black cover");
                Assert.True(coverIndex < panel.Children.IndexOf(surface.Layer), "attention targets must draw above the black cover");
                Assert.True(coverIndex < panel.Children.IndexOf(surface.Buttons.Row));
                Assert.True(panel.Children.IndexOf(surface.Video) < coverIndex);
            }
            finally { o.CloseAll(); }
            return Task.CompletedTask;
        });
    }

    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
    }

    private sealed class StillTime : TimeProvider
    {
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan due, TimeSpan period) => new Never();
        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan due, TimeSpan period) => true;
            public void Dispose() { }
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

}
