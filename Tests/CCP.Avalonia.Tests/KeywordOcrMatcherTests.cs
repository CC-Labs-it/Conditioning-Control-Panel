using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class KeywordOcrMatcherTests
{
    private static readonly DateTime Time = new(2026, 10, 14, 12, 0, 0);
    private static KeywordOcrHit Hit(string text = "sit", int x = 0) => new(text, new Rectangle(x, 0, 20, 10));
    private static AppSettings Settings() => new() { KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = "sit", CooldownSeconds = 0 } }, OcrConfirmationScans = 2, OcrHighlightAll = true };

    [Fact]
    public void WholeWordsAndPhrasesUseTokenBoundariesAndUnionRectangles()
    {
        Assert.Empty(KeywordOcrMatcher.WholeMatches("sit", new[] { Hit("sitting") }));
        Assert.Single(KeywordOcrMatcher.WholeMatches("sit", new[] { Hit("'SIT!'") }));
        var phrase = Assert.Single(KeywordOcrMatcher.WholeMatches("sit down", new[] { Hit("sit", 10), Hit("down!", 40) }));
        Assert.Equal(new Rectangle(10, 0, 50, 10), phrase.ScreenRect);
    }

    [Fact]
    public void RegexMapsMatchesToWordsAndRejectsInvalidOrEmptyPatterns()
    {
        var words = new[] { Hit("sit", 10), Hit("down", 40) };
        Assert.Equal(new Rectangle(10, 0, 50, 10), Assert.Single(KeywordOcrMatcher.RegexMatches("sit down", words)).ScreenRect);
        Assert.Empty(KeywordOcrMatcher.RegexMatches("[", words));
        Assert.Empty(KeywordOcrMatcher.RegexMatches("^", words));
    }

    [Fact]
    public void ScanSelectsRegexAndPresetBeforeCustom()
    {
        var s = Settings(); s.OcrConfirmationScans = 1;
        s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:regex", Keyword = "s.t", MatchType = KeywordMatchType.Regex });
        var result = new KeywordOcrMatcher().Scan(new[] { Hit("sit") }, s, new KeywordCooldowns(), Time);
        Assert.Equal("preset:regex", result.Triggers[0].Id);
        Assert.Equal(2, result.Triggers.Count);
        Assert.Single(result.Hits);
    }

    [Fact]
    public void ConfirmationPositionGuardAndEmptyScanReset()
    {
        var matcher = new KeywordOcrMatcher(); var s = Settings(); var cooldowns = new KeywordCooldowns();
        Assert.Empty(matcher.Scan(new[] { Hit() }, s, cooldowns, Time).Hits);
        Assert.True(matcher.NeedsConfirmation);
        Assert.Single(matcher.Scan(new[] { Hit() }, s, cooldowns, Time).Hits);
        Assert.Empty(matcher.Scan(new[] { Hit() }, s, cooldowns, Time).Hits);
        Assert.Empty(matcher.Scan(new[] { Hit(x: 240) }, s, cooldowns, Time).Hits);
        Assert.Single(matcher.Scan(new[] { Hit(x: 240) }, s, cooldowns, Time).Hits);
        matcher.Scan(Array.Empty<KeywordOcrHit>(), s, cooldowns, Time);
        Assert.Empty(matcher.Scan(new[] { Hit(x: 240) }, s, cooldowns, Time).Hits);
        Assert.True(matcher.NeedsConfirmation);
    }

    [Fact]
    public void CooldownAndEchoMuteFilterMatches()
    {
        var matcher = new KeywordOcrMatcher(); var s = Settings(); s.OcrConfirmationScans = 1;
        var cd = new KeywordCooldowns { LastGlobal = Time };
        Assert.Empty(matcher.Scan(new[] { Hit() }, s, cd, Time).Hits);
        cd.LastGlobal = DateTime.MinValue; cd.Mute("sit", 10000, Time.ToUniversalTime());
        Assert.Empty(matcher.Scan(new[] { Hit() }, s, cd, Time).Hits);
        s.KeywordTriggers[0].LastTriggeredAt = Time; s.KeywordTriggers[0].CooldownSeconds = 30;
        Assert.Empty(matcher.Scan(new[] { Hit() }, s, cd, Time.AddSeconds(11)).Hits);
        Assert.Single(matcher.Scan(new[] { Hit() }, s, cd, Time.AddSeconds(31)).Hits);
    }

    [Fact]
    public void RuntimeGatesAndBlockedScanResetStreak() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope, s.OcrConfirmationScans, s.OcrHighlightAll, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        var runtime = new KeywordAudioRuntime { Now = () => Time, QuestCredit = () => { } };
        try
        {
            s.KeywordTriggers = Settings().KeywordTriggers; s.KeywordTriggersEnabled = true; s.OcrConfirmationScans = 2; s.OcrHighlightAll = true;
            s.KeywordTriggerIgnoreOwnFocus = true; s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            CoreEntitlement.HasPremiumProvider = () => true; CoreEntitlement.IsFreeTodayProvider = _ => false;
            runtime.Start(); Assert.Empty(runtime.CheckOcrWords(new[] { Hit() }, "other")); Assert.True(runtime.NeedsOcrConfirmation);
            Assert.Empty(runtime.CheckOcrWords(new[] { Hit() }, "own", true)); Assert.False(runtime.NeedsOcrConfirmation);
            Assert.Empty(runtime.CheckOcrWords(new[] { Hit() }, "other"));
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.Empty(runtime.CheckOcrWords(new[] { Hit() }, "other")); Assert.False(runtime.NeedsOcrConfirmation);
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.Empty(runtime.CheckOcrWords(new[] { Hit() }, "other"));
            Assert.Single(runtime.CheckOcrWords(new[] { Hit() }, "other"));
            Assert.Equal("OCR", Assert.Single(runtime.GetRecentFires()).Source);
        }
        finally
        {
            runtime.Stop();
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope, s.OcrConfirmationScans, s.OcrHighlightAll, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
        return System.Threading.Tasks.Task.CompletedTask;
    }).GetAwaiter().GetResult();
}
