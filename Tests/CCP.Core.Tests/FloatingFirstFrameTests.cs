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

    [Theory]
    [InlineData(true, null, 0, null, FloatingRevealGate.Verdict.Reveal)]
    [InlineData(false, 999, 400, 3999, FloatingRevealGate.Verdict.Wait)]
    [InlineData(false, 1000, 399, 3999, FloatingRevealGate.Verdict.Wait)]
    [InlineData(false, 1000, 400, 3999, FloatingRevealGate.Verdict.Reveal)]
    [InlineData(false, 5000, 0, 9000, FloatingRevealGate.Verdict.Reveal)]
    [InlineData(false, 0, 0, 3999, FloatingRevealGate.Verdict.Wait)]
    [InlineData(false, 0, 0, 4000, FloatingRevealGate.Verdict.Reveal)]
    [InlineData(false, 3999, 0, null, FloatingRevealGate.Verdict.Wait)]
    [InlineData(false, 4000, 0, null, FloatingRevealGate.Verdict.Reveal)]
    [InlineData(false, 5000, 0, 3999, FloatingRevealGate.Verdict.Wait)]
    [InlineData(false, null, 0, 3999, FloatingRevealGate.Verdict.Wait)]
    [InlineData(false, null, 0, 4000, FloatingRevealGate.Verdict.Skip)]
    [InlineData(false, null, 0, null, FloatingRevealGate.Verdict.Wait)]
    public void RevealUsesFrameProofOrPlayingFallbackAndOnlySkipsBeforePlaying(bool painted, int? playing, int advanced,
        int? ready, FloatingRevealGate.Verdict expected)
        => Assert.Equal(expected, FloatingRevealGate.Decide(painted, playing, advanced, ready));
}
