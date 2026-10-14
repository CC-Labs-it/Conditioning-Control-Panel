using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DashboardDepthAndMuteTests
{
    [Theory]
    [InlineData(false, true, true, true, 0)]
    [InlineData(true, false, false, false, -1)]
    [InlineData(true, false, false, true, -2)]
    [InlineData(true, false, true, true, 1)]
    [InlineData(true, true, true, true, 2)]
    public void MosaicFaceTravelFollowsPriority(bool enabled, bool pressed, bool active, bool hovered, double expected) =>
        Assert.Equal(expected, DashboardDepthLaw.TravelFor(enabled, pressed, active, hovered));

    [Theory]
    [InlineData(false, false, "DepthRaisedBevel")]
    [InlineData(false, true, "DepthPressedBevel")]
    [InlineData(true, false, "DepthPressedBevel")]
    public void BevelDescribesTheFaceHeight(bool pressed, bool active, string expected) =>
        Assert.Equal(expected, DashboardDepthLaw.BevelKeyFor(pressed, active));

    [Fact]
    public void PointerTiltIsClampedAndMirrored()
    {
        Assert.Equal(-1.2, DashboardDepthLaw.TiltFor(4, 3));
        Assert.Equal(1.2, DashboardDepthLaw.TiltFor(-4, 3));
        Assert.Equal(0, DashboardDepthLaw.TiltFor(0, 1));
    }

    [Theory]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    public void HalfMuteReturnsColourForActiveOrHovered(bool active, bool hovered, bool mute) =>
        Assert.Equal(mute, CardMuteRule.ShouldMuteHalf(active, hovered));

    [Theory]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, true)]
    [InlineData(true, true, true, false)]
    public void ClickInversionOnlyAppliesOnDashboard(bool dashboard, bool invert, bool right, bool toggle) =>
        Assert.Equal(toggle, DashboardDepthLaw.ToggleFor(dashboard, invert, right));

    [Theory]
    [InlineData(false, true, 0)]
    [InlineData(true, false, 0)]
    [InlineData(true, true, 200)]
    public void MuteFadeOnlyAnimatesAfterLoading(bool motion, bool loaded, int ms) =>
        Assert.Equal(ms, CardMuteRule.TransitionMs(motion, loaded));

    [Fact]
    public void GreyLumaKeepsAlphaAndHandlesIncompleteTrailingPixel()
    {
        byte[] pixels = { 0, 0, 255, 93, 0, 255, 0, 127, 255, 0, 0, 255, 44 };
        CardMutePixels.ToGrey(pixels);
        Assert.Equal(new byte[] { 76, 76, 76, 93, 150, 150, 150, 127, 29, 29, 29, 255, 44 }, pixels);
    }
}
