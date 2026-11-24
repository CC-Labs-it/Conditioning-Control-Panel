using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Xunit;
namespace CCP.Core.Tests;
public class HostedPageMotionTests
{
    [Theory]
    [InlineData(MotionLevel.Off, "--force-prefers-reduced-motion")]
    [InlineData(MotionLevel.Full, "--force-prefers-no-reduced-motion")]
    [InlineData(MotionLevel.Reduced, "--force-prefers-no-reduced-motion")]
    public void MotionFollowsSetting(MotionLevel level, string expected) => Assert.Equal(expected, HostedPageMotion.Argument(level));
    [Fact]
    public void MergeCombinesFeatureListsAndKeepsLastValueForOtherNames() => Assert.Equal(
        "--disable-features=A,B,C --autoplay=last --force-prefers-no-reduced-motion",
        HostedPageMotion.Merge("--disable-features=A,B --autoplay=first --disable-features=B,C --autoplay=last", MotionLevel.Full));
    [Fact]
    public void MergeReplacesMotionSwitchAndKeepsOtherArguments() => Assert.Equal(
        "--autoplay-policy=no-user-gesture-required --force-prefers-no-reduced-motion",
        HostedPageMotion.Merge("--autoplay-policy=no-user-gesture-required --force-prefers-reduced-motion --force-prefers-reduced-motion", MotionLevel.Reduced));
}
