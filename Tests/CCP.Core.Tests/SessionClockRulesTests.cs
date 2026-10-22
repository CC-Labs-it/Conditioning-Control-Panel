using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SessionClockRulesTests
{
    [Theory]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, false)]
    public void ResumeStartsEngine_OnlyWhenStoppedAndLocallyOwned(bool engine, bool controller, bool expected)
        => Assert.Equal(expected, SessionClockRules.ResumeStartsEngine(engine, controller));

    [Theory]
    [InlineData(true, true, true, true)]
    [InlineData(false, true, true, false)]
    [InlineData(true, true, false, false)]
    [InlineData(true, false, true, false)]
    public void UserStartResumes_OnlyAnActuallyRunningParkedSession(bool engine, bool session, bool paused, bool expected)
        => Assert.Equal(expected, SessionClockRules.StartResumesParkedSession(engine, session, paused));

    [Fact]
    public void PauseMark_IsVisibleWithAndWithoutCountdown()
    {
        var mark = SessionClockRules.PauseMark(true, "PAUSED");
        Assert.Equal(" [PAUSED]", mark);
        Assert.Equal("", SessionClockRules.PauseMark(false, "PAUSED"));
        Assert.Equal("STOP SESSION (01:14) [PAUSED]", SessionClockLabel.StopButton(
            TimeSpan.FromSeconds(74), true, "STOP SESSION ({0}:{1})", "STOP SESSION", mark));
        Assert.Equal("Session [PAUSED]", SessionClockLabel.StartButton(
            "Session", TimeSpan.FromSeconds(74), false, mark, "{0} {1}:{2}{3}"));
    }

    [Fact]
    public void ThrowingReporter_DoesNotPreventLaterSteps()
    {
        Assert.False(SessionClockRules.RunStep(() => throw new Exception("step"), _ => throw new Exception("log")));
        var ran = false;
        Assert.True(SessionClockRules.RunStep(() => ran = true, null));
        Assert.True(ran);
    }
}
