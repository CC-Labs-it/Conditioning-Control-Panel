using ConditioningControlPanel.Services.Video;
using Xunit;

namespace CCP.Core.Tests;

public sealed class FloatingVideoFadeTests
{
    [Theory]
    [InlineData(false, 100, 0.75, 0)] // decoder startup never spends the fade
    [InlineData(true, 0, 0.75, 0)]
    [InlineData(true, 0.375, 0.75, 0.5)]
    [InlineData(true, 0.75, 0.75, 1)]
    [InlineData(true, 5, 0.75, 1)]
    [InlineData(true, -1, 0.75, 0)]
    [InlineData(false, 0, 0, 1)] // off or full screen: no fade
    [InlineData(true, 0, 0, 1)]
    public void Picture_rises_from_black_after_first_frame(bool framed, double elapsed, double fade, double expected)
        => Assert.Equal(expected, MandatoryVideoPresentation.PictureOpacity(framed, elapsed, fade));
}
