using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF SubliminalService.cs:631-644, 1344-1374.
public sealed class SubliminalGentleVisualsTests
{
    private sealed class Clock : TimeProvider
    {
        public long Ms;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => Ms;
    }

    [Theory]
    [InlineData(false, 0, 50)]
    [InlineData(true, 0, 250)]
    [InlineData(true, 5, 2000)]
    public Task PerformanceSettingsControlEachCardEnvelope(bool gentle, int choice, int fadeMs) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        s.SubBackgroundTransparent = false;
        s.GentleVisuals = false;
        s.SubliminalDuration = 2;
        s.DualMonitorEnabled = false;
        s.GlobalTargetMonitor = -1;
        var clock = new Clock();
        var oldClock = SubliminalOverlayWindow.Clock;
        var oldFrame = SubliminalOverlayWindow.RequestFrame;
        SubliminalOverlayWindow.Clock = clock;
        SubliminalOverlayWindow.RequestFrame = (_, callback) => callback(TimeSpan.Zero);
        var settings = new PerformanceSettingsSection();
        var host = new Window();
        SubliminalOverlay.SkipPlatformChecksForTests = true;
        var windows = new System.Collections.Generic.List<SubliminalOverlayWindow>();
        try
        {
            settings.FindControl<CheckBox>("ChkGentleVisuals")!.IsChecked = gentle;
            settings.FindControl<ComboBox>("CmbGentleFade")!.SelectedIndex = choice;
            SubliminalOverlay.Show(host, "OBEY", 60);
            var w = Assert.Single(SubliminalOverlay.Shown);
            windows.Add(w);
            var grid = (Grid)w.Content!;
            Assert.Equal(!gentle, grid.Children.OfType<Rectangle>().Any());
            Assert.Equal(9, grid.Children.OfType<TextBlock>().Count());
            Assert.True(w.IsFading);
            Assert.Equal(0, w.CurrentOpacity);
            void Step(long ms) { clock.Ms = ms; w.FadeFrame(); }
            Step(fadeMs / 2); Assert.Equal(0.3, w.CurrentOpacity, 3);
            Step(fadeMs); Assert.Equal(0.6, w.CurrentOpacity, 3);
            Step(fadeMs + 50); Assert.Equal(0.6, w.CurrentOpacity, 3);
            // A live setting change affects the next card, not this one's running envelope.
            s.GentleVisuals = !gentle;
            Step(fadeMs + 100 + fadeMs / 2); Assert.Equal(0.3, w.CurrentOpacity, 3);
            Assert.True(w.IsVisible);
            Step(2 * fadeMs + 100); Assert.False(w.IsVisible);
            Assert.False(w.IsFading);

            // A replacement/Stop closes a card immediately and stops its fade timer.
            SubliminalOverlay.Show(host, "NEXT", 60);
            var next = Assert.Single(SubliminalOverlay.Shown);
            windows.Add(next);
            Assert.True(next.IsFading);
            SubliminalOverlay.Show(host, "REPLACEMENT", 60);
            var replacement = Assert.Single(SubliminalOverlay.Shown);
            windows.Add(replacement);
            Assert.False(next.IsVisible);
            Assert.False(next.IsFading);
            Assert.True(replacement.IsFading);
            SubliminalOverlay.CloseAll();
            Assert.False(replacement.IsVisible);
            Assert.False(replacement.IsFading);
            return Task.CompletedTask;
        }
        finally
        {
            SubliminalOverlay.CloseAll();
            foreach (var w in windows) w.Close();
            SubliminalOverlayWindow.RequestFrame = oldFrame;
            SubliminalOverlay.SkipPlatformChecksForTests = false;
            SubliminalOverlayWindow.Clock = oldClock;
            host.Close();
        }
    });

    [Fact]
    public Task GentleBackstopAllowsTheWholeEnvelopeWithoutAFirstFrame() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        (service.Current.GentleVisuals, service.Current.GentleFadeSeconds) = (true, 0.25);
        var frame = SubliminalOverlayWindow.RequestFrame;
        SubliminalOverlayWindow.RequestFrame = (_, _) => { };
        var w = new SubliminalOverlayWindow("OBEY");
        try
        {
            w.Show();
            w.Run(0.6, TimeSpan.FromMilliseconds(100));
            await Task.Delay(400); // the old 300 ms backstop would have cut the Gentle fade short
            Assert.True(w.IsVisible);
            await Task.Delay(500);
            Assert.False(w.IsVisible);
        }
        finally { w.Close(); SubliminalOverlayWindow.RequestFrame = frame; }
    });

    [Fact]
    public Task ADelayedFirstFrameStillGetsTheFullGentleEnvelope() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var service = new ConditioningControlPanel.Services.SettingsService();
        CoreSettings.ServiceProvider = () => service;
        (service.Current.GentleVisuals, service.Current.GentleFadeSeconds) = (true, 0.25);
        var frame = SubliminalOverlayWindow.RequestFrame;
        Action<TimeSpan>? firstFrame = null;
        SubliminalOverlayWindow.RequestFrame = (_, callback) => firstFrame = callback;
        var w = new SubliminalOverlayWindow("OBEY");
        try
        {
            w.Show();
            w.Run(0.6, TimeSpan.FromMilliseconds(100));
            await Task.Delay(400);
            Assert.NotNull(firstFrame);
            firstFrame!(TimeSpan.Zero);
            var initialOpacity = w.CurrentOpacity;
            await Task.Delay(80);
            Assert.True(w.CurrentOpacity > initialOpacity); // advances without another frame callback
            await Task.Delay(270); // past the original backstop, before this card's fade-out finishes
            Assert.True(w.IsVisible);
            await Task.Delay(500);
            Assert.False(w.IsVisible);
        }
        finally { w.Close(); SubliminalOverlayWindow.RequestFrame = frame; }
    });

}
