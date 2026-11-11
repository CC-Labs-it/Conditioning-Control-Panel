using System;
using System.Collections.Generic;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class X11KeywordInputTests
{
    [Theory]
    [InlineData(0x61u, 'a')]
    [InlineData(0x41u, 'A')] // XKB has already applied shift or caps
    [InlineData(0xe9u, 'é')]
    [InlineData(0x010020acu, '€')]
    public void CharacterMapsResolvedKeysyms(uint keysym, char expected) =>
        Assert.Equal(expected, X11KeywordInput.Character(keysym));

    [Theory]
    [InlineData(0xff0d, KeywordInputKey.Clear)]
    [InlineData(0xff1b, KeywordInputKey.Clear)]
    [InlineData(0xff09, KeywordInputKey.Clear)]
    [InlineData(0xff08, KeywordInputKey.Backspace)]
    [InlineData(0x20, KeywordInputKey.Space)]
    public void SpecialKeysMapToEngineKeys(uint keysym, KeywordInputKey expected) =>
        Assert.Equal(expected, X11KeywordInput.Special(keysym));

    [Fact]
    public void InvalidAndNonCharacterKeysymsAreIgnored()
    {
        Assert.Null(X11KeywordInput.Character(0xff0d));
        Assert.Null(X11KeywordInput.Character(0x0100d800));
        Assert.Null(X11KeywordInput.Character(0x0101f600));
        Assert.Null(X11KeywordInput.Special(0x61));
    }

    [Fact]
    public void PanicRunsFirstAndThrowingKeywordDoesNotEscape()
    {
        var seen = new List<string>();
        var previous = X11PanicKey.BoundKeycode;
        X11PanicKey.BindKeycodeForTest(42);
        try
        {
            X11PanicKey.RoutePress(42, () => seen.Add("panic"), _ =>
            {
                seen.Add("keyword");
                throw new InvalidOperationException();
            });
            seen.Add("listener continues");
            Assert.Equal(new[] { "panic", "keyword", "listener continues" }, seen);
        }
        finally { X11PanicKey.BindKeycodeForTest(previous); }
    }
}
