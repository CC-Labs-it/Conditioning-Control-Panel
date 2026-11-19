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

    // A controller switched Autonomy on and it picked a web video: that takeover goes fullscreen
    // over the remote card. A late event from a stopped page (no arm) is still refused.
    [Theory]
    [InlineData(true, false, false, true)]
    [InlineData(true, false, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(false, false, false, false)]
    public void OnlyAnUnarmedEnterIsRefusedUnderTheRemoteCard(
        bool overlayVisible, bool remoteVideoActive, bool takeoverArmed, bool expected)
        => Assert.Equal(expected,
            RemoteBrowserStopRule.RefuseFullscreen(overlayVisible, remoteVideoActive, takeoverArmed));

    [Fact]
    public void AStop_ClearsTheArm_SoALateEnterStaysRefused()
    {
        var gate = new BrowserFullscreenGate();
        gate.ArmTakeover(0);
        Assert.True(gate.TakeoverArmed(100));
        gate.Clear();                              // stop_video, panic, EndWebVideoTakeover
        Assert.False(gate.TakeoverArmed(100));
        Assert.False(gate.NoteTakeoverAsk(150));
        Assert.True(RemoteBrowserStopRule.RefuseFullscreen(true, false, gate.TakeoverArmed(200)));
    }
}
