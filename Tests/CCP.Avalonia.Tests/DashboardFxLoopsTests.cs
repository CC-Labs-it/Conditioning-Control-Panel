using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF MainWindow.DashboardFx ApplyDashboardFxLoops (:171): window state and the
/// motion/performance gate re-run every dashboard card's RefreshFx and the vault CTA breath.</summary>
public sealed class DashboardFxLoopsTests
{
    [Fact]
    public Task DashboardLoopsFollowWindowStateMotionAndPerformance() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.MotionLevel = MotionLevel.Full;
        s.PerformanceMode = false;

        var time = new SteppedTime();
        global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = time;
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            shell.Activate();
            Dispatcher.UIThread.RunJobs();
            var dash = shell.FindControl<SettingsTabView>("SettingsTab")!;
            var card = dash.FindControl<FeatureCard>("CardFlash")!;
            var combo = dash.FindControl<SplitFeatureCard>("ComboVideoBubble")!;
            card.IsActive = true;
            combo.IsActiveA = true;
            Dispatcher.UIThread.RunJobs();
            void AllBreathing(bool on)
            {
                Assert.Equal(on, card.IsBreathing);
                Assert.Equal(on, combo.IsBreathing);
                Assert.Equal(on, shell.VaultCtaBreathing);
            }
            AllBreathing(true);

            // The stamp really breathes 1.0 -> 1.07 -> 1.0 (Animation.RunAsync on its ScaleTransform threw).
            var scale = (ScaleTransform)((TransformGroup)dash.FindControl<TextBlock>("VaultCta")!.RenderTransform!).Children[0];
            time.Advance(TimeSpan.FromSeconds(1.1));
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.Equal(1.035, scale.ScaleX, 3);
            // The tile glow + ring breathe on the same beat (no infinite Animation, no Effect on the tile, P82):
            // WPF FeatureCard.xaml.cs:679-688, 0.50->0.90 / 0.55->1.0 over 3.5 s SineEase AutoReverse.
            Assert.Null(card.FindControl<Border>("RootBorder")!.Effect);
            Assert.Equal(0.590, card.FindControl<Border>("GlowLayer")!.Opacity, 3);
            Assert.Equal(0.651, card.FindControl<Border>("ActiveBorder")!.Opacity, 3);
            time.Advance(TimeSpan.FromSeconds(1.1));
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.Equal(1.07, scale.ScaleY, 3);
            time.Advance(TimeSpan.FromSeconds(2.2));
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.StepAll();
            Assert.Equal(1.0, scale.ScaleX, 3);

            // Minimised: every loop parks; restored: they re-arm (the funnel's window hook).
            shell.WindowState = WindowState.Minimized;
            Dispatcher.UIThread.RunJobs();
            AllBreathing(false);
            shell.WindowState = WindowState.Normal;
            Dispatcher.UIThread.RunJobs();
            AllBreathing(true);

            // The user path: Settings > Performance > Motion = Reduced, then back to Home.
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            var perf = shell.GetLogicalDescendants().OfType<PerformanceSettingsSection>().First();
            perf.FindControl<ComboBox>("CmbMotionLevel")!.SelectedIndex = 1;
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(MotionLevel.Reduced, s.MotionLevel);
            AllBreathing(false);
            var glow = card.FindControl<Border>("GlowLayer")!;
            Assert.Equal(0.90, glow.Opacity, 3);                       // parked at PEAK, WPF :660
            Assert.Equal(1.0, card.FindControl<Border>("ActiveBorder")!.Opacity, 3);

            // A gate change while Home is on screen re-runs the funnel (Env.MotionGateChanged).
            s.MotionLevel = MotionLevel.Full;
            global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Dispatcher.UIThread.RunJobs();
            AllBreathing(true);

            // Performance mode raises the same gate (tier Performance forbids ambient loops).
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            perf.FindControl<CheckBox>("ChkPerformanceMode")!.IsChecked = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(s.PerformanceMode);
            shell.ShowTab("settings");
            Dispatcher.UIThread.RunJobs();
            AllBreathing(false);
            Assert.Equal(0.0, glow.Opacity, 3);                         // no glow on the Performance tier

        }
        finally
        {
            shell.Close();
            Dispatcher.UIThread.RunJobs();
            global::ConditioningControlPanel.Avalonia.Helpers.BeatLoop.Time = TimeProvider.System;
            s.MotionLevel = MotionLevel.Full;
            s.PerformanceMode = false;
            CoreSettings.ServiceProvider = old;
        }
        return Task.CompletedTask;
    });
}
