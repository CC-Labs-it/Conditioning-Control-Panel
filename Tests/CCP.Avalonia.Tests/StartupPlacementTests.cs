using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>WPF CenterScreen for the splash: the monitor under the cursor, else the primary. Under WSLg's
/// XWayland there is no primary and 0,0 is on no monitor, which left Avalonia's CenterScreen with nothing.</summary>
public sealed class StartupPlacementTests
{
    // The owner's WSLg desktop as Screens.All reported it (Windows primary on the right, second monitor left and lower).
    private static readonly PixelRect Primary = new(1680, 0, 2560, 1440), Left = new(0, 235, 1680, 1050);
    private static readonly PixelRect[] Wslg = { Primary, Left };

    [Fact]
    public void NoPrimaryAndNoPointerFallsToTheFirstScreenNotZeroZero() =>
        Assert.Equal(0, StartupPlacement.Pick(Wslg, primary: -1, pointer: null));

    [Fact]
    public void ThePointersMonitorWinsAsInWpf()
    {
        Assert.Equal(1, StartupPlacement.Pick(Wslg, primary: 0, pointer: new PixelPoint(300, 600)));
        Assert.Equal(0, StartupPlacement.Pick(Wslg, primary: 1, pointer: new PixelPoint(2000, 100)));
    }

    [Fact]
    public void APointerOnNoMonitorMeansThePrimary()
    {
        // XWayland's last-known pointer can sit off every output; that says nothing about the user.
        Assert.Equal(1, StartupPlacement.Pick(Wslg, primary: 1, pointer: new PixelPoint(0, 0)));
        Assert.Equal(0, StartupPlacement.Pick(Wslg, primary: -1, pointer: new PixelPoint(0, 0)));
    }

    [Fact]
    public void NoScreensLeavesAvaloniaInCharge() =>
        Assert.Equal(-1, StartupPlacement.Pick(System.Array.Empty<PixelRect>(), primary: -1, pointer: null));

    [Fact]
    public void TheSplashLandsInTheMiddleOfThatWorkArea()
    {
        Assert.Equal(new PixelPoint(1680 + 1080, 612), StartupPlacement.CenterIn(Primary, 400, 215, 1.0));
        Assert.Equal(new PixelPoint(1680 + 1030, 585), StartupPlacement.CenterIn(Primary, 400, 215, 1.25));
        Assert.Equal(new PixelPoint(640, 652), StartupPlacement.CenterIn(Left, 400, 215, 0));   // unknown scale reads as 1
    }

    /// <summary>Owner 11-06 took the Linux shell 20% smaller (1329x802); owner 11-07 took it 25% bigger again, which
    /// is WPF's 1661x1002 on both. The XAML floors (1131x620) still hold.</summary>
    [Fact]
    public void TheLinuxShellOpensAtWpfsSizeAgain()
    {
        Assert.Equal((1661d, 1002d), StartupPlacement.ShellSize(1661, 1002, 1131, 620, linux: true));
        Assert.Equal((1661d, 1002d), StartupPlacement.ShellSize(1661, 1002, 1131, 620, linux: false));
        Assert.Equal((1131d, 620d), StartupPlacement.ShellSize(1000, 600, 1131, 620, linux: true));
    }

    /// <summary>Owner 11-07: a cold WSLg start first reports Xwayland's 640x480 placeholder ("rdp-0 0,0 640x480")
    /// for ~0.9 s; Avalonia kept it all run and the splash sat in a corner. Only that exact shape is waited out.</summary>
    [Fact]
    public void OnlyXwaylandsLone640x480PlaceholderIsWaitedOut()
    {
        Assert.True(X11ScreenSettle.IsPlaceholder(new[] { new PixelRect(0, 0, 640, 480) }));
        Assert.False(X11ScreenSettle.IsPlaceholder(Wslg));
        Assert.False(X11ScreenSettle.IsPlaceholder(new[] { Primary }));
        Assert.False(X11ScreenSettle.IsPlaceholder(new[] { new PixelRect(0, 0, 640, 480), Left }));
        Assert.False(X11ScreenSettle.IsPlaceholder(System.Array.Empty<PixelRect>()));
    }

    /// <summary>Owner 11-07: WSLg's window manager ignored the first-map position (the splash sat low-left) but
    /// honours a move after the map, so the startup position is sent again once the window is shown.</summary>
    [Fact]
    public Task AWindowManagerThatIgnoresTheFirstPositionIsOverruledAfterTheMap() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var w = new Window { Width = 400, Height = 215, WindowStartupLocation = WindowStartupLocation.Manual };
        try
        {
            var at = new PixelPoint(2760, 612);
            w.Position = at;
            StartupPlacement.ReassertAfterMap(w, at, linux: true);
            w.Show();
            w.Position = new PixelPoint(184, 950);   // where the window manager put it
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(at, w.Position);
            w.Position = new PixelPoint(10, 10);     // the user drags it later: not undone by the Opened hook
            w.Hide(); w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new PixelPoint(10, 10), w.Position);
        }
        finally { w.Close(); }
        return Task.CompletedTask;
    });

    [Fact]
    public void TheShellCentresOnThePrimaryAsWpfAndOnThePointersScreenWhenThereIsNone()
    {
        // WPF MainWindow.CenterOnPrimaryScreen: the primary, wherever the pointer is.
        Assert.Equal(0, StartupPlacement.PickForShell(Wslg, primary: 0, pointer: new PixelPoint(300, 600)));
        // WSLg reports no primary: the splash's screen, the one under the pointer, else the first listed.
        Assert.Equal(1, StartupPlacement.PickForShell(Wslg, primary: -1, pointer: new PixelPoint(300, 600)));
        Assert.Equal(0, StartupPlacement.PickForShell(Wslg, primary: -1, pointer: null));
        // Not 0,0 on no monitor any more: the middle of the 2560x1440 screen.
        Assert.Equal(new PixelPoint(1680 + 615, 319), StartupPlacement.CenterIn(Primary, 1329, 802, 1.0));
    }
}
