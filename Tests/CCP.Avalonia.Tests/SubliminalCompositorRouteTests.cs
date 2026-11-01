using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Compositor;
using Xunit;

namespace CCP.Avalonia.Tests;

[Collection(RunsAloneCollection.Name)]
public sealed class SubliminalCompositorRouteTests
{
    // WPF SubliminalService.cs:650-661 sends both modes to the same compositor as the tint.
    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, true, true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    public void Show_routes_modes_using_the_renderer_setting(bool solid, bool unified, bool hosted) => AvaloniaTestDispatcher.Run(() =>
    {
        SubliminalSilentHintTests.EnsureAvalonia();
        var bounds = new PixelRect(0, 0, 300, 200);
        var screen = (Screen)Activator.CreateInstance(typeof(PlatformScreen),
            new object[] { new PlatformHandle(IntPtr.Zero, "headless-test") })!;
        typeof(Screen).GetProperty(nameof(Screen.Scaling))!.SetValue(screen, 1.0);
        typeof(Screen).GetProperty(nameof(Screen.Bounds))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.WorkingArea))!.SetValue(screen, bounds);
        typeof(Screen).GetProperty(nameof(Screen.IsPrimary))!.SetValue(screen, true);
        var configure = SharedOverlayHost.Configure;
        var transparent = SharedOverlayHost.IsTransparent;
        var raise = SharedOverlayHost.Raise;
        var available = SubliminalOverlay.IsAvailable;
        var query = ScreenList.QueryOverride;
        var stages = new List<Window>();
        SharedOverlayHost.Configure = (w, _) => { stages.Add(w); w.Width = 300; w.Height = 200; return true; };
        SharedOverlayHost.IsTransparent = _ => true;
        SharedOverlayHost.Raise = _ => true;
        SubliminalOverlay.IsAvailable = () => true;
        ScreenList.QueryOverride = _ => new[] { screen };
        var settings = CoreSettings.Current;
        settings.SubliminalSolidMode = solid;
        settings.UnifiedOverlayHost = unified;
        settings.SubliminalStealsFocus = false;
        settings.SubliminalFont = "DejaVu Sans";
        settings.DualMonitorEnabled = false;
        settings.GlobalTargetMonitor = -1;
        var owner = new Window();
        owner.Show();
        try
        {
            Assert.True(PinkFilterOverlay.ShowHosted(bounds, 1, 2, 3, 0.4));
            SubliminalOverlay.Show(owner, "FIRST");
            var stage = Assert.Single(stages);
            var canvas = Assert.IsType<Grid>(stage.Content);
            var tint = Assert.Single(canvas.Children.OfType<Border>());
            if (!hosted)
            {
                Assert.Empty(canvas.Children.OfType<Grid>());
                return;
            }
            var card = Assert.Single(canvas.Children.OfType<Grid>());
            Assert.Equal(CompositorLayers.Subliminal, card.ZIndex);
            Assert.True(tint.ZIndex > card.ZIndex);
            SubliminalOverlay.Show(owner, "SECOND");
            Assert.Same(stage, Assert.Single(stages));
            Assert.Single(canvas.Children.OfType<Grid>());
            Assert.DoesNotContain(card, canvas.Children);
            SubliminalOverlay.CloseAll();
            Assert.True(stage.IsVisible);
            Assert.Same(tint, Assert.Single(canvas.Children));
            PinkFilterOverlay.CloseAll();
            Assert.False(stage.IsVisible);
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            PinkFilterOverlay.CloseAll();
            owner.Close();
            SharedOverlayHost.Configure = configure;
            SharedOverlayHost.IsTransparent = transparent;
            SharedOverlayHost.Raise = raise;
            SubliminalOverlay.IsAvailable = available;
            ScreenList.QueryOverride = query;
        }
    });
}
