using ConditioningControlPanel.Services.Haptics;
using Xunit;

namespace CCP.Core.Tests;

public class BackRoomHapticDirectorTests
{
    [Fact]
    public void LimitsAndShapesPulses()
    {
        var limit = new BackRoomHapticDirector.Limiter();
        Assert.True(limit.Accept(0));
        Assert.False(limit.Accept(79));
        Assert.True(limit.Accept(80));
        Assert.Equal(3, BackRoomHapticDirector.PriorityFor(0.7));
        Assert.Equal(2, BackRoomHapticDirector.PriorityFor(0.4));
        Assert.Equal(1, BackRoomHapticDirector.PriorityFor(0.3));
        Assert.Equal(1500, BackRoomHapticDirector.DurationFor(1000, true));
        Assert.Equal(500, BackRoomHapticDirector.DurationFor(500, false));
    }
}
