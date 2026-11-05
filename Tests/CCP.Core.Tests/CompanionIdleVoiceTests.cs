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
    [InlineData("FaceLost", true)]
    [InlineData("FaceFound", true)]
    [InlineData("Blink", true)]
    [InlineData("TrackingStateChanged", true)]
    [InlineData("IdleStateChanged", true)]
    [InlineData("ActivityChanged", true)]
    [InlineData("StillOnActivity", true)]
    [InlineData("AvatarClicked", false)]
    [InlineData(null, false)]
    public void TriggerClassifiesTheLine(string? trigger, bool expected) =>
        Assert.Equal(expected, CompanionIdleVoiceRule.IsUnprompted(trigger));

    [Fact]
    public void PanicAndPausedSessionsRespectTheWpfTruthTable()
    {
        for (int mask = 0; mask < 64; mask++)
        {
            bool idle = (mask & 1) != 0, engine = (mask & 2) != 0,
                session = (mask & 4) != 0, paused = (mask & 8) != 0,
                optIn = (mask & 16) != 0, quiet = (mask & 32) != 0;
            var expected = !idle || (!quiet && (engine || (session && !paused) || optIn));
            Assert.Equal(expected, CompanionIdleVoiceRule.ShouldVoice(idle, engine, session, paused, optIn, quiet));
        }
    }

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
