using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class WindowMinimizeRecoveryTests
{
    [Theory]
    [InlineData(true, true, false, false, true, true)]
    [InlineData(true, true, true, false, false, true)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(false, true, false, false, false, false)]
    public void OnlyAttachedAvatarBorrowsRunningUnmutedStateAndLockdownKeepsTakeover(
        bool attached, bool running, bool lockdown, bool muted, bool pause, bool mute)
    {
        var state = new WindowMinimizeRecovery();
        Assert.Equal((pause, mute), state.Minimized(attached, running, lockdown, muted));
        Assert.Equal((pause, mute), state.Restored());
        Assert.Equal((false, false), state.Restored());
    }

    [Fact]
    public void RepeatedMinimizeDoesNotForgetBorrowedState()
    {
        var state = new WindowMinimizeRecovery();
        state.Minimized(true, true, false, false);
        Assert.Equal((false, false), state.Minimized(true, false, false, true));
        Assert.Equal((true, true), state.Restored());
    }
}
