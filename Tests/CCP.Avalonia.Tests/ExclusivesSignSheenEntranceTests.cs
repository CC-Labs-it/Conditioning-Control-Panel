using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class ExclusivesSignSheenEntranceTests
{
    [Fact]
    public Task SignBandsFollowMotionAndParkThroughShellNavigation() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var clock = new SteppedClock();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode);
        FxAdorner.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = (MotionLevel.Full, false, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            shell.ShowTab("premium"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var bands = view.GetVisualDescendants().OfType<Rectangle>().Where(r => r.Name == "SignSheen").ToArray();
            Assert.Equal(2, bands.Length);
            foreach (var band in bands)
            {
                var image = Assert.IsType<ImageBrush>(band.OpacityMask);
                var host = Assert.IsType<Grid>(band.Parent);
                Assert.Same(host.Children.OfType<Image>().Single().Source, image.Source);
            }
            double X(int i) => ((LinearGradientBrush)bands[i].Fill!).StartPoint.Point.X;
            Assert.True(view.SignMotionRunning);
            Assert.Equal(-1.2, X(0), 6);
            clock.Now = (long)(0.775 * TimeSpan.TicksPerSecond); view.MotionFrame();
            Assert.Equal(-1.2 + 2.4 * (1 - Math.Cos(Math.PI / 4)) / 2, X(0), 6);
            Assert.Equal(-1.2, X(1), 6);
            clock.Now = (long)(1.6 * TimeSpan.TicksPerSecond); view.MotionFrame();
            Assert.Equal(1.2, X(0), 6);
            Assert.InRange(X(1), -1.2, 1.19);
            clock.Now = (long)(2.3 * TimeSpan.TicksPerSecond); view.MotionFrame();
            Assert.Equal(1.2, X(1), 6);
            clock.Now = (long)(4.71 * TimeSpan.TicksPerSecond); view.MotionFrame();
            Assert.InRange(X(0), -1.2, -1.199);
            shell.ShowTab("settings"); Settle();
            Assert.False(view.SignMotionRunning);
            Assert.Equal(-1.2, X(0), 6);
            settings.MotionLevel = MotionLevel.Reduced;
            shell.ShowTab("premium"); Settle();
            bands = view.GetVisualDescendants().OfType<Rectangle>().Where(r => r.Name == "SignSheen").ToArray();
            Assert.True(view.SignMotionRunning);
            clock.Now += (long)(2.7 * TimeSpan.TicksPerSecond); view.SignTick();
            Assert.Equal(1.2, X(0), 6);
            clock.Now += (long)(6.81 * TimeSpan.TicksPerSecond); view.SignTick();
            Assert.InRange(X(0), -1.2, -1.199);
            settings.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(view.SignMotionRunning);
            Assert.Equal(-1.2, X(0), 6);
        }
        finally
        {
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = old;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FullSignSweepRunsAtPerformanceTierAndTracksGateChanges() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var clock = new SteppedClock();
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode, settings.GentleVisuals);
        FxAdorner.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode, settings.GentleVisuals) = (MotionLevel.Full, true, false, false);
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            shell.Show(); Settle();
            shell.ShowTab("premium"); Settle();
            var view = shell.Named<ExclusivesTabView>("ExclusivesTab")!;
            var band = view.GetVisualDescendants().OfType<Rectangle>().First(r => r.Name == "SignSheen");
            double X() => ((LinearGradientBrush)band.Fill!).StartPoint.Point.X;
            Assert.False(view.MotionRunning);
            Assert.True(view.SignMotionRunning);
            Assert.Single(BeatLoop.Running.Where(loop => loop.Owner == view));
            clock.Now += (long)(1.6 * TimeSpan.TicksPerSecond); view.SignTick();
            Assert.Equal(1.2, X(), 6);

            settings.PerformanceMode = false;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(view.MotionRunning);
            Assert.True(view.SignMotionRunning);
            Assert.Single(BeatLoop.Running.Where(loop => loop.Owner == view));
            settings.PerformanceMode = true;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(view.MotionRunning);
            Assert.True(view.SignMotionRunning);
            Assert.Single(BeatLoop.Running.Where(loop => loop.Owner == view));
            clock.Now += (long)(1.6 * TimeSpan.TicksPerSecond); view.SignTick();
            Assert.Equal(1.2, X(), 6);
            shell.ShowTab("settings"); Settle();
            Assert.False(view.SignMotionRunning);
            Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == view);
            Assert.Equal(-1.2, X(), 6);
            shell.ShowTab("premium"); Settle();
            Assert.True(view.SignMotionRunning);
            settings.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(view.SignMotionRunning);
        }
        finally
        {
            shell?.Close();
            FxAdorner.Time = TimeProvider.System;
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode, settings.GentleVisuals) = old;
        }
        return Task.CompletedTask;
    });

    private static void Settle()
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class SteppedClock : TimeProvider
    {
        public long Now;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
    }
}
