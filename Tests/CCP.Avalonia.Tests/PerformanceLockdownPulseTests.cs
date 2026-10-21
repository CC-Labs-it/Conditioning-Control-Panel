using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PerformanceLockdownPulseTests
{
    [Fact]
    public Task Lockdown_navigation_pulses_and_motion_off_stops_until_the_next_visit()
        => AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode);
            var oldTime = BeatLoop.Time;
            var time = new SteppedTime();
            BeatLoop.Time = time;
            MainShellWindow? shell = null;
            Window? settingsHost = null;
            try
            {
                (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode) = (MotionLevel.Full, false, false);
                shell = new MainShellWindow();
                shell.Show();
                shell.ShowTab("lockdown");
                Dispatcher.UIThread.RunJobs();
                Assert.True(shell.LockdownPulsing); // fails on the original base: the pulse has no caller
                var plate = shell.Named<Control>("LockdownTab")!.FindControl<Border>("LockdownImageBorder")!;
                var glow = shell.Named<Control>("LockdownTab")!.FindControl<Border>("LockdownImageGlow")!;
                Assert.Null(plate.Effect);
                Assert.Equal(Color.FromRgb(255, 20, 147), Assert.IsAssignableFrom<ISolidColorBrush>(plate.BorderBrush).Color);
                time.Advance(TimeSpan.FromSeconds(1.5));
                BeatLoop.StepAll();
                Assert.Equal(Color.FromRgb(255, 105, 180), Assert.IsAssignableFrom<ISolidColorBrush>(plate.BorderBrush).Color);
                Assert.Equal(22, glow.BoxShadow[0].Blur);
                Assert.Equal(1, glow.Opacity);
                time.Advance(TimeSpan.FromSeconds(1.5));
                BeatLoop.StepAll();
                Assert.Equal(Color.FromRgb(255, 20, 147), Assert.IsAssignableFrom<ISolidColorBrush>(plate.BorderBrush).Color);
                Assert.Equal(12, glow.BoxShadow[0].Blur);
                Assert.Equal(0.7, glow.Opacity, 10);

                var settings = new PerformanceSettingsSection();
                settingsHost = new Window { Content = settings };
                settingsHost.Show();
                var motion = settings.FindControl<ComboBox>("CmbMotionLevel")!;
                motion.SelectedIndex = 2;
                Assert.Equal(MotionLevel.Off, s.MotionLevel);
                Assert.False(shell.LockdownPulsing);
                Assert.Equal(Color.FromRgb(255, 20, 147), Assert.IsAssignableFrom<ISolidColorBrush>(plate.BorderBrush).Color);
                Assert.Equal(14, glow.BoxShadow[0].Blur);
                motion.SelectedIndex = 0;
                Assert.False(shell.LockdownPulsing); // WPF re-arms this pulse on the next tab visit
                shell.ShowTab("lockdown");
                Assert.True(shell.LockdownPulsing);
                shell.ShowTab("settings");
                Assert.False(shell.LockdownPulsing);
                motion.SelectedIndex = 1;
                shell.ShowTab("lockdown");
                Assert.False(shell.LockdownPulsing);
                motion.SelectedIndex = 0;
                shell.ShowTab("lockdown");
                Assert.True(shell.LockdownPulsing);
                shell.Close();
                Assert.False(shell.LockdownPulsing);
            }
            finally
            {
                settingsHost?.Close();
                shell?.Close();
                BeatLoop.Time = oldTime;
                (s.MotionLevel, s.PerformanceMode, s.AutoPerformanceMode) = saved;
            }
            return Task.CompletedTask;
        });
}
