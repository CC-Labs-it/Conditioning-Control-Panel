using ConditioningControlPanel;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF and its tests reference Core transitively. A second public definition makes
/// callers fail with CS0433, even though each assembly compiles on its own.</summary>
public class ConversionTypeOwnershipTests
{
    [Theory]
    [InlineData("ConditioningControlPanel.Services.Startup.Conversion.LapseRow")]
    [InlineData("ConditioningControlPanel.Services.Startup.Conversion.LapseReading")]
    [InlineData("ConditioningControlPanel.Services.Startup.Conversion.ConversionRules")]
    [InlineData("ConditioningControlPanel.Services.Funnel.FunnelBeacon")]
    public void CoreDoesNotExportTypesStillOwnedByWpf(string fullName)
        => Assert.Null(typeof(CoreSettings).Assembly.GetType(fullName));
}
