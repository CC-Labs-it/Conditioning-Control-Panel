using System.Collections.Generic;
using System.Drawing;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1456: regex keyword triggers fire from OCR too, not only from the keyboard
/// (<see cref="KeywordTriggerService.FindRegexMatchedWords"/>).
/// </summary>
public class KeywordOcrRegexTests
{
    private static List<OcrWordHit> Scan(params (string Text, int X)[] words)
    {
        var list = new List<OcrWordHit>();
        foreach (var (text, x) in words)
            list.Add(new OcrWordHit { Text = text, ScreenRect = new Rectangle(x, 100, 40, 20) });
        return list;
    }

    [Fact]
    public void RegexMatchesASingleToken()
    {
        var hits = KeywordTriggerService.FindRegexMatchedWords(@"\bgood (girl|pet)\b|obey\w*",
            Scan(("please", 0), ("OBEYING", 50), ("now", 100)));
        Assert.NotNull(hits);
        var hit = Assert.Single(hits!);
        Assert.Equal("OBEYING", hit.Text);
        Assert.Equal(new Rectangle(50, 100, 40, 20), hit.ScreenRect);
    }

    [Fact]
    public void RegexSpanningTokensGivesOneMergedHit()
    {
        var hits = KeywordTriggerService.FindRegexMatchedWords(@"good\s+(girl|pet)",
            Scan(("such", 0), ("a", 50), ("Good", 100), ("pet.", 150)));
        var hit = Assert.Single(hits!);
        Assert.Equal("Good pet", hit.Text);
        Assert.Equal(new Rectangle(100, 100, 90, 20), hit.ScreenRect);
    }

    [Fact]
    public void EveryMatchIsItsOwnHit()
    {
        var hits = KeywordTriggerService.FindRegexMatchedWords("drop+",
            Scan(("drop", 0), ("and", 50), ("droppp", 300)));
        Assert.Equal(2, hits!.Count);
    }

    [Theory]
    [InlineData("nomatch")]
    [InlineData("(unclosed")]   // bad pattern: nothing, never a throw
    [InlineData("x*")]          // empty matches only
    public void NoMatchOrBadPatternFindsNothing(string pattern)
        => Assert.Null(KeywordTriggerService.FindRegexMatchedWords(pattern, Scan(("hello", 0), ("there", 50))));
}
