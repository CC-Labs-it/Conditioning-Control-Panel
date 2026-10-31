using System;
using System.Threading.Tasks;
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
                Assert.True(window.IsVisible);
                Assert.IsType<Border>(window.Content);
                Assert.Equal(Brushes.Transparent, window.Background);
                Assert.Contains(WindowTransparencyLevel.Transparent, window.TransparencyLevelHint);
                Assert.False(window.IsHitTestVisible);
                Assert.Equal(TimeSpan.FromSeconds(0.75), Assert.IsType<DoubleTransition>(Assert.Single(window.Transitions!)).Duration);
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
            finally { o.CloseAll(); window.Close(); }
            return Task.CompletedTask;
        });
    }
}
