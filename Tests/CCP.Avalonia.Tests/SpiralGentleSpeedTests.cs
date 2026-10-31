using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class SpiralGentleSpeedTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task SettingsSpeedAppliesAtStartAndOnLiveTicks(bool initiallyGentle) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        // Three 30 ms frames. Drive the real decoder, windows and timer, without a desktop.
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".gif");
        File.WriteAllBytes(path, Convert.FromBase64String("R0lGODlhBAAEAIEAAP8AAAAAAAAAAAAAACH/C05FVFNDQVBFMi4wAwEAAAAh+QQAAwAAACwAAAAABAAEAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEA/wAAAAAAAAAAAAAICQABCBxIsCCAgAAh+QQBAwABACwAAAAABAAEAIEAAP8AAAAAAAAAAAAICQABCBxIsCCAgAA7"));
        var host = new Window();
        try
        {
            SpiralOverlay.CloseAll();
            if (SpiralOverlay.Decoding is { } pending) await pending;
            SpiralOverlay.CloseAll();
            SpiralOverlay.SkipPlatformChecksForTests = true;
            CoreSession.IsEngineRunningProvider = () => true;
            var service = new ConditioningControlPanel.Services.SettingsService();
            CoreSettings.ServiceProvider = () => service;
            var s = CoreSettings.Current;
            s.SpiralEnabled = true;
            s.SpiralRandomize = false;
            s.SpiralPath = path;
            s.GentleVisuals = initiallyGentle;
            s.GentleSpeed = 0.5;
            var settings = new PerformanceSettingsSection();
            SpiralOverlay.Refresh(host);
            if (SpiralOverlay.Decoding is { } decoding) await decoding;
            Assert.True(SpiralOverlay.IsShowing && SpiralOverlay.IsAnimating);
            var timer = (DispatcherTimer)typeof(SpiralOverlay).GetField("_timer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            Assert.Equal(initiallyGentle ? 60 : 30, timer.Interval.TotalMilliseconds);

            // The user's Performance editors change a running spiral without reopening it.
            settings.FindControl<CheckBox>("ChkGentleVisuals")!.IsChecked = true;
            settings.FindControl<ComboBox>("CmbGentleSpeed")!.SelectedIndex = 3;
            SpiralOverlay.Tick();
            Assert.Equal(120, timer.Interval.TotalMilliseconds);
            SpiralOverlay.Tick();
            Assert.Equal(120, timer.Interval.TotalMilliseconds); // no compounded scaling
            settings.FindControl<ComboBox>("CmbGentleSpeed")!.SelectedIndex = 1;
            SpiralOverlay.Tick();
            Assert.Equal(40, timer.Interval.TotalMilliseconds);
            settings.FindControl<CheckBox>("ChkGentleVisuals")!.IsChecked = false;
            SpiralOverlay.Tick();
            Assert.Equal(30, timer.Interval.TotalMilliseconds);
            Assert.Equal(TimeSpan.FromMilliseconds(30), SpiralOverlay.FrameDelay);
            Assert.Same(timer, typeof(SpiralOverlay).GetField("_timer", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null));
            SpiralOverlay.CloseAll();
            Assert.False(timer.IsEnabled);
        }
        finally
        {
            SpiralOverlay.CloseAll();
            host.Close();
            File.Delete(path);
        }
    });
}
