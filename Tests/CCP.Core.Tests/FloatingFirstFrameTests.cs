using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public class FloatingFirstFrameTests
{
    [Theory]
    [InlineData(0, false, false)]
    [InlineData(3.999, false, false)]
    [InlineData(4, false, true)]
    [InlineData(20, false, true)]
    [InlineData(4, true, false)]
    [InlineData(20, true, false)]
    public void Skip_only_an_unframed_clip_after_four_seconds(double seconds, bool framed, bool skip) =>
        Assert.Equal(skip, MandatoryVideoPresentation.FirstFrameExpired(seconds, framed));
}
