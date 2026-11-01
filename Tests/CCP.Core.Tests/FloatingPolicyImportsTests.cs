using ConditioningControlPanel.Services.Video;
using ConditioningControlPanel.Services.Video.Floating;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FloatingPolicyImportsTests
{
    [Fact]
    public void WpfPolicyRemainsUnambiguousWithCoreVideoImports()
    {
        Assert.Equal(FloatingRevealGate.ReadyBudgetMs, FloatingFirstFrame.ReadyBudgetMs);
        Assert.Equal(FloatingRevealGate.PlayingGraceMs, FloatingFirstFrame.PlayingGraceMs);
        Assert.Equal(FloatingRevealGate.AdvanceMs, FloatingFirstFrame.AdvanceMs);
        Assert.Equal(FloatingFirstFrame.Verdict.Wait, FloatingFirstFrame.Decide(false, 0, 0, 3999));
        Assert.Equal(FloatingFirstFrame.Verdict.Reveal, FloatingFirstFrame.Decide(false, 0, 0, 4000));
    }

    [Theory]
    [MemberData(nameof(FloatingFirstFrameTests.RevealCases), MemberType = typeof(FloatingFirstFrameTests))]
    public void WpfAndCoreAgreeOnEveryRevealBoundary(bool painted, int? playing, int advanced, int? ready,
        FloatingRevealGate.Verdict expected)
    {
        var core = FloatingRevealGate.Decide(painted, playing, advanced, ready);
        var wpf = FloatingFirstFrame.Decide(painted, playing, advanced, ready);
        Assert.Equal(core.ToString(), wpf.ToString());
        Assert.Equal(expected, core);
    }
}
