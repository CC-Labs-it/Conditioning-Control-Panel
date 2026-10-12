using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF Services/Safety/BlinkStopGate (now in Core), the order WPF checks: the Blink Trainer asks for
/// blinks, so it blocks first; then Lockdown, a disabled panic key, Strict Lock.</summary>
public sealed class BlinkStopGateCoreTests
{
    [Theory]
    [InlineData(true, true, false, true, 1)]
    [InlineData(false, true, false, true, 2)]
    [InlineData(false, false, false, true, 3)]
    [InlineData(false, false, true, true, 4)]
    [InlineData(false, false, true, false, 0)]
    public void ChecksInWpfOrder(bool trainer, bool lockdown, bool panicOn, bool strict, int expected) =>
        Assert.Equal((BlinkStopGate.Block)expected, BlinkStopGate.Check(trainer, lockdown, panicOn, strict));
}
