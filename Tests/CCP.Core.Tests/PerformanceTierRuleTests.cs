using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;

// WPF PerformanceProfile.CurrentTier (moved to Core) and the mandatory video's decoder choice
// (WPF VideoService.cs:3494/3861), both read by Settings ▸ Performance toggles.
public sealed class PerformanceTierRuleTests
{
    [Theory]
    [InlineData(true, false, 0, PerformanceTier.Performance)]   // the explicit toggle wins
    [InlineData(true, true, 0, PerformanceTier.Performance)]
    [InlineData(false, false, 99, PerformanceTier.Quality)]     // auto off: load never escalates
    [InlineData(false, true, 7, PerformanceTier.Quality)]
    [InlineData(false, true, 8, PerformanceTier.Balanced)]
    [InlineData(false, true, 15, PerformanceTier.Balanced)]
    [InlineData(false, true, 16, PerformanceTier.Performance)]
    public void ResolveEscalatesByHeavyElementsOnlyUnderAuto(bool perf, bool auto, int heavy, PerformanceTier expected) =>
        Assert.Equal(expected, PerformanceTierRule.Resolve(perf, auto, heavy));

    [Fact]
    public void MandatoryVideoDecodesInSoftwareUnlessGpuDecodeIsForced()
    {
        Assert.Equal(":avcodec-hw=none", MandatoryVideoScheduler.SoftwareDecodeOption(forceHardware: false));
        Assert.Null(MandatoryVideoScheduler.SoftwareDecodeOption(forceHardware: true));
    }
}
