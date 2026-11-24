using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Tours;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

// WPF TutorialOverlay.xaml:8, TutorialOverlay.xaml.cs:52-59 and :360-424: the coach mark covers
// MainWindow's whole monitor. The satellite draws that outside the host window.
[Collection(RunsAloneCollection.Name)]
public class TutorialMonitorDimTests
{
    private sealed class Store : TutorialService.ITourCompletionStore
    {
        public bool Has(string name) => false;
        public void Latch(string name) { }
    }

    private static global::Avalonia.Platform.Screen MakeScreen(PixelRect bounds, double scaling)
    {
        var screen = (global::Avalonia.Platform.Screen)Activator.CreateInstance(typeof(global::Avalonia.Platform.PlatformScreen),
            new object[] { new global::Avalonia.Platform.PlatformHandle(new IntPtr(bounds.X + 1), "headless-test") })!;
        typeof(global::Avalonia.Platform.Screen).GetProperty("Scaling")!.SetValue(screen, scaling);
        typeof(global::Avalonia.Platform.Screen).GetProperty("Bounds")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("WorkingArea")!.SetValue(screen, bounds);
        typeof(global::Avalonia.Platform.Screen).GetProperty("IsPrimary")!.SetValue(screen, true);
        return screen;
    }

    private static long Area(PixelRect[] rects) => rects.Sum(r => (long)r.Width * r.Height);

    private static void AssertLeavesClear(PixelRect[] rects, PixelRect monitor, PixelRect hole)
    {
        Assert.All(rects, r => Assert.False(r.Intersects(hole), $"{r} covers the host {hole}"));
        Assert.Equal((long)monitor.Width * monitor.Height - (long)hole.Width * hole.Height, Area(rects));
    }

    [Fact]
    public void MixedScaleHostRectIsLeftClear()
    {
        // Satellite monitor at 100 %, host on a 200 % monitor: the host's frame is 800x600 px.
        var monitor = new PixelRect(1920, 0, 2560, 1440);
        var host = TutorialOverlay.HostPixelRect(new PixelPoint(2000, 100), new Size(400, 300), 2.0);
        Assert.Equal(new PixelRect(2000, 100, 800, 600), host);
        var rects = TutorialOverlay.MonitorDimRects(monitor, host);
        AssertLeavesClear(rects, new PixelRect(0, 0, 2560, 1440), new PixelRect(80, 100, 800, 600));
    }

    private static Task WithDialogTour(Action<Window, Window, TutorialOverlay> check) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var previousStore = TutorialService.CompletionStore;
        var previousQuery = ScreenList.QueryOverride;
        TutorialService.CompletionStore = new Store();
        TutorialHead.Seed();
        TutorialHead.ResetForTests();
        var main = new Window { Width = 800, Height = 600, Content = new Button { Content = "Main" } };
        var dialog = new Window { Width = 400, Height = 300, Content = new Button { Content = "Dialog" },
            WindowStartupLocation = WindowStartupLocation.Manual, Position = new PixelPoint(300, 200) };
        TutorialOverlay? overlay = null;
        TutorialStep? step = null;
        string? previousWindow = null;
        try
        {
            TutorialOverlay.ForceMonitorDimForTests = true;
            ScreenList.QueryOverride = _ => new[] { MakeScreen(new PixelRect(0, 0, 1920, 1080), 1.5) };
            TutorialHead.Service.Start(TutorialType.DeeperEditorInteractiveHTPart2);
            while (TutorialHead.Service.CurrentStep!.Id != "iht_play") TutorialHead.Service.Next();
            step = TutorialHead.Service.CurrentStep;
            previousWindow = step.TargetWindowTypeName;
            step.TargetWindowTypeName = null;
            main.Show();
            dialog.Show();
            overlay = new TutorialOverlay(dialog, main);
            overlay.Show();
            Dispatcher.UIThread.RunJobs();
            check(main, dialog, overlay);
        }
        finally
        {
            overlay?.Close();
            TutorialHead.ResetForTests();
            dialog.Close();
            main.Close();
            if (step != null) step.TargetWindowTypeName = previousWindow;
            TutorialService.CompletionStore = previousStore;
            ScreenList.QueryOverride = previousQuery;
            TutorialOverlay.ForceMonitorDimForTests = false;
            Dispatcher.UIThread.RunJobs();
        }
        return Task.CompletedTask;
    });

    private static PixelRect HostRect(Window dialog) =>
        TutorialOverlay.HostPixelRect(dialog.Position, dialog.FrameSize ?? dialog.ClientSize, dialog.DesktopScaling);

    [Fact]
    public Task DialogTourCoversMainMonitorFromOwnedNonActivatingSatellite() => WithDialogTour((main, dialog, overlay) =>
    {
        var satellite = overlay.MonitorDimWindow;
        Assert.NotNull(satellite);
        Assert.True(satellite!.IsVisible);
        Assert.Same(main, satellite.Owner);
        Assert.False(satellite.ShowActivated);
        Assert.False(satellite.Topmost);
        Assert.False(satellite.ShowInTaskbar);
        Assert.Equal(new PixelPoint(0, 0), satellite.Position);
        Assert.Equal(1280, satellite.Width, 3);     // 1920 px at 150 %
        Assert.Equal(720, satellite.Height, 3);
        AssertLeavesClear(overlay.MonitorDimRegion!, new PixelRect(0, 0, 1920, 1080), HostRect(dialog));
        Assert.True(dialog.IsVisible);
    });

    [Fact]
    public Task MovingTheDialogMovesTheClearRect() => WithDialogTour((main, dialog, overlay) =>
    {
        dialog.Position = new PixelPoint(900, 500);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new PixelPoint(900, 500), HostRect(dialog).Position);
        AssertLeavesClear(overlay.MonitorDimRegion!, new PixelRect(0, 0, 1920, 1080), HostRect(dialog));
    });

    [Fact]
    public Task NonBlockingStepDropsTheSatellite() => WithDialogTour((main, dialog, overlay) =>
    {
        var satellite = overlay.MonitorDimWindow!;
        CoreTutorial.RaiseStepChanged(null, new CoreTutorial.Step { BlockBackgroundClicks = false });
        Assert.Null(overlay.MonitorDimWindow);
        Assert.False(satellite.IsVisible);
        CoreTutorial.RaiseStepChanged(null, new CoreTutorial.Step { BlockBackgroundClicks = true });
        Assert.NotNull(overlay.MonitorDimWindow);
    });

    [Theory]
    [InlineData("skip")]
    [InlineData("panic")]
    [InlineData("panic-surfaces")]
    [InlineData("host-close")]
    [InlineData("finish")]
    public Task EndingTourClosesTheSatelliteAtOnce(string ending) => WithDialogTour((main, dialog, overlay) =>
    {
        var satellite = overlay.MonitorDimWindow!;
        Assert.True(satellite.IsVisible);
        switch (ending)
        {
            case "skip": TutorialHead.Service.Skip(); break;
            case "panic": TutorialHead.OnPanic(); break;
            case "panic-surfaces": PanicSurfaces.StopAll("tutorial monitor dim test"); break;
            case "host-close": dialog.Close(); break;
            case "finish": while (TutorialHead.Service.IsActive) TutorialHead.Service.Next(); break;
        }
        // No dispatcher pump: the satellite is gone within the same tick.
        Assert.False(satellite.IsVisible);
        Assert.Null(overlay.MonitorDimWindow);
    });
}
