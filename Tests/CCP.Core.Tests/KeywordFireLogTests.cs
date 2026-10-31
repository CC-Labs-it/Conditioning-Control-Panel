using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class KeywordFireLogTests
{
    [Fact]
    public void HistoryIsNewestFirstBoundedAndCapturedBeforeEdits()
    {
        var log = new KeywordFireLog();
        var at = new DateTime(2026, 10, 31);
        var trigger = new KeywordTrigger { Id = "one", Keyword = "sit", Actions = new()
        {
            new PlayAudioAction(), new HighlightAction { Enabled = false }, new AddXpAction(),
            new HapticAction(), new AvatarCommentAction(), new ExtendSessionAction { Minutes = 5 },
            new ChasterAddTimeAction { Minutes = 3 }, new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }
        } };
        log.Record(trigger, "Keyboard", at);
        var snapshot = log.Recent();
        trigger.Keyword = "stay"; trigger.Actions.Clear();
        Assert.Equal("sit", snapshot[0].Keyword);
        Assert.Equal("one", snapshot[0].TriggerId);
        Assert.Equal(at, snapshot[0].FiredAt);
        Assert.Equal("Keyboard", snapshot[0].Source);
        Assert.Equal(new[] { "PlayAudio", "Haptic", "AvatarComment", "ExtendSession:5", "ChasterAddTime:3", "VisualEffect:ImageFlash" }, snapshot[0].ActionKeys);
        for (var i = 0; i < 21; i++)
        {
            trigger.Id = i.ToString();
            log.Record(trigger, "Tutorial", at.AddSeconds(i));
        }
        Assert.Single(snapshot); // readers hold a stable list, not the live ring
        var recent = log.Recent();
        Assert.Equal(20, recent.Count);
        Assert.Equal("20", recent[0].TriggerId);
        Assert.Equal("1", recent[19].TriggerId);
        Assert.Equal("Tutorial", recent[0].Source);
        Assert.Empty(recent[0].ActionKeys);
    }

    [Fact]
    public void LegacyNullActionsHaveNoDisplayChips()
    {
        var log = new KeywordFireLog();
        log.Record(new KeywordTrigger { Actions = null! }, "Keyboard", DateTime.Now);
        Assert.Empty(Assert.Single(log.Recent()).ActionKeys);
    }
}
