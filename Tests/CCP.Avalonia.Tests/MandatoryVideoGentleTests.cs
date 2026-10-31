using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Threading;
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
