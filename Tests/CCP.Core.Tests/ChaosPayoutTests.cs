using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF ChaosModeService.EndRun / AwardLoopTip payout maths, worked by hand from the
/// original lines (:3243-3247, :1743-1750).</summary>
public class ChaosPayoutTests
{
    [Theory]
    [InlineData(180, 1.0, 400, 1.5, 400, 600)]     // under the cap: score is the base
    [InlineData(60, 1.0, 900, 2.0, 250, 500)]      // capped at 250/min
    [InlineData(120, 1.5, 9000, 1.0, 750, 750)]    // cap scales with difficulty
    [InlineData(0, 1.0, 900, 1.0, 4.1666666, 4.1666666)]   // duration floored at 1 s
    public void Xp_caps_the_score_then_applies_skill(double dur, double diff, double score, double skill,
        double expBase, double expFinal)
    {
        var xp = ChaosPayout.Xp(dur, diff, score, skill);
        Assert.Equal(expBase, xp.BaseXp, 4);
        Assert.Equal(skill, xp.SkillMult);
        Assert.Equal(expFinal, xp.FinalXp, 4);
    }

    [Theory]
    [InlineData(4, 1.0, false, false, 4)]
    [InlineData(3, 1.5, false, false, 4)]    // round(4.5) = 4 (to even, as WPF Math.Round)
    [InlineData(4, 1.0, true, false, 8)]
    [InlineData(5, 1.5, false, false, 8)]    // round(7.5) = 8 (to even)
    [InlineData(3, 2.0, true, true, 24)]     // 6 x2 clean x2 relapse
    public void Loop_tip(int roll, double diff, bool clean, bool relapse, int expected)
    {
        var s = new ChaosRunState(new ChaosRunConfig()) { RelapseLoopActive = relapse };
        Assert.Equal(expected, ChaosPayout.LoopTip(roll, diff, clean, s));
    }

    [Theory]
    [InlineData(3, 0, ChaosRank.Curious + 1)]   // crossed the 3-run threshold
    [InlineData(3, 1, null)]                    // already shown
    [InlineData(10, 0, ChaosRank.Curious + 2)]  // only the highest new rank
    [InlineData(0, 0, null)]
    public void Rank_up_only_above_the_last_card(int runs, int lastSeen, ChaosRank? expected)
        => Assert.Equal(expected, ChaosPayout.RankUp(runs, lastSeen));
}
