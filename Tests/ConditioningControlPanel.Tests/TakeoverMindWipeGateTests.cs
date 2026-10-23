using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>ccp-bugs #1458 / #1453: Takeover played Mind Wipe stings with Mind Wipe switched off,
/// from the moment the app started. The Takeover box defaults to on, so Mind Wipe's own switch
/// must gate it too.</summary>
public class TakeoverMindWipeGateTests
{
    [Theory]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]   // the report: Takeover box on, Mind Wipe off
    [InlineData(false, true, false)]
    [InlineData(false, false, false)]
    public void MindWipe_NeedsBothSwitches(bool takeoverBox, bool mindWipeOn, bool expected)
        => Assert.Equal(expected, AutonomyService.MayMindWipe(takeoverBox, mindWipeOn));

    [Fact]
    public void TakeoverBox_DefaultsOn_SoTheFeatureSwitchIsWhatStopsIt()
    {
        var s = new ConditioningControlPanel.Models.AppSettings();
        Assert.True(s.AutonomyCanTriggerMindWipe);
        Assert.False(s.MindWipeEnabled);
        Assert.False(AutonomyService.MayMindWipe(s.AutonomyCanTriggerMindWipe, s.MindWipeEnabled));
    }
}
