using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class GentleWindowsTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly SettingsService _settings = new();
    public GentleWindowsTests() => CoreSettings.ServiceProvider = () => _settings;
    public void Dispose() => CoreSettings.ServiceProvider = _provider;
    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public void Gentle_bolt_starts_dark_and_rises_without_rejitter() => AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var saved = (s.GentleVisuals, s.GentleFadeSeconds);
        var can = ChaosEStimOverlay.CanShow;
        var clock = new Clock();
        try
        {
            s.GentleVisuals = true; s.GentleFadeSeconds = 0.75;
            ChaosEStimOverlay.CanShow = () => true;
            ChaosEStimOverlay.SkipPlatformChecksForTests = true;
            ChaosEStimOverlay.Clock = clock;
            ChaosEStimOverlay.Strike(new[] { (new PixelPoint(10, 10), new PixelPoint(200, 100)) });
            var w = ChaosEStimOverlay.Active!;
            Assert.Equal(0, w.StrikeAlpha);
            var path = ((Canvas)w.Content!).Children.OfType<global::Avalonia.Controls.Shapes.Polyline>().First().Points.ToArray();
            clock.Ms = 150; w.Step();
            Assert.Equal(0.425, w.StrikeAlpha, 6);
            Assert.Equal(path, ((Canvas)w.Content!).Children.OfType<global::Avalonia.Controls.Shapes.Polyline>().First().Points.ToArray());
            clock.Ms = 420; w.Step(); Assert.Equal(0.85, w.StrikeAlpha, 6);
            clock.Ms = 1170; w.Step(); Assert.False(w.IsVisible);
        }
        finally
        {
            ChaosEStimOverlay.CloseActive(); ChaosEStimOverlay.CanShow = can;
            ChaosEStimOverlay.SkipPlatformChecksForTests = false; ChaosEStimOverlay.Clock = TimeProvider.System;
            (s.GentleVisuals, s.GentleFadeSeconds) = saved;
        }
    });

    [Fact]
    public Task Solved_card_fades_but_panic_closes_immediately_and_old_fade_cannot_close_next_card() =>
        AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        var s = CoreSettings.Current;
        var saved = (s.GentleVisuals, s.GentleFadeSeconds);
        try
        {
            s.GentleVisuals = true; s.GentleFadeSeconds = 0.25;
            LockCardWindow.ShowOnAllMonitors("ok", 1, false, isTest: true);
            var card = LockCardWindow.Primary!;
            Assert.Equal(0, card.Opacity);
            card.KeyTextInput("ok");
            Dispatcher.UIThread.RunJobs();
            Assert.True(card.IsCompleted);
            card.DismissSolved();
            Assert.True(card.IsVisible);
            LockCardWindow.ForceCloseAll();
            Assert.False(card.IsVisible);
            LockCardWindow.ShowOnAllMonitors("next", 1, false, isTest: true);
            var next = LockCardWindow.Primary!;
            await Task.Delay(400);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.True(next.IsVisible);
            Assert.Equal(1, next.Opacity);
            Assert.Same(next, LockCardWindow.Primary);
            next.KeyTextInput("next");
            Dispatcher.UIThread.RunJobs();
            Assert.True(next.IsCompleted);
            next.DismissSolved();
            Assert.True(next.IsVisible);
            await Task.Delay(300);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            Assert.False(next.IsVisible);
        }
        finally { LockCardWindow.ForceCloseAll(); (s.GentleVisuals, s.GentleFadeSeconds) = saved; }
    });

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 0)]
    public void Corner_burst_is_suppressed_without_losing_corner_event(bool gentle, int expected) =>
        AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var s = CoreSettings.Current;
        var saved = (s.GentleVisuals, s.BouncingTextFxCornerBurst);
        var (burst, corner) = (BouncingTextOverlay.Burst, BouncingTextOverlay.CornerHit);
        int bursts = 0, corners = 0;
        try
        {
            s.GentleVisuals = gentle; s.BouncingTextFxCornerBurst = true;
            BouncingTextOverlay.Burst = (_, _, _) => bursts++;
            BouncingTextOverlay.CornerHit = () => corners++;
            var e = BouncingTextOverlay.Engine;
            e.SetBounds(0, 0, 1000, 800); e.Measure = (_, _) => (200, 60); e.Start(s);
            var l = e.Logos[0];
            (l.PosX, l.PosY, l.VelX, l.VelY) = (2, 2, -100, -100);
            BouncingTextOverlay.Advance(0.05);
            Assert.Equal(expected, bursts); Assert.Equal(1, corners);
        }
        finally
        {
            BouncingTextOverlay.Stop(); (BouncingTextOverlay.Burst, BouncingTextOverlay.CornerHit) = (burst, corner);
            (s.GentleVisuals, s.BouncingTextFxCornerBurst) = saved;
        }
    });

    [Fact]
    public void Video_curtain_survives_natural_teardown_but_idle_engine_stop_and_panic_drop_it() =>
        AvaloniaTestDispatcher.Run(() =>
    {
        Setup();
        var host = MandatoryVideoOverlay.Instance;
        var accept = MandatoryVideoOverlay.AcceptCurtain;
        var s = CoreSettings.Current;
        var saved = (s.GentleVisuals, s.GentleFadeSeconds);
        try
        {
            s.GentleVisuals = true; s.GentleFadeSeconds = 2;
            MandatoryVideoOverlay.AcceptCurtain = _ => true;
            var w = new Window(); w.Show();
            host.FadeDesktopBack(w);
            Assert.True(w.IsVisible);
            Assert.False(w.IsHitTestVisible);
            host.Scheduler.Stop();
            Assert.False(w.IsVisible);
            var second = new Window(); second.Show();
            host.FadeDesktopBack(second);
            PanicSurfaces.All.Single(x => x.Id == "video-fades").Stop(null);
            Assert.False(second.IsVisible);
            MandatoryVideoOverlay.AcceptCurtain = _ => false;
            var refused = new Window(); refused.Show();
            host.FadeDesktopBack(refused);
            Assert.False(refused.IsVisible); // no compositor/click-through: no black cover is retained
        }
        finally
        {
            host.DropGentleFades(); MandatoryVideoOverlay.AcceptCurtain = accept;
            (s.GentleVisuals, s.GentleFadeSeconds) = saved;
        }
    });

    private sealed class Clock : TimeProvider
    {
        public int Ms;
        public override long GetTimestamp() => TimeSpan.FromMilliseconds(Ms).Ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
