using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Moved from the WPF ConversationLinksTests with the rule (the page-line tests stay there).</summary>
public sealed class ConversationLinksTidyTests
{
    private const string Title = "Deep Acceptance";

    [Theory]
    [InlineData("Click here: [Deep Acceptance]", "Click here: Deep Acceptance")]
    [InlineData("Try this one [PLAY THE VIDEO: Deep Acceptance] now~", "Try this one Deep Acceptance now~")]
    [InlineData("Watch Deep Acceptance for me. [PLAY THE VIDEO]", "Watch Deep Acceptance for me.")]
    [InlineData("Go on: [click here]", "Go on")]
    [InlineData("Deep Acceptance, and [whispers] relax", "Deep Acceptance, and [whispers] relax")]
    [InlineData("No brackets here, Deep Acceptance.", "No brackets here, Deep Acceptance.")]
    public void TidyKeepsTheTitleAndDropsDeadBrackets(string input, string expected)
        => Assert.Equal(expected, ConversationLinks.Tidy(input, Title));

    [Fact]
    public void TidyWithoutATitleOnlyDropsPlaceholders()
    {
        Assert.Equal("Here you go.", ConversationLinks.Tidy("Here you go. [video link]", null));
        Assert.Equal("[giggles] hi", ConversationLinks.Tidy("[giggles] hi", null));
    }
}
