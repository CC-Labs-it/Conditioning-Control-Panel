using System;
using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class KeywordTextInputTests
{
    private static readonly DateTime Time = new(2026, 10, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void BufferCapsCharactersTimesOutAndEditsLikeWpf()
    {
        var input = new KeywordTextInput();
        for (var i = 0; i < 205; i++) input.Append('a', Time, 1000);
        Assert.Equal(200, input.Text.Length);
        input.Append('b', Time.AddMilliseconds(1001), 1000);
        Assert.Equal("b", input.Text);
        Assert.False(input.Key(KeywordInputKey.Backspace, Time.AddMilliseconds(1002), 1000));
        Assert.Equal("", input.Text);
        Assert.True(input.Key(KeywordInputKey.Space, Time.AddMilliseconds(1003), 1000));
        Assert.Equal(" ", input.Text);
        Assert.False(input.Key(KeywordInputKey.Clear, Time.AddMilliseconds(1004), 1000));
        Assert.Equal("", input.Text);
    }

    [Theory]
    [InlineData("intensity", "sit", false)]
    [InlineData("SIT!", "sit", true)]
    [InlineData("good\t  girl", "good girl", true)]
    [InlineData("hello [word]", "[word]", false)]
    [InlineData("Sit down", "sit", true)]
    public void PlainTextUsesWpfWholeWordSemantics(string text, string keyword, bool matches) =>
        Assert.Equal(matches, KeywordMatchRules.ContainsWholeWord(text, keyword));

    [Fact]
    public void CollectOrdersPresetsAndHonorsEnabledCooldownMuteAndRegex()
    {
        var custom = new KeywordTrigger { Id = "custom", Keyword = "good girl", CooldownSeconds = 30 };
        var preset = new KeywordTrigger { Id = "preset:one", Keyword = @"g[io]rl", MatchType = KeywordMatchType.Regex };
        var list = new List<KeywordTrigger> { custom, preset,
            new() { Keyword = "good", Enabled = false },
            new() { Keyword = "girl", LastTriggeredAt = Time, CooldownSeconds = 30 },
            new() { Keyword = "[", MatchType = KeywordMatchType.Regex },
            new() { Keyword = "good", Id = "muted" } };
        Assert.Equal(new[] { preset, custom }, KeywordMatchRules.Collect(list, "good girl", Time,
            word => word == "good"));
        Assert.False(KeywordMatchRules.Match("anything", "["));
    }

    [Fact]
    public void MuteIsCaseInsensitiveAlwaysAppliesHardCooldownAndOnlyExtends()
    {
        var state = new KeywordCooldowns();
        var settings = new AppSettings { AwarenessLoopProtectionEnabled = false, KeywordPerKeywordCooldownSeconds = 15 };
        var trigger = new KeywordTrigger { Keyword = "sit" };
        state.Record(trigger, settings, Time);
        state.Mute("SIT", 100, Time.AddSeconds(1));
        Assert.True(state.IsMuted("Sit", Time.AddSeconds(14)));
        Assert.False(state.IsMuted("sit", Time.AddSeconds(15)));
        settings.AwarenessLoopProtectionEnabled = true;
        settings.AwarenessLoopProtectionMs = 20000;
        state.Record(trigger, settings, Time);
        Assert.True(state.IsMuted("sit", Time.AddSeconds(19)));
        Assert.False(state.IsMuted("sit", Time.AddSeconds(20)));
    }

    [Theory]
    [InlineData(AwarenessAppScope.Everywhere, false, null, false, true)]
    [InlineData(AwarenessAppScope.OnlyListed, false, null, false, false)]
    [InlineData(AwarenessAppScope.ExceptListed, false, null, false, true)]
    [InlineData(AwarenessAppScope.OnlyListed, false, "editor", false, true)]
    [InlineData(AwarenessAppScope.ExceptListed, false, "editor", false, false)]
    [InlineData(AwarenessAppScope.Everywhere, true, "editor", true, false)]
    public void ScopeFailsClosedForUnknownAllowList(AwarenessAppScope scope, bool ignoreOwn,
        string? process, bool own, bool allowed)
    {
        var settings = new AppSettings { KeywordTriggerAppScope = scope, KeywordTriggerIgnoreOwnFocus = ignoreOwn,
            KeywordTriggerApps = new List<string> { " Editor.EXE " } };
        Assert.Equal(allowed, KeywordMatchRules.AppAllowed(settings, process, own));
    }

    [Fact]
    public void AudioUsesWpfMasterCurveAndFivePercentFloor()
    {
        Assert.Equal(0.125f, KeywordMatchRules.AudioVolume(50, 50));
        Assert.Equal(0.05f, KeywordMatchRules.AudioVolume(0, 100));
    }
}
