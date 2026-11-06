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
}
