using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalSharedStageTests
{
    private static readonly PixelRect Bounds = new(0, 0, 300, 200);

    private static Screen ScreenAt(PixelRect bounds)
    {
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        return screen;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Tint_and_card_share_a_stage_and_stop_independently(bool tintFirst) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var transparency = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var available = PinkFilterOverlay.IsAvailable;
        var query = ScreenList.QueryOverride;
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        var stages = new List<Window>();
        var owner = new Window();
        try
        {
            CoreSettings.ServiceProvider = () => service;
            SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
            SharedOverlayHost.IsTransparent = _ => true;
            SharedOverlayHost.Raise = _ => true;
            PinkFilterOverlay.IsAvailable = () => true;
            ScreenList.QueryOverride = _ => new[] { ScreenAt(Bounds) };
            var settings = service.Current;
            settings.PinkFilterEnabled = true;
            settings.PinkFilterColor = "#112233";
            settings.PinkFilterOpacity = 40;
            settings.PinkFilterTargetMonitor = 0;
            owner.Show();
            if (tintFirst) PinkFilterOverlay.Refresh(owner);
            Assert.True(SubliminalOverlay.ShowHosted(Bounds, "TEST", 0.8, TimeSpan.FromSeconds(5)));
            if (!tintFirst) PinkFilterOverlay.Refresh(owner);
            var stage = Assert.Single(stages);
            var canvas = Assert.IsType<Grid>(stage.Content);
            var card = Assert.Single(canvas.Children.OfType<Grid>());
            var tint = Assert.Single(canvas.Children.OfType<Border>());
            Assert.True(PinkFilterOverlay.IsShowing);
            Assert.True(PinkFilterOverlay.QuestMinutes.TickRunning);
            Assert.Equal(CompositorLayers.Subliminal, card.ZIndex);
            Assert.Equal(CompositorLayers.PinkTint, tint.ZIndex);
            Assert.True(tint.ZIndex > card.ZIndex);
            Assert.Equal(Color.FromArgb(102, 0x11, 0x22, 0x33), Assert.IsType<SolidColorBrush>(tint.Background).Color);

            settings.PinkFilterColor = "#445566";
            settings.PinkFilterOpacity = 20;
            PinkFilterOverlay.Refresh(owner);
            Assert.Same(stage, Assert.Single(stages));
            Assert.Equal(Color.FromArgb(51, 0x44, 0x55, 0x66), Assert.IsType<SolidColorBrush>(tint.Background).Color);

            if (tintFirst)
            {
                PinkFilterOverlay.CloseAll();
                Assert.False(PinkFilterOverlay.IsShowing);
                Assert.True(stage.IsVisible);
                Assert.Same(card, Assert.Single(canvas.Children));
                SubliminalOverlay.CloseAll();
            }
            else
            {
                SubliminalOverlay.CloseAll();
                Assert.True(stage.IsVisible);
                Assert.Same(tint, Assert.Single(canvas.Children));
                Assert.True(PinkFilterOverlay.QuestMinutes.TickRunning);
                PinkFilterOverlay.CloseAll();
            }
            Assert.False(stage.IsVisible);
            Assert.False(PinkFilterOverlay.QuestMinutes.TickRunning);
        }
        finally
        {
            try
            {
                PinkFilterOverlay.CloseAll();
                SubliminalOverlay.CloseAll();
                owner.Close();
            }
            finally
            {
                SharedOverlayHost.Configure = configure;
                SharedOverlayHost.IsTransparent = transparency;
                SharedOverlayHost.Raise = raise;
                PinkFilterOverlay.IsAvailable = available;
                ScreenList.QueryOverride = query;
                CoreSettings.ServiceProvider = provider;
            }
        }
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public void Tint_scenario_leaves_the_callers_settings_untouched(bool tintFirst, bool fallback) => AvaloniaTestDispatcher.Run(() =>
    {
        var provider = CoreSettings.ServiceProvider;
        var service = fallback ? null : new SettingsService();
        CoreSettings.ServiceProvider = service is null ? null : () => service;
        var settings = CoreSettings.Current;
        var before = (settings.PinkFilterEnabled, settings.PinkFilterColor, settings.PinkFilterOpacity, settings.PinkFilterTargetMonitor);
        try
        {
            settings.PinkFilterEnabled = false;
            settings.PinkFilterColor = "#abcdef";
            settings.PinkFilterOpacity = 7;
            settings.PinkFilterTargetMonitor = -1;
            var caller = CoreSettings.ServiceProvider;
            var expected = (settings.PinkFilterEnabled, settings.PinkFilterColor, settings.PinkFilterOpacity, settings.PinkFilterTargetMonitor);
            Tint_and_card_share_a_stage_and_stop_independently(tintFirst);
            Assert.Same(caller, CoreSettings.ServiceProvider);
            Assert.Same(settings, CoreSettings.Current);
            Assert.Equal(expected, (settings.PinkFilterEnabled, settings.PinkFilterColor, settings.PinkFilterOpacity, settings.PinkFilterTargetMonitor));
        }
        finally
        {
            try { (settings.PinkFilterEnabled, settings.PinkFilterColor, settings.PinkFilterOpacity, settings.PinkFilterTargetMonitor) = before; }
            finally { CoreSettings.ServiceProvider = provider; }
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Shared_stage_stays_below_an_open_chaos_hud(bool tintFirst) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var transparency = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var schedule = SubliminalOverlay.Schedule;
        var stack = new List<Window>();
        var timers = new List<(Action Run, TimeSpan At)>();
        Window? stage = null;
        ChaosHudWindow? hud = null;
        SharedOverlayHost.Configure = (w, _) => { stage = w; w.Width = 300; w.Height = 200; stack.Add(w); return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Raise = w => { stack.Remove(w); stack.Add(w); return true; };
        SubliminalOverlay.Schedule = (run, at) => timers.Add((run, at));
        try
        {
            if (tintFirst) Assert.True(PinkFilterOverlay.ShowHosted(Bounds, 1, 2, 3, 0.4));
            else Assert.True(SubliminalOverlay.ShowHosted(Bounds, "FIRST", 0.8, TimeSpan.FromMilliseconds(100)));
            hud = new ChaosHudWindow { Topmost = true };
            hud.Show();
            stack.Add(hud);
            if (tintFirst) Assert.True(SubliminalOverlay.ShowHosted(Bounds, "TEST", 0.8, TimeSpan.FromMilliseconds(100)));
            else Assert.True(PinkFilterOverlay.ShowHosted(Bounds, 1, 2, 3, 0.4));
            Assert.Equal(new Window[] { stage!, hud }, stack);
            foreach (var (run, _) in timers.OrderBy(t => t.At).ToArray()) run();
            Assert.True(stage!.IsVisible);
            Assert.True(hud.IsVisible);
            Assert.Single(Assert.IsType<Grid>(stage.Content).Children);
            Assert.Equal(new Window[] { stage, hud }, stack);
        }
        finally
        {
            try { PinkFilterOverlay.CloseAll(); SubliminalOverlay.CloseAll(); hud?.Close(); }
            finally
            {
                SharedOverlayHost.Configure = configure;
                SharedOverlayHost.IsTransparent = transparency;
                SharedOverlayHost.Raise = raise;
                SubliminalOverlay.Schedule = schedule;
            }
        }
    });

    [Fact]
    public void Opaque_stage_is_closed_and_both_consumers_can_fall_back() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var transparency = SharedOverlayHost.IsTransparent;
        var stages = new List<Window>();
        SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => false;
        try
        {
            Assert.False(PinkFilterOverlay.ShowHosted(Bounds, 1, 2, 3, 0.4));
            Assert.False(SubliminalOverlay.ShowHosted(Bounds, "TEST", 0.8, TimeSpan.FromSeconds(1)));
            Assert.Equal(2, stages.Count);
            Assert.All(stages, w => Assert.False(w.IsVisible));
            Assert.False(PinkFilterOverlay.IsShowing);
        }
        finally
        {
            PinkFilterOverlay.CloseAll();
            SubliminalOverlay.CloseAll();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparency;
        }
    });

    [Fact]
    public void Card_reopens_a_stage_closed_outside_its_lease() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var transparency = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var stages = new List<Window>();
        SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Raise = _ => true;
        try
        {
            Assert.True(SubliminalOverlay.ShowHosted(Bounds, "FIRST", 0.8, TimeSpan.FromSeconds(5)));
            stages[0].Close();
            Assert.True(SubliminalOverlay.ShowHosted(Bounds, "SECOND", 0.8, TimeSpan.FromSeconds(5)));
            Assert.Equal(2, stages.Count);
            Assert.True(stages[1].IsVisible);
            Assert.Single(Assert.IsType<Grid>(stages[1].Content).Children);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparency;
            SharedOverlayHost.Raise = raise;
        }
    });

    [Fact]
    public void Screens_are_independent_and_a_closed_stage_can_be_recreated() => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var configure = SharedOverlayHost.Configure;
        var transparency = SharedOverlayHost.IsTransparent;
        var stages = new List<Window>();
        SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Lease? first = null, second = null, replacement = null;
        try
        {
            first = SharedOverlayHost.Acquire(Bounds)!;
            second = SharedOverlayHost.Acquire(new PixelRect(300, 0, 300, 200))!;
            Assert.Equal(2, stages.Count);
            Assert.NotSame(first.Canvas, second.Canvas);
            stages[0].Close();
            replacement = SharedOverlayHost.Acquire(Bounds)!;
            Assert.Equal(3, stages.Count);
            first.Dispose(); // an old lease must not close a replacement
            Assert.True(stages[2].IsVisible);
            second.Dispose();
            Assert.False(stages[1].IsVisible);
            Assert.True(stages[2].IsVisible);
            replacement.Dispose();
            Assert.False(stages[2].IsVisible);
        }
        finally
        {
            first?.Dispose(); second?.Dispose(); replacement?.Dispose();
            foreach (var stage in stages) if (stage.IsVisible) stage.Close();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparency;
        }
    });
}
