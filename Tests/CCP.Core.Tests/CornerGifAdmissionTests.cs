using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class CornerGifAdmissionTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, false, true, false)]
    [InlineData(false, true, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, false, false)]
    [InlineData(true, false, true, true)]
    [InlineData(true, true, false, false)]
    [InlineData(true, true, true, false)]
    public void OnlyEnabledRunningStandaloneSlotsAreAdmitted(bool enabled, bool sessionOwnsCorner, bool running, bool expected)
        => Assert.Equal(expected, CornerGifPlanner.AllowStandaloneCornerGif(enabled, sessionOwnsCorner, running));
}
