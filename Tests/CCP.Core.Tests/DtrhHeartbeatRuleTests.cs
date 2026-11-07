using System;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public sealed class DtrhHeartbeatRuleTests
{
    [Theory]
    [InlineData(true, false, true, 10, false)]
    [InlineData(true, false, true, 10.001, true)]
    [InlineData(true, false, false, 20, false)]
    [InlineData(true, false, false, 20.001, true)]
    [InlineData(false, false, true, 100, false)]
    [InlineData(true, true, true, 100, false)]
    [InlineData(true, false, true, -1, false)]
    public void LoadingAndExitAreIgnored_RunAndHubUseStrictWpfDeadlines(
        bool ready, bool exiting, bool running, double silentSeconds, bool expected)
        => Assert.Equal(expected, DtrhHeartbeatRule.ShouldRecover(ready, exiting, running, TimeSpan.FromSeconds(silentSeconds)));
}
