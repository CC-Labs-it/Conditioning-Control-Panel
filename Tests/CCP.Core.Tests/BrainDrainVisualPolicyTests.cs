using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// WPF Services/Notifications/BrainDrainVisualPolicy (now in Core): Brain Drain's blur dial at zero
/// means no picture, not a faint one. The WPF call sites that must ask it stay pinned in
/// <c>Tests/ConditioningControlPanel.Tests/BrainDrainBlurZeroTests.cs</c>.
/// </summary>
public class BrainDrainVisualPolicyTests
{
    // ---- the settings model ----------------------------------------------------------

    [Fact]
    public void TheSettingAcceptsZero()
    {
        var s = new AppSettings { BrainDrainBlurStrength = 0 };
        Assert.Equal(0, s.BrainDrainBlurStrength);
    }

    [Theory]
    [InlineData(-5, 0)]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(50, 50)]
    [InlineData(100, 100)]
    [InlineData(250, 100)]
    public void TheClampStillHoldsBothEnds(int written, int expected)
    {
        var s = new AppSettings { BrainDrainBlurStrength = written };
        Assert.Equal(expected, s.BrainDrainBlurStrength);
    }

    /// <summary>
    /// Widening the floor must not rewrite a saved choice. Someone sitting on the old minimum of 1
    /// keeps 1 - which is a different setting from 0 and always was.
    /// </summary>
    [Fact]
    public void AnOldFileHoldingTheOldFloorIsLeftAlone()
    {
        var s = new AppSettings { BrainDrainBlurStrength = 1 };
        Assert.Equal(1, s.BrainDrainBlurStrength);
        Assert.False(BrainDrainVisualPolicy.IsSilent(s.BrainDrainBlurStrength));
    }

    // ---- the policy ------------------------------------------------------------------

    [Theory]
    [InlineData(0, true)]
    [InlineData(-1, true)]      // defensive: a corrupt file that dodged the setter
    [InlineData(1, false)]
    [InlineData(100, false)]
    public void SilentMeansZeroAndNothingElse(int strength, bool silent) =>
        Assert.Equal(silent, BrainDrainVisualPolicy.IsSilent(strength));

    [Theory]
    [InlineData(true, 50, true)]
    [InlineData(true, 1, true)]
    [InlineData(true, 0, false)]    // the feature is on, the picture is not
    [InlineData(false, 50, false)]
    [InlineData(false, 0, false)]
    public void TheBaseFeatureWantsTheBlurOnlyWhenBothHalvesSayYes(bool enabled, int strength, bool wants) =>
        Assert.Equal(wants, BrainDrainVisualPolicy.WantsBlur(enabled, strength));
}
