using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

public sealed class KeywordResponseRulesTests
{
    [Fact]
    public void MergeKeepsPriorityAndOneSlotPerTypeButDistinctVisualEffects()
    {
        var presetAudio = new PlayAudioAction { FilePath = "preset.wav" };
        var xp = new AddXpAction { Amount = 7 };
        var triggers = new List<KeywordTrigger>
        {
            new() { Actions = new() { new HapticAction { Enabled = false }, presetAudio, xp,
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }, new ChasterAddTimeAction { Minutes = 1 } } },
            new() { Actions = new() { new PlayAudioAction(), new AddXpAction(),
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash },
                new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal }, new ChasterAddTimeAction { Minutes = 9 } } },
        };
        var actions = KeywordResponseRules.Actions(triggers);
        Assert.Equal(5, actions.Count);
        Assert.Same(presetAudio, actions[0]); Assert.Same(xp, actions[1]);
        Assert.Equal(KeywordVisualEffect.ImageFlash, Assert.IsType<VisualEffectAction>(actions[2]).Effect);
        Assert.Equal(1, Assert.IsType<ChasterAddTimeAction>(actions[3]).Minutes);
        Assert.Equal(KeywordVisualEffect.ExactSubliminal, Assert.IsType<VisualEffectAction>(actions[4]).Effect);
    }

    [Fact]
    public void SingleTriggerPreservesRepeatedActionsAndFiltersDisabledOnes()
    {
        var trigger = new KeywordTrigger { Actions = new() { new AddXpAction(), new AddXpAction(), new HapticAction { Enabled = false } } };
        Assert.Equal(2, KeywordResponseRules.Actions(new[] { trigger }).Count);
        Assert.Equal(3, trigger.Actions.Count);
    }

    [Fact]
    public void LegacyHighlightOnlySkipsAudioHapticsAndXp()
    {
        var trigger = new KeywordTrigger { VisualEffect = KeywordVisualEffect.HighlightOnly, AudioFilePath = "clip.wav" };
        Assert.IsType<HighlightAction>(Assert.Single(KeywordResponseRules.Actions(new[] { trigger })));
        trigger.VisualEffect = KeywordVisualEffect.ImageFlash;
        trigger.AudioPlayCount = 3; trigger.AudioVolume = 42; trigger.HapticIntensity = 0.7;
        var actions = KeywordResponseRules.Actions(new[] { trigger });
        Assert.Equal(3, Assert.IsType<PlayAudioAction>(actions[0]).PlayCount);
        Assert.Equal(42, Assert.IsType<PlayAudioAction>(actions[0]).Volume);
        Assert.Equal(0.7, Assert.IsType<HapticAction>(actions[3]).Intensity);
        Assert.IsType<AddXpAction>(actions[4]);
        Assert.Empty(trigger.Actions);
    }

    [Fact]
    public void MergedLegacyTriggerDoesNotSynthesizeActionsUnlikeSingleFallback()
    {
        Assert.Empty(KeywordResponseRules.Actions(new[] { new KeywordTrigger(), new KeywordTrigger() }));
        Assert.Empty(KeywordResponseRules.Actions(new List<KeywordTrigger>()));
    }

    [Fact]
    public void AppScopeParserStripsExeSplitsAndDeduplicatesWithoutReordering()
    {
        Assert.Equal(new[] { "Editor", "browser", "player" },
            KeywordMatchRules.ParseAppList(" Editor.EXE, browser;editor\r\nplayer.exe; .exe ; BROWSER"));
        Assert.Empty(KeywordMatchRules.ParseAppList(null));
        Assert.True(KeywordMatchRules.MatchesAppList(KeywordMatchRules.ParseAppList("Editor.exe"), "editor"));
    }

}
