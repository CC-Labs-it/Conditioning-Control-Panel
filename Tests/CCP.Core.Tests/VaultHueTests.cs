using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VaultHueTests
{
    [Theory]
    [InlineData(0x7FFF0000u, 120, 0x7F00FF00u)]
    [InlineData(0x4D00FF00u, -120, 0x4DFF0000u)]
    [InlineData(0x120000FFu, 120, 0x12FF0000u)]
    [InlineData(0x40808080u, -59, 0x40808080u)]
    [InlineData(0x00000000u, 725, 0x00000000u)]
    [InlineData(0xFFFFFFFFu, -725, 0xFFFFFFFFu)]
    [InlineData(0x4DFF69B4u, 360, 0x4DFF69B4u)]
    [InlineData(0x4DFF69B4u, -720, 0x4DFF69B4u)]
    public void RotationPreservesAlphaAndHandlesAllSectorsAndWraps(uint color, double degrees, uint expected)
        => Assert.Equal(expected, VaultHue.ShiftHue(color, degrees));

    [Fact]
    public void VaultPartnerUsesTheWpfHsvPairRatherThanTheNavHslRotation()
    {
        var pink = VaultHue.Partner(0xFFFF69B4);
        Assert.InRange(NavStripPaint.R(pink), (byte)175, (byte)191);
        Assert.Equal(0x69, NavStripPaint.G(pink));
        Assert.Equal(0xFF, NavStripPaint.B(pink));
        Assert.Equal(0xFFFF00FBu, VaultHue.Partner(0xFFFF0000));
        Assert.Equal(0xFFFBFF00u, VaultHue.Partner(0xFF00FF00));
        // The partner preserves the RGB range even when the accent is muted rather than saturated.
        var muted = VaultHue.Partner(0x3233CC88);
        Assert.Equal(0x32, NavStripPaint.A(muted));
        Assert.Equal(0xCC, NavStripPaint.G(muted));
        Assert.Equal(0x33, NavStripPaint.B(muted));
    }
}
