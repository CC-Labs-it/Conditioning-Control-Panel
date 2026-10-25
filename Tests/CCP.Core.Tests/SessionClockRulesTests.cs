using System;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF MainWindow.Presets.cs:1943 (ccp-bugs #1464): a held clock says it is paused.</summary>
public sealed class SessionClockRulesTests
{
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
}
