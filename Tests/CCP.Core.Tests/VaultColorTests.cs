using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class VaultColorTests
{
    [Theory]
    [InlineData(255, 0, 0, 120, 0, 255, 0)]
    [InlineData(0, 255, 0, 120, 0, 0, 255)]
    [InlineData(0, 0, 255, 120, 255, 0, 0)]
    [InlineData(255, 0, 0, -120, 0, 0, 255)]
    [InlineData(255, 0, 0, 480, 0, 255, 0)]
    [InlineData(255, 255, 0, 180, 0, 0, 255)]
    [InlineData(0, 0, 0, -59, 0, 0, 0)]
    [InlineData(128, 128, 128, -59, 128, 128, 128)]
    [InlineData(255, 255, 255, -59, 255, 255, 255)]
    public void HueRotationKeepsValueAndSaturation(byte r, byte g, byte b, double degrees,
        byte expectedR, byte expectedG, byte expectedB)
        => Assert.Equal((expectedR, expectedG, expectedB), VaultColor.ShiftHue(r, g, b, degrees));

    [Fact]
    public void VaultPartnerUsesTheWpfMinus59DegreePair()
        => Assert.Equal(((byte)255, (byte)0, (byte)251),
            VaultColor.ShiftHue(255, 0, 0, VaultColor.PartnerHueShift));
}
