using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RainTypeIsolationTests
{
    [Theory]
    [InlineData("RainLookCodec")]
    [InlineData("RainItemManifest")]
    [InlineData("RainItemAccess")]
    [InlineData("RainLookClamp")]
    [InlineData("RainWardrobeCatalog")]
    public void CoreDoesNotShadowWpfTypes(string name)
        => Assert.Null(typeof(CoreSettings).Assembly.GetType(
            "ConditioningControlPanel.Services.Companion." + name));
}
