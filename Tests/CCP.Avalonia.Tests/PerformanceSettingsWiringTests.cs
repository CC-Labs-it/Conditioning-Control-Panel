using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Settings ▸ Performance toggles that used to only save a value: AutoPerformanceMode escalates the
/// tier by live flashes + bubbles (WPF PerformanceProfile), the HW-decode box follows the Monitors
/// editor (WPF PerformanceSettingsSection :68), and a motion change re-reads the shell chrome loops
/// (WPF CmbMotionLevel_SelectionChanged: edge, sheen, favourites glow, fold arrow).
/// </summary>
public sealed class PerformanceSettingsWiringTests
{
    private static void EnsureApp()
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
    }

    [Fact]
    public Task AutoPerformanceEscalatesTheTierByLiveBubbles() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var (perf, auto, motion) = (s.PerformanceMode, s.AutoPerformanceMode, s.MotionLevel);
        var bubbles = BubbleOverlay.Field.Bubbles;
        var savedBubbles = bubbles.ToArray();
        Window? w = null;
        try
        {
            (s.PerformanceMode, s.AutoPerformanceMode, s.MotionLevel) = (false, true, MotionLevel.Full);
            bubbles.Clear();
            var section = new PerformanceSettingsSection();
            w = new Window { Content = section, Width = 700, Height = 1400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var box = section.FindControl<CheckBox>("ChkAutoPerformance")!;
            Assert.True(box.IsChecked);
            Assert.Equal(PerformanceTier.Quality, AmbientFxCanvas.Env.CurrentTier);
            for (var i = 0; i < 8; i++) bubbles.Add(new AmbientBubble());
            Assert.Equal(8, AmbientFxCanvas.Env.HeavyElementCount);
            Assert.Equal(PerformanceTier.Balanced, AmbientFxCanvas.Env.CurrentTier);
            for (var i = 0; i < 8; i++) bubbles.Add(new AmbientBubble());
            Assert.Equal(PerformanceTier.Performance, AmbientFxCanvas.Env.CurrentTier);
            Assert.False(AmbientFxCanvas.Env.AllowAmbientLoops);   // the heavy screen sheds the ambient loops

            box.IsChecked = false;   // the user's opt-out, through the box: load never escalates
            Assert.False(s.AutoPerformanceMode);
            Assert.Equal(PerformanceTier.Quality, AmbientFxCanvas.Env.CurrentTier);
        }
        finally
        {
            w?.Close();
            bubbles.Clear();
            foreach (var b in savedBubbles) bubbles.Add(b);
            (s.PerformanceMode, s.AutoPerformanceMode, s.MotionLevel) = (perf, auto, motion);
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task HwDecodeBoxFollowsTheMonitorsEditor() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var hw = s.VideoForceHardwareDecoding;
        Window? w = null;
        try
        {
            s.VideoForceHardwareDecoding = false;
            var section = new PerformanceSettingsSection();
            w = new Window { Content = section, Width = 700, Height = 1400 };
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var box = section.FindControl<CheckBox>("ChkVideoHwDecode")!;
            Assert.False(box.IsChecked);

            s.VideoForceHardwareDecoding = true;   // what Settings ▸ Monitors' ChkVideoGpuDecode writes
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsChecked);

            box.IsChecked = false;   // and this box still writes the setting
            Assert.False(s.VideoForceHardwareDecoding);
        }
        finally
        {
            w?.Close();
            s.VideoForceHardwareDecoding = hw;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task MotionChangeStopsAndReArmsTheShellChromeLoops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        EnsureApp();
        var s = CoreSettings.Current;
        var (motion, perf, fold) = (s.MotionLevel, s.PerformanceMode, s.DashboardBrowserCollapsed);
        MainShellWindow? w = null;
        try
        {
            (s.MotionLevel, s.PerformanceMode, s.DashboardBrowserCollapsed) = (MotionLevel.Full, false, true);
            w = new MainShellWindow();
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var dash = w.Named<SettingsTabView>("SettingsTab")!;
            var strip = w.PageStrip!;
            var lift = w.Named<Border>("SectionEdgeLiftTop")!;
            // Home: the fold arrow and the favourites handle breathe, the edge lift is up.
            Assert.True(dash.FoldArrowBreathing && dash.FavoritesDrawerBreathing && lift.IsVisible,
                "a Home chrome loop never started - the test proves nothing");
            SetMotion(MotionLevel.Off);   // what Settings ▸ Performance's motion combo does
            Assert.False(dash.FoldArrowBreathing, "fold arrow kept breathing at motion Off");
            Assert.False(dash.FavoritesDrawerBreathing, "favourites glow kept breathing at motion Off");
            Assert.False(lift.IsVisible, "edge lift stayed up at motion Off");
            SetMotion(MotionLevel.Full);
            Assert.True(dash.FoldArrowBreathing && dash.FavoritesDrawerBreathing && lift.IsVisible,
                "motion Full did not re-arm the Home chrome loops");

            // A page with the section strip: its idle sheen clock follows the level both ways.
            w.ShowTab("haptics");
            Dispatcher.UIThread.RunJobs();
            Assert.True(strip.SheenClockRunning, "the strip sheen never started - the test proves nothing");
            SetMotion(MotionLevel.Off);
            Assert.False(strip.SheenClockRunning, "strip sheen kept its clock at motion Off");
            SetMotion(MotionLevel.Full);
            Assert.True(strip.SheenClockRunning, "motion Full did not re-arm the strip sheen");

            void SetMotion(MotionLevel level)
            {
                s.MotionLevel = level;
                AmbientFxCanvas.Env.RaiseMotionGateChanged();
                Dispatcher.UIThread.RunJobs();
            }
        }
        finally
        {
            w?.Close();
            Dispatcher.UIThread.RunJobs();
            (s.MotionLevel, s.PerformanceMode, s.DashboardBrowserCollapsed) = (motion, perf, fold);
        }
        return Task.CompletedTask;
    });
}
