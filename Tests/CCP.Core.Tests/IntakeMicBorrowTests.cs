using System;
using ConditioningControlPanel.Services.Quiz;
using Xunit;
namespace CCP.Core.Tests;
public class IntakeMicBorrowTests
{
    private sealed class FakeWake
    {
        public bool Armed = true;
        public int StoodDown, GivenBack;
        public bool Down;                               // the wake loop is stopped (stopping twice is one stop)
        public bool Listening => Armed && !Down;        // it holds the mic unless stood down
        public IntakeMicBorrow Borrow() => new(
            () => Armed,
            () => { StoodDown++; Down = true; },
            () => { GivenBack++; Down = false; });
    }

    [Fact]
    public void MicBorrow_TakesTheMicFromAnArmedWakeWordAndGivesItBackOnTheAnswer()
    {
        var wake = new FakeWake();
        var mic = wake.Borrow();
        Assert.True(mic.Take(1));
        Assert.False(wake.Listening);
        Assert.True(mic.IsOwed);
        mic.Return(1);                 // matched answer, timer, idle, error: all leave through Return
        Assert.True(wake.Listening);
        Assert.False(mic.IsOwed);
        Assert.Equal(1, wake.GivenBack);
    }

    [Fact]
    public void MicBorrow_LeavesAnUnarmedWakeWordAlone()
    {
        var wake = new FakeWake { Armed = false };
        var mic = wake.Borrow();
        Assert.False(mic.Take(1));
        mic.Return(1);
        mic.ReturnAll();
        Assert.Equal(0, wake.StoodDown);
        Assert.Equal(0, wake.GivenBack);   // nothing borrowed, so nothing is re-armed behind the user's back
    }

    [Fact]
    public void MicBorrow_ALateReturnFromTheLastQuestionDoesNotReArmUnderTheNextOne()
    {
        // The page opens the next say-it item before the previous listen loop has wound down.
        var wake = new FakeWake();
        var mic = wake.Borrow();
        mic.Take(1);
        mic.Take(2);                   // next question took the mic
        mic.Return(1);                 // old loop's finally arrives late
        Assert.False(wake.Listening);  // still on loan to question 2
        Assert.Equal(0, wake.GivenBack);
        mic.Return(2);
        Assert.True(wake.Listening);
        Assert.Equal(1, wake.GivenBack);
    }

    [Fact]
    public void MicBorrow_WindowCloseAndPanicGiveItBackAtOnceAndOnlyOnce()
    {
        var wake = new FakeWake();
        var mic = wake.Borrow();
        mic.Take(1);
        mic.Take(2);
        mic.ReturnAll();               // DisposeAll / CloseActive
        Assert.True(wake.Listening);
        Assert.Equal(0, mic.Holders);
        mic.ReturnAll();               // idempotent
        mic.Return(1);                 // loops still winding down
        mic.Return(2);
        mic.Return(99);                // a session that never took it
        Assert.Equal(1, wake.GivenBack);
    }

    [Fact]
    public void MicBorrow_StandsDownAgainWhenSomethingReArmedTheWakeWordMidAnswer()
    {
        // A lock card that closes hands the mic back to the wake loop; our next window asks again.
        var wake = new FakeWake();
        var mic = wake.Borrow();
        mic.Take(1);
        wake.Down = false;             // the lock card's own restore
        Assert.True(wake.Listening);
        Assert.True(mic.Take(1));
        Assert.False(wake.Listening);
        mic.Return(1);
        Assert.True(wake.Listening);
    }

    [Fact]
    public void MicBorrow_AThrowingHandoverNeverLosesTheDebt()
    {
        int back = 0;
        var mic = new IntakeMicBorrow(() => true, () => throw new InvalidOperationException(), () => back++);
        Assert.True(mic.Take(1));
        mic.Return(1);
        Assert.Equal(1, back);
    }

}
