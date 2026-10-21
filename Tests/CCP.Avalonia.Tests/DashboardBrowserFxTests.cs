using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace CCP.Avalonia.Tests;

public sealed class DashboardBrowserFxTests
{
    [Fact]
    public Task BrowserFrameSweepsAndLoadingPulsesWithMotionGate() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var settings = CoreSettings.Current;
        settings.MotionLevel = MotionLevel.Full;
        settings.PerformanceMode = false;
        var time = new SteppedTime();
        BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var tab = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var frame = tab.FindControl<Border>("BrowserCardFrame")!;
            var brush = Assert.IsType<LinearGradientBrush>(frame.BorderBrush);
            var status = tab.FindControl<TextBlock>("TxtBrowserStatus")!;
            // Status writes are the real browser path, no extra caller needs to restart FX.
            status.Text = "Connected";
            status.Text = "  " + Loc.Get("label_loading").ToUpperInvariant() + "  ";
            Assert.Equal(0.35, status.Opacity, 3);
            time.Advance(TimeSpan.FromSeconds(1.2));
            BeatLoop.StepAll();
            Assert.Equal(0.375, brush.GradientStops[0].Offset, 3); // -0.30 + half of 1.35
            Assert.Equal(0.525, brush.GradientStops[1].Offset, 3);
            Assert.Equal(0.675, brush.GradientStops[2].Offset, 3);
            Assert.Equal(0.905, status.Opacity, 3); // WPF sine ease, 1.6 s half cycle
            time.Advance(TimeSpan.FromSeconds(1.2));
            BeatLoop.StepAll();
            Assert.Equal(1.05, brush.GradientStops[0].Offset, 3);
            time.Advance(TimeSpan.FromSeconds(0.02));
            BeatLoop.StepAll();
            Assert.Equal(1.05, brush.GradientStops[0].Offset, 3); // discrete reset at 2.45 s
            time.Advance(TimeSpan.FromSeconds(0.04));
            BeatLoop.StepAll();
            Assert.Equal(-0.30, brush.GradientStops[0].Offset, 3);
            time.Advance(TimeSpan.FromSeconds(12.74));
            BeatLoop.StepAll();
            Assert.Equal(0.375, brush.GradientStops[0].Offset, 3); // next cycle, 15.2 s
            status.Text = "Failed";
            Assert.Equal(1, status.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == status);

            settings.MotionLevel = MotionLevel.Reduced;
            Env.RaiseMotionGateChanged();
            status.Text = Loc.Get("label_loading");
            Assert.Equal(1, status.Opacity);
            Assert.Equal(-0.30, brush.GradientStops[0].Offset, 3);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == frame || l.Owner == status);
            settings.MotionLevel = MotionLevel.Off;
            Env.RaiseMotionGateChanged();
            Assert.Equal(1, status.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == frame || l.Owner == status);
            settings.MotionLevel = MotionLevel.Full;
            Env.RaiseMotionGateChanged();
            Assert.Contains(BeatLoop.Running, l => l.Owner == frame);
            Assert.Contains(BeatLoop.Running, l => l.Owner == status);
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            var parked = BeatLoop.Running.Where(l => l.Owner == frame || l.Owner == status).ToList();
            Assert.NotEmpty(parked);
            Assert.All(parked, l => Assert.False(l.IsTicking));
            shell.ShowTab("settings");
            shell.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, status.Opacity);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == frame || l.Owner == status);
            shell.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            settings.PerformanceMode = true;
            Env.RaiseMotionGateChanged();
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == frame || l.Owner == status);
            settings.PerformanceMode = false;
            Env.RaiseMotionGateChanged();
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == frame || l.Owner == status);
        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            BeatLoop.Time = TimeProvider.System;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
