using ConditioningControlPanel.Services.Video;
using Xunit;
using Verdict = ConditioningControlPanel.Services.Video.MandatoryVideoPresentation.FirstFrameVerdict;

namespace CCP.Core.Tests;

public class FloatingFirstFrameTests
{
    [Theory]
    [InlineData(0, false, false, Verdict.Wait)]
    [InlineData(3.999, false, false, Verdict.Wait)]
    [InlineData(3.999, false, true, Verdict.Wait)]
    [InlineData(4, false, false, Verdict.Skip)]
    [InlineData(20, false, false, Verdict.Skip)]
    [InlineData(4, false, true, Verdict.Reveal)]
    [InlineData(20, false, true, Verdict.Reveal)]
    [InlineData(0, true, false, Verdict.Reveal)]
    [InlineData(4, true, true, Verdict.Reveal)]
    public void Playing_without_a_frame_reveals_at_the_budget_instead_of_skipping(double seconds, bool framed, bool playing, object expected) =>
        Assert.Equal((Verdict)expected, MandatoryVideoPresentation.DecideFirstFrame(seconds, framed, playing));
}
