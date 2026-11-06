using Avalonia;
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

    /// <summary>Owner 11-06: on Linux the shell opened at 1661x1002 and filled the 1680x1050 monitor; it now opens
    /// 20% smaller per axis. Windows keeps WPF's size. The XAML floors (1131x620) still hold.</summary>
    [Fact]
    public void TheLinuxShellOpensAFifthSmallerAndWindowsKeepsWpfsSize()
    {
        Assert.Equal((1329d, 802d), StartupPlacement.ShellSize(1661, 1002, 1131, 620, linux: true));
        Assert.Equal((1661d, 1002d), StartupPlacement.ShellSize(1661, 1002, 1131, 620, linux: false));
        Assert.Equal((1131d, 620d), StartupPlacement.ShellSize(1200, 700, 1131, 620, linux: true));
    }

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
