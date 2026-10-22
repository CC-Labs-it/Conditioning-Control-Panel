using ConditioningControlPanel.Services.Browser;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class RemoteBrowserStopTests
{
    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void StoppedRemoteVideoGivesTheOverlayItsAirspaceBack(
        bool overlayVisible, bool remoteVideoActive, bool expected)
        => Assert.Equal(expected, RemoteBrowserStopRule.HideBrowser(overlayVisible, remoteVideoActive));
}
