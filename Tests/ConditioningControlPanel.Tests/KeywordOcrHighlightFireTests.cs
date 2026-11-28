using System.Collections.Generic;
using System.Drawing;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1669: a keyword's sound follows its highlight. Before the fix every trigger with a
/// word anywhere on screen fired as soon as ONE new word was highlighted, so a keyword already
/// highlighted on an earlier scan (or still waiting for confirmation) played again with nothing
/// new on screen.
/// </summary>
public class KeywordOcrHighlightFireTests
{
    private static OcrWordHit Word(string text, int x, int y = 100) =>
        new() { Text = text, ScreenRect = new Rectangle(x, y, 40, 20) };

    [Fact]
    public void OnlyTheTriggerWhoseWordIsNewlyHighlightedFires()
    {
        var obey = new KeywordTrigger { Keyword = "obey" };
        var drop = new KeywordTrigger { Keyword = "drop" };
        var obeyWord = Word("obey", 0);
        var dropWord = Word("drop", 600);
        var hits = new List<(KeywordTrigger, List<OcrWordHit>)>
        {
            (obey, new List<OcrWordHit> { obeyWord }),   // highlighted on an earlier scan
            (drop, new List<OcrWordHit> { dropWord }),   // new this scan
        };
        var newKeys = new HashSet<string> { KeywordTriggerService.OcrPositionKey(dropWord) };

        var fired = KeywordTriggerService.TriggersWithNewHighlights(hits, newKeys);

        Assert.Same(drop, Assert.Single(fired));
    }

    [Fact]
    public void NoNewHighlight_NothingFires()
    {
        var obey = new KeywordTrigger { Keyword = "obey" };
        var hits = new List<(KeywordTrigger, List<OcrWordHit>)> { (obey, new List<OcrWordHit> { Word("obey", 0) }) };

        Assert.Empty(KeywordTriggerService.TriggersWithNewHighlights(hits, new HashSet<string>()));
    }

    [Fact]
    public void ATriggerFiresOnceEvenWithSeveralNewWords()
    {
        var obey = new KeywordTrigger { Keyword = "obey" };
        var a = Word("obey", 0);
        var b = Word("OBEY", 0, 400);
        var hits = new List<(KeywordTrigger, List<OcrWordHit>)> { (obey, new List<OcrWordHit> { a, b }) };
        var newKeys = new HashSet<string> { KeywordTriggerService.OcrPositionKey(a), KeywordTriggerService.OcrPositionKey(b) };

        Assert.Single(KeywordTriggerService.TriggersWithNewHighlights(hits, newKeys));
    }

    [Fact]
    public void ThePositionKeyIgnoresCaseAndSmallReflow()
    {
        Assert.Equal(KeywordTriggerService.OcrPositionKey(Word("Obey", 10)),
            KeywordTriggerService.OcrPositionKey(Word("obey", 50)));
        Assert.NotEqual(KeywordTriggerService.OcrPositionKey(Word("obey", 10)),
            KeywordTriggerService.OcrPositionKey(Word("obey", 500)));
    }
}
