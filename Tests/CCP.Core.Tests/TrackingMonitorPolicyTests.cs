using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF App.GetWebcamCalibrationScreen + GazeContentScreenPolicy over a screen-name list.</summary>
public class TrackingMonitorPolicyTests
{
    private static readonly string[] Three = { "DP-1", "HDMI-1", "eDP-1" };

    [Theory]
    [InlineData("Primary", 2)]          // sentinel: the primary, not index 0
    [InlineData("", 2)]
    [InlineData(null, 2)]
    [InlineData("hdmi-1", 1)]           // named, case-insensitive (WPF OrdinalIgnoreCase)
    [InlineData("DP-9", 2)]             // unplugged: back to the primary
    public void CalibrationScreen_IsTheNamedOne_ElseThePrimary(string? saved, int expected) =>
        Assert.Equal(expected, TrackingMonitorPolicy.CalibrationIndex(saved, Three, 2));

    [Fact]
    public void CalibrationScreen_NoScreens_IsMinusOne_AndABadPrimaryIsFirst()
    {
        Assert.Equal(-1, TrackingMonitorPolicy.CalibrationIndex("DP-1", new string[0], 0));
        Assert.Equal(0, TrackingMonitorPolicy.CalibrationIndex("Primary", Three, -1));
    }

    [Theory]
    [InlineData("HDMI-1", true, new[] { 1 })]       // an explicit pick beats DualMonitorEnabled
    [InlineData("DP-9", true, new[] { 2 })]         // unplugged pick: primary alone, even in dual
    [InlineData("Primary", true, new[] { 0, 1, 2 })]
    [InlineData("Primary", false, new[] { 2 })]
    public void Overlays_ExplicitPickWins_ElseTheGlobalRule(string saved, bool dual, int[] expected) =>
        Assert.Equal(expected, TrackingMonitorPolicy.OverlayIndices(saved, Three, 2, dual));

    [Fact]
    public void Overlays_NoScreens_AreNone() =>
        Assert.Empty(TrackingMonitorPolicy.OverlayIndices("HDMI-1", new string[0], 0, true));
}
