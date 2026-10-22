using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// BUG-7NAKK79CZ2: a 25-minute web video played under a 20m 3s max video length.
public sealed class VideoMaxCapTests
{
    [Theory]
    [InlineData(0, 100, null)]        // no cap set
    [InlineData(1203, 0, 1203.0)]     // fresh clip: the whole cap
    [InlineData(1203, 1000, 203.0)]   // a replay/fallback continues the budget
    [InlineData(1203, 1500, 0.0)]     // already past it: end now, never negative
    public void CapLeftContinuesTheClipBudget(int max, double spent, double? expected)
        => Assert.Equal(expected, VideoMaxCap.CapLeft(max, spent));

    [Fact]
    public void MandatoryGuardCountsCapSpentBeforeAReplay()
    {
        Assert.Null(MandatoryVideoScheduler.Guard(3, 2, true, true, 600, 0, 8));
        Assert.Equal("max length", MandatoryVideoScheduler.Guard(3, 2, true, true, 600, 0, 8, capSpentBefore: 7));
        Assert.Null(MandatoryVideoScheduler.Guard(3, 2, true, true, 600, 0, 0, capSpentBefore: 7));
    }
}
