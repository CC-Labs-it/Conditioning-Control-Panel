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
using ConditioningControlPanel.Avalonia.Views.Overlays;
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
        var stages = new List<Window>();
        SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Raise = _ => true;
        PinkFilterOverlay.IsAvailable = () => true;
        ScreenList.QueryOverride = _ => new[] { ScreenAt(Bounds) };
        var settings = CoreSettings.Current;
        settings.PinkFilterEnabled = true;
        settings.PinkFilterColor = "#112233";
        settings.PinkFilterOpacity = 40;
        settings.PinkFilterTargetMonitor = 0;
        var owner = new Window();
        owner.Show();
        try
        {
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
            PinkFilterOverlay.CloseAll();
            SubliminalOverlay.CloseAll();
            owner.Close();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparency;
            SharedOverlayHost.Raise = raise;
            PinkFilterOverlay.IsAvailable = available;
            ScreenList.QueryOverride = query;
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
