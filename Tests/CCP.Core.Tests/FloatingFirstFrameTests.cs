using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FloatingFirstFrameTests
{
    [Theory]
    [InlineData(1, 0, 40, true, false)]
    [InlineData(3, 0, 0, true, false)]
    [InlineData(3, 0, 40, false, false)]
    [InlineData(3, 0, 40, true, true)]
    public void PaintNeedsSeveralMovingFramesOnAVisibleSurface(int frames, long first, long at, bool visible, bool expected)
        => Assert.Equal(expected, FloatingRevealGate.Painted(frames, first, at, visible));

    public static TheoryData<bool, int?, int, int?, FloatingRevealGate.Verdict> RevealCases => new()
    {
        { true, null, 0, null, FloatingRevealGate.Verdict.Reveal },
        { false, 999, 400, 3999, FloatingRevealGate.Verdict.Wait },
        { false, 1000, 399, 3999, FloatingRevealGate.Verdict.Wait },
        { false, 1000, 400, 3999, FloatingRevealGate.Verdict.Reveal },
        { false, 5000, 0, 9000, FloatingRevealGate.Verdict.Reveal },
        { false, 0, 0, 3999, FloatingRevealGate.Verdict.Wait },
        { false, 0, 0, 4000, FloatingRevealGate.Verdict.Reveal },
        { false, 3999, 0, null, FloatingRevealGate.Verdict.Wait },
        { false, 4000, 0, null, FloatingRevealGate.Verdict.Reveal },
        { false, 5000, 0, 3999, FloatingRevealGate.Verdict.Wait },
        { false, null, 0, 3999, FloatingRevealGate.Verdict.Wait },
        { false, null, 0, 4000, FloatingRevealGate.Verdict.Skip },
        { false, null, 0, null, FloatingRevealGate.Verdict.Wait },
    };

    [Theory]
    [MemberData(nameof(RevealCases))]
    public void RevealUsesFrameProofOrPlayingFallbackAndOnlySkipsBeforePlaying(bool painted, int? playing, int advanced,
        int? ready, FloatingRevealGate.Verdict expected)
        => Assert.Equal(expected, FloatingRevealGate.Decide(painted, playing, advanced, ready));
}
