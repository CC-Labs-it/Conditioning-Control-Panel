using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

public sealed class IdleVoiceLinesPlaceholderTests
{
    [Theory]
    [InlineData("Watching target application, pet?", "YouTube", "Firefox", "Watching YouTube, pet?")]
    [InlineData("Watching target application, pet?", "", " Firefox ", "Watching Firefox, pet?")]
    [InlineData("target application again?", null, null, "That again?")]
    [InlineData("Still on target application?", " ", "", "Still on that?")]
    [InlineData("Good girl.", null, null, "Good girl.")]
    public void ThePlaceholderBecomesTheFocusedAppOrThat(string text, string? service, string? detected, string expected) =>
        Assert.Equal(expected, IdleVoiceLines.ResolvePlaceholder(text, service, detected));
}
