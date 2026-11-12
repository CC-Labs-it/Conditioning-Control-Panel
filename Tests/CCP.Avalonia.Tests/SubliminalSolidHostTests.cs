using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalSolidHostTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Hosted_card_fades_holds_and_retires(bool gentle) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var schedule = SubliminalOverlay.ScheduleHosted;
        var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        settings.Current.GentleVisuals = gentle;
        settings.Current.GentleFadeSeconds = 0.75;
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => settings;
        var clock = new Clock();
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        SubliminalOverlay.ScheduleHosted = clock.Schedule;
        try
        {
            var hold = TimeSpan.FromMilliseconds(500);
            var fade = SubliminalOverlayWindow.FadeDuration;
            Assert.Equal(TimeSpan.FromMilliseconds(gentle ? 750 : 50), fade);
            var steps = Math.Max(3, (int)Math.Ceiling(fade.TotalMilliseconds / 17));
            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, hold));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var card = Assert.Single(canvas.Children);
            Assert.Equal(0, card.Opacity);
            clock.AdvanceTo(fade / steps);
            Assert.Equal(0.8 / steps, card.Opacity, 6);
            clock.AdvanceTo(fade);
            Assert.Equal(0.8, card.Opacity, 3);
            clock.AdvanceTo(fade + hold);
            Assert.Equal(0.8, card.Opacity, 3);
            Assert.Single(canvas.Children);
            clock.AdvanceTo(fade + hold + fade * (steps - 1) / steps);
            Assert.Equal(0.8 / steps, card.Opacity, 6);
            Assert.Single(canvas.Children);
            clock.AdvanceTo(fade + hold + fade);
            Assert.Empty(canvas.Children);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            SharedOverlayHost.Configure = configure;
            SubliminalOverlay.ScheduleHosted = schedule;
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
        }
    });

    private sealed class Clock
    {
        private readonly List<(TimeSpan Due, Action Run)> _steps = new();
        private TimeSpan _now;
        internal void Schedule(Action action, TimeSpan delay) => _steps.Add((_now + delay, action));
        internal void AdvanceTo(TimeSpan target)
        {
            while (_steps.OrderBy(step => step.Due).FirstOrDefault() is var next && next.Run != null && next.Due <= target)
            {
                _steps.Remove(next);
                _now = next.Due;
                next.Run();
            }
            _now = target;
        }
    }

    [Fact]
    public System.Threading.Tasks.Task Hosted_card_reaches_full_opacity_on_the_dispatcher() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var settings = new ConditioningControlPanel.Services.SettingsService();
        settings.Current.GentleVisuals = false;
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => settings;
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try
        {
            // A long hold leaves room for a busy dispatcher without changing the expected opacity.
            Assert.True(SubliminalOverlay.ShowHosted(new PixelRect(0, 0, 300, 200), "TEST", 0.8, TimeSpan.FromSeconds(15)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            var card = Assert.Single(canvas.Children);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            while (card.Opacity < 0.8 && watch.Elapsed < TimeSpan.FromSeconds(10))
                await System.Threading.Tasks.Task.Delay(20);
            Assert.Equal(0.8, card.Opacity, 3);
            Assert.Single(canvas.Children);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            SharedOverlayHost.Configure = configure;
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
        }
    });

    [Fact]
    public void Host_is_passive_and_closes_on_release() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try
        {
            var lease = SharedOverlayHost.Acquire(new PixelRect(0, 0, 300, 200))!;
            Assert.EndsWith(".Overlays", stage!.GetType().Namespace); // PanicCheck must count the live stage.
            Assert.False(stage.ShowActivated);
            Assert.False(stage.IsHitTestVisible);
            Assert.True(stage.IsVisible);
            lease.Dispose(); lease.Dispose();
            Assert.False(stage.IsVisible);
        }
        finally { SharedOverlayHost.Configure = configure; }
    });

    // WPF SubliminalService: solid mode uses the shared stage; focus requests and a live mandatory video
    // keep the classic cards. Show itself exits headless (no X11), so the decision it calls is pinned here.
    [Theory]
    [InlineData(true, false, 0, true)]
    [InlineData(false, false, 0, false)]
    [InlineData(true, true, 0, false)]
    [InlineData(true, false, 1, false)]
    public void Show_routes_solid_mode_to_the_shared_stage_only_without_focus_or_video(bool solid, bool focus, int videos, bool hosted) =>
        Assert.Equal(hosted, SubliminalOverlay.UseSolidHost(solid, focus, videos));

    [Fact]
    public void Hosted_card_is_removed_by_stop_and_native_failure_allows_fallback() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        Window? stage = null;
        var bounds = new PixelRect(0, 0, 300, 200);
        var raise = SharedOverlayHost.Raise; var raised = new List<Window>();
        SharedOverlayHost.Raise = w => { raised.Add(w); return true; };
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; return true; };
        try
        {
            Assert.True(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(1)));
            var canvas = Assert.IsType<Grid>(stage!.Content);
            Assert.Single(canvas.Children);
            var first = stage; var other = new Window(); other.Show(); raised.Clear();
            Assert.True(SubliminalOverlay.ShowHosted(bounds, "AGAIN", 0.8, TimeSpan.FromSeconds(1)));
            Assert.Same(first, Assert.Single(raised));
            other.Close();
            SubliminalOverlay.CloseAll(); // existing engine/panic surface owns the host lease
            Assert.Empty(canvas.Children);
            Assert.False(stage.IsVisible);
            SharedOverlayHost.Configure = (_, _) => false;
            Assert.False(SubliminalOverlay.ShowHosted(bounds, "TEST", 0.8, TimeSpan.FromSeconds(1)));
        }
        finally { SubliminalOverlay.CloseAll(); SharedOverlayHost.Configure = configure; SharedOverlayHost.Raise = raise; }
    });
}
