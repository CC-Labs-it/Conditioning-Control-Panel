using ConditioningControlPanel.Services.Autonomy;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace CCP.Core.Tests;
public class TakeoverCommentPortTests
{
    [Theory]
    [InlineData("hello", " HELLO ", true)]
    [InlineData("hello", "another line", false)]
    [InlineData(null, "hello", false)]
    [InlineData(" ", " ", false)]
    public void RepeatIsTrimmedAndCaseInsensitive(string? last, string? next, bool expected)
        => Assert.Equal(expected, TakeoverComment.IsRepeat(last, next));

    [Fact]
    public void UnpromptedCommentNeverCastsTheUserAsAttentionSeeking()
    {
        Assert.DoesNotContain("to get attention", TakeoverComment.Prompt);
        Assert.Contains("did nothing to ask for it", TakeoverComment.Prompt);
        Assert.Contains("asked for nothing", FrameFormatter.TakeoverCommentEvent());
    }
}
