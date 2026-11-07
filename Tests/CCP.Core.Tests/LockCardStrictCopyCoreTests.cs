using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>WPF 7.2.2 #1547, moved to Core for both heads: the strict copy never says Escape does
/// nothing while Escape is the panic key (the WPF suite holds the full rows and the language files).</summary>
public class LockCardStrictCopyCoreTests
{
    [Theory]
    [InlineData(true, "Escape", LockCardStrictCopy.HintEscCloses)]
    [InlineData(false, " escape ", LockCardStrictCopy.HintPanicIsEscape)]
    [InlineData(false, "F8", LockCardStrictCopy.HintOnlyPanicKey)]
    [InlineData(false, null, LockCardStrictCopy.HintOnlyPanicKey)]
    public void Hint_NamesTheRealExit(bool escCloses, string? panicKey, string expected)
        => Assert.Equal(expected, LockCardStrictCopy.HintKey(escCloses, panicKey));

    [Fact]
    public void Confirm_PicksTheEscapeBody_OnlyWhenPanicIsEscape()
    {
        Assert.Equal(LockCardStrictCopy.ConfirmBodyPanicIsEscape, LockCardStrictCopy.ConfirmBodyKey("Escape"));
        Assert.Equal(LockCardStrictCopy.ConfirmBody, LockCardStrictCopy.ConfirmBodyKey("F8"));
    }
}
