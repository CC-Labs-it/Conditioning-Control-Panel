using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class KeywordCreditTests
{
    private static readonly PropertyInfo Achievements = typeof(AvApp).GetProperty("Achievements", BindingFlags.Static | BindingFlags.NonPublic)!;

    private static Task Run(Action<KeywordAudioRuntime, AppSettings, AchievementEngine, QuestService> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            var s = CoreSettings.Current;
            var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.AwarenessLoopProtectionEnabled,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, AvApp.Quests, Achievements.GetValue(null));
            var dir = Directory.CreateTempSubdirectory("ccp-keyword-credit-").FullName;
            var quests = new QuestService(null, dir);
            var achievement = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json"))) { SuppressPopups = true };
            var engine = new KeywordAudioRuntime();
            try
            {
                AvApp.Quests = quests;
                Achievements.SetValue(null, achievement);
                quests.Progress.DailyQuests = new List<ActiveQuest> { new("trigger_words_d") };
                CoreEntitlement.HasPremiumProvider = () => true;
                CoreEntitlement.IsFreeTodayProvider = _ => false;
                s.KeywordTriggersEnabled = true;
                s.KeywordTriggerIgnoreOwnFocus = false;
                s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
                s.KeywordGlobalCooldownSeconds = s.KeywordPerKeywordCooldownSeconds = 0;
                s.AwarenessLoopProtectionEnabled = false;
                PanicAftermath.EndQuiet();
                engine.Start();
                body(engine, s, achievement, quests);
            }
            finally
            {
                engine.Stop(); KeywordAvatarComment.CancelPending(); PanicAftermath.EndQuiet();
                (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                    s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.AwarenessLoopProtectionEnabled,
                    CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, AvApp.Quests, _) = saved;
                Achievements.SetValue(null, saved.Item11);
                quests.Dispose();
                Directory.Delete(dir, true);
            }
            return Task.CompletedTask;
        });

    [Fact]
    public Task TypedMergedFiresCountAchievementsBeforeQuietAndQuestsAfterQuiet() => Run((engine, s, achievement, quests) =>
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0);
        engine.Now = () => now;
        s.KeywordTriggers = new() {
            new() { Keyword = "sit", CooldownSeconds = 1, Actions = new() { new HighlightAction() } },
            new() { Keyword = "sit", CooldownSeconds = 1, Actions = new() { new HighlightAction() } } };
        foreach (var c in "sit") engine.Character(c);
        Assert.Equal(2, achievement.Progress.KeywordTriggersFired);
        Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
        now = now.AddSeconds(2);
        PanicAftermath.ArmQuiet();
        foreach (var c in "sit") engine.Character(c);
        Assert.Equal(4, achievement.Progress.KeywordTriggersFired);
        Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task QuestListenerFailureDoesNotEscapeDispatch(bool demo) => Run((engine, s, achievement, quests) =>
    {
        s.KeywordTriggers = new() { new() { Keyword = "sit", Actions = new() { new HighlightAction() } } };
        quests.QuestProgressChanged += (_, _) => throw new InvalidOperationException("test listener");
        if (demo) engine.FireDemo("other", "Tutorial");
        else foreach (var c in "sit") engine.Character(c);
        Assert.Equal(1, achievement.Progress.KeywordTriggersFired);
        Assert.Equal(1, quests.Progress.DailyQuests[0].CurrentProgress);
    });

    [Fact]
    public Task DemoCreditsDespiteMasterAndQuietButRequiresAccessAndRecordsMute() => Run((engine, s, achievement, quests) =>
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0);
        engine.Now = () => now;
        s.KeywordTriggersEnabled = false;
        s.KeywordPerKeywordCooldownSeconds = 15;
        s.KeywordTriggers = new() { new() { Keyword = "sit", CooldownSeconds = 1, Actions = new() { new HighlightAction() } } };
        PanicAftermath.ArmQuiet();
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.FireDemo("sit", "Tutorial");
        Assert.Equal(0, achievement.Progress.KeywordTriggersFired);
        CoreEntitlement.IsFreeTodayProvider = _ => true;
        engine.FireDemo(" ", "Tutorial");
        Assert.Equal(0, achievement.Progress.KeywordTriggersFired);
        engine.FireDemo("sit", "Tutorial");
        Assert.Equal(1, achievement.Progress.KeywordTriggersFired);
        Assert.False(achievement.Progress.IsUnlocked("magic_word"));
        Assert.Equal(1, quests.Progress.DailyQuests[0].CurrentProgress);
        s.KeywordTriggersEnabled = true;
        PanicAftermath.EndQuiet();
        foreach (var c in "sit") engine.Character(c);
        Assert.Equal(1, achievement.Progress.KeywordTriggersFired);
        now = now.AddSeconds(15);
        engine.Key(KeywordInputKey.Clear);
        foreach (var c in "sit") engine.Character(c);
        Assert.Equal(2, achievement.Progress.KeywordTriggersFired);
        Assert.Equal(2, quests.Progress.DailyQuests[0].CurrentProgress);
    });
}
