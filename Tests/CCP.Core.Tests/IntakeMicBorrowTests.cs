using ConditioningControlPanel.Services.Quiz;
using Xunit;

namespace CCP.Core.Tests;

public sealed class IntakeMicBorrowTests
{
    [Fact]
    public void OldAnswerCannotRearmTheWakeWordUnderTheNextAnswer()
    {
        var armed = true;
        var returned = 0;
        var mic = new IntakeMicBorrow(() => armed, () => armed = false, () => { armed = true; returned++; });
        Assert.True(mic.Take(1));
        Assert.False(mic.Take(2));
        mic.Return(1);
        Assert.False(armed);
        Assert.Equal(1, mic.Holders);
        mic.Return(2);
        Assert.True(armed);
        Assert.Equal(1, returned);
    }

    [Fact]
    public void TeardownReturnsOnceAndLateLoopCleanupDoesNothing()
    {
        var returned = 0;
        var mic = new IntakeMicBorrow(() => true, () => { }, () => returned++);
        mic.Take(1);
        mic.Take(2);
        mic.ReturnAll();
        mic.ReturnAll();
        mic.Return(1);
        mic.Return(2);
        Assert.Equal(1, returned);
        Assert.Equal(0, mic.Holders);
        Assert.False(mic.IsOwed);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ReturnReconcilesWithCurrentSettingsOnlyWhenSomethingWasBorrowed(bool initiallyArmed)
    {
        var enabled = initiallyArmed;
        var listening = enabled;
        var returned = 0;
        var mic = new IntakeMicBorrow(() => listening, () => listening = false,
            () => { returned++; listening = enabled; });
        Assert.Equal(initiallyArmed, mic.Take(1));
        enabled = false;
        mic.Return(1);
        Assert.False(listening);
        Assert.Equal(initiallyArmed ? 1 : 0, returned);
    }
}
