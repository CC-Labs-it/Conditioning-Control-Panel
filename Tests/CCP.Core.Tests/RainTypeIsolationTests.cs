using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RainTypeIsolationTests
{
    // WPF retains these types. Its tests can see both heads' internals, so even an
    // internal duplicate makes an unqualified reference ambiguous (CS0433).
    [Theory]
    [InlineData("RainLookCodec")]
    [InlineData("RainLookHash")]
    [InlineData("RainPackSink")]
    [InlineData("RainStripFrames")]
    public void CoreDoesNotShadowTheWpfRainContract(string typeName)
        => Assert.Null(typeof(CoreSettings).Assembly.GetType(
            "ConditioningControlPanel.Services.Companion." + typeName));
}
