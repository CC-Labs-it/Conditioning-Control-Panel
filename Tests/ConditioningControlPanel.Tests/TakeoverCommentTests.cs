using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.AIService;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Takeover's random Comment (beebee, 2026-10-20): the old prompt asked for a tease "to get
/// attention", the model cast the user as the brat, and a stateless call repeated the same line
/// word for word minutes apart.
/// </summary>
public class TakeoverCommentTests
{
    [Fact]
    public void The_prompt_never_asks_for_attention_seeking()
    {
        Assert.DoesNotContain("to get attention", AutonomyService.TakeoverCommentPrompt);
        Assert.Contains("did nothing to ask for it", AutonomyService.TakeoverCommentPrompt);
    }

    [Fact]
    public void The_brain_event_says_the_user_did_nothing()
    {
        var e = FrameFormatter.TakeoverCommentEvent();
        Assert.Contains("asked for nothing", e);
        Assert.True(e.Length <= 100, "descriptor too long for the brain's clamp");
        Assert.DoesNotContain("ignore", e);
        Assert.DoesNotContain("forget", e);
    }

    [Theory]
    [InlineData("Are you trying to be a brat?", "Are you trying to be a brat?")]
    [InlineData("Are you trying to be a brat?", "  are you trying to be a BRAT?  ")]
    public void A_word_for_word_repeat_is_dropped(string last, string next)
    {
        Assert.True(AutonomyService.IsRepeatComment(last, next));
    }

    [Theory]
    [InlineData(null, "Good girl.")]
    [InlineData("", "Good girl.")]
    [InlineData("Good girl.", "Such a good girl.")]
    public void A_new_line_is_shown(string? last, string next)
    {
        Assert.False(AutonomyService.IsRepeatComment(last, next));
    }
}
