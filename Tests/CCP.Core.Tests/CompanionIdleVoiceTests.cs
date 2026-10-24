using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

[Collection(SessionStatics.Name)]
public sealed class CompanionIdleVoiceTests
{
    [Theory]
    [InlineData(false, false, false, true)]
    [InlineData(true, false, false, false)]
    [InlineData(true, true, false, true)]
    [InlineData(true, false, true, true)]
    public void OnlyUnpromptedEngineOffLinesAreHeld(bool idle, bool running, bool optIn, bool expected) =>
        Assert.Equal(expected, CompanionIdleVoiceRule.ShouldVoice(idle, running, optIn));

    [Theory]
    [InlineData("Idle", true)]
    [InlineData("IDLE", true)]
    [InlineData("AvatarClicked", false)]
    [InlineData(null, false)]
    public void TriggerClassifiesTheLine(string? trigger, bool expected) =>
        Assert.Equal(expected, CompanionIdleVoiceRule.IsUnprompted(trigger));

    [Fact]
    public void PromptedLinesNeverReadEngineState()
    {
        var old = CoreSession.IsEngineRunningProvider;
        try
        {
            int reads = 0;
            CoreSession.IsEngineRunningProvider = () => { reads++; return false; };
            Assert.True(CompanionIdleVoiceRule.ShouldVoiceNow(false));
            Assert.Equal(0, reads);
        }
        finally { CoreSession.IsEngineRunningProvider = old; }
    }
}
