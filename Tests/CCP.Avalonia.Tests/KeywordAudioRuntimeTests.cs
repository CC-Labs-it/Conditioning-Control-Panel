using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using AvApp = ConditioningControlPanel.Avalonia.App;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class KeywordAudioRuntimeTests
{
    private static void Run(Action<KeywordAudioRuntime, AppSettings, List<(string Path, float Volume)>> body) =>
        RunAsync((engine, settings, played) => { body(engine, settings, played); return Task.CompletedTask; }).GetAwaiter().GetResult();

    private static Task RunAsync(Func<KeywordAudioRuntime, AppSettings, List<(string Path, float Volume)>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
            s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled);
        var engine = new KeywordAudioRuntime();
        var played = new List<(string, float)>();
        var file = Path.GetTempFileName();
        try
        {
            s.KeywordTriggers = new List<KeywordTrigger> { new() { Keyword = "sit", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = file, Volume = 50, DuckSystemAudio = false } } } };
            s.KeywordTriggersEnabled = true; s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordGlobalCooldownSeconds = 0; s.KeywordPerKeywordCooldownSeconds = 0; s.MasterVolume = 50;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            CoreAudio.PlayStoppableProvider = (path, volume, _, _, _) => { played.Add((path, volume)); return () => played.Add(("stopped", 0)); };
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Start();
            await body(engine, s, played);
        }
        finally
        {
            engine.Stop(); KeywordTriggerHead.Stop(); File.Delete(file);
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled) = saved;
        }
    });

    private static void Type(KeywordAudioRuntime engine, string text) { foreach (var ch in text) engine.Character(ch); }

    [Fact]
    public void TypedMatchPlaysConfiguredClipAtWpfVolumeAndStopCutsIt() => Run((engine, s, played) =>
    {
        Type(engine, "intensity"); Assert.Empty(played);
        engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
        Type(engine, "SIT");
        Assert.Equal(0.125f, Assert.Single(played).Volume);
        engine.Stop();
        Assert.Equal("stopped", played.Last().Path);
        Type(engine, "sit"); Assert.Equal(2, played.Count);
    });

    [Fact]
    public void LiveMasterAndEntitlementGatesPreventPlaybackAndPresetAudioWins() => Run((engine, s, played) =>
    {
        CoreEntitlement.HasPremiumProvider = () => false;
        Type(engine, "sit"); Assert.Empty(played);
        CoreEntitlement.IsFreeTodayProvider = _ => true;
        s.KeywordTriggersEnabled = false;
        Type(engine, "sit"); Assert.Empty(played);
        s.KeywordTriggersEnabled = true;
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:one", Keyword = "sit", Actions = new List<KeywordAction> {
            new PlayAudioAction { FilePath = audio.FilePath, Volume = 100, DuckSystemAudio = false } } });
        Type(engine, "sit");
        Assert.Equal((float)Math.Pow(0.5, 1.5), Assert.Single(played).Volume);
    });

    [Fact]
    public void KeyboardMatchesCreditEachDailyAndWeeklyQuestOncePerTrigger() => Run((engine, s, played) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-keyword-quest-").FullName;
        var oldQuests = AvApp.Quests;
        var quests = new QuestService(null, dir);
        try
        {
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("trigger_words_d"), new("word_slave_d") };
            quests.Progress.WeeklyQuest = new ActiveQuest("pavlov_w");
            AvApp.Quests = quests;
            var events = 0;
            quests.QuestProgressChanged += (_, _) => events++;
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:one", Keyword = "sit", CooldownSeconds = 0 });
            s.KeywordPerKeywordCooldownSeconds = 10;
            s.AwarenessLoopProtectionEnabled = false;

            Type(engine, "sit");
            Assert.All(quests.Progress.DailyQuests, q => Assert.Equal(2, q!.CurrentProgress));
            Assert.Equal(2, quests.Progress.WeeklyQuest.CurrentProgress);
            Assert.Equal(6, events);
            Assert.Single(played); // merged audio dispatch stays single
            Type(engine, "sit"); // per-keyword mute
            Assert.Equal(2, quests.Progress.WeeklyQuest.CurrentProgress);
            engine.FireDemo("sit", "tutorial");
            Assert.Equal(3, quests.Progress.WeeklyQuest.CurrentProgress);
            CoreEntitlement.HasPremiumProvider = () => false;
            Type(engine, "sit");
            Assert.Equal(3, quests.Progress.WeeklyQuest.CurrentProgress);
        }
        finally
        {
            AvApp.Quests = oldQuests;
            quests.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public void PanicQuietSuppressesKeyboardDispatchAndQuestCredit() => Run((engine, s, played) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-keyword-quiet-").FullName;
        var oldQuests = AvApp.Quests;
        var quests = new QuestService(null, dir);
        try
        {
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("trigger_words_d") };
            AvApp.Quests = quests;
            s.AwarenessLoopProtectionEnabled = false;
            PanicAftermath.ArmQuiet();
            Type(engine, "sit");
            Assert.Empty(played);
            Assert.Equal(0, quests.Progress.DailyQuests[0]!.CurrentProgress);
            PanicAftermath.EndQuiet();
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 2); // WPF records the suppressed match's cooldown
            Type(engine, "sit");
            Assert.Single(played);
            Assert.Equal(1, quests.Progress.DailyQuests[0]!.CurrentProgress);
        }
        finally
        {
            PanicAftermath.EndQuiet();
            AvApp.Quests = oldQuests;
            quests.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]
    public void TutorialDemoCreditsOnlyAcceptedFiresIncludingDuringPanicQuiet() => Run((engine, s, played) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-demo-quest-").FullName;
        var oldQuests = AvApp.Quests;
        var quests = new QuestService(null, dir);
        try
        {
            quests.Progress.DailyQuests = new List<ActiveQuest> { new("trigger_words_d") };
            AvApp.Quests = quests;
            CoreEntitlement.HasPremiumProvider = () => false;
            engine.FireDemo("sit", "Tutorial");
            Assert.Equal(0, quests.Progress.DailyQuests[0]!.CurrentProgress);
            CoreEntitlement.HasPremiumProvider = () => true;
            engine.FireDemo(" ", "Tutorial");
            Assert.Equal(0, quests.Progress.DailyQuests[0]!.CurrentProgress);
            engine.Stop();
            s.KeywordTriggersEnabled = false;
            engine.FireDemo("sit", "Tutorial");
            Assert.Equal(1, quests.Progress.DailyQuests[0]!.CurrentProgress);
            PanicAftermath.ArmQuiet();
            engine.FireDemo("sit", "Tutorial");
            Assert.Equal(2, quests.Progress.DailyQuests[0]!.CurrentProgress);
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.IsFreeTodayProvider = _ => true;
            engine.FireDemo("sit", "Tutorial");
            Assert.Equal(3, quests.Progress.DailyQuests[0]!.CurrentProgress);
            Assert.Empty(played);
        }
        finally
        {
            PanicAftermath.EndQuiet();
            AvApp.Quests = oldQuests;
            quests.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]   // WPF FireDemoTrigger never reads saved triggers or plays audio (#887 back door)
    public void TutorialDemoNeverPlaysTheSavedTriggerAudio() => Run((engine, _, played) =>
    {
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.FireDemo("sit", "tutorial");
        CoreEntitlement.HasPremiumProvider = () => true;
        engine.FireDemo("sit", "tutorial");
        Assert.Empty(played);
    });

    [Fact]
    public void StopCancelsPendingAudioRepeats() => Run((engine, s, played) =>
    {
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.PlayCount = 2; audio.DelayBetweenMs = 50;
        var gap = new TaskCompletionSource();   // P08: the repeat gap ends when the test says so, not on a timer
        engine.Delay = (_, _) => gap.Task;
        Type(engine, "sit"); Assert.Single(played);
        engine.Stop();
        gap.SetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(played.Where(x => x.Path != "stopped"));
        Assert.Equal("stopped", played.Last().Path);
    });

    [Fact]
    public void GlobalCooldownAndHardMuteApplyAcrossRepeatedMatches() => Run((engine, s, played) =>
    {
        var now = new DateTime(2026, 10, 14, 12, 0, 0);
        engine.Now = () => now;
        s.AwarenessLoopProtectionEnabled = false;
        s.KeywordGlobalCooldownSeconds = 5;
        s.KeywordPerKeywordCooldownSeconds = 15;
        Type(engine, "sit"); Assert.Single(played);
        now = now.AddSeconds(6);
        Type(engine, "sit"); Assert.Single(played); // global expired, word still muted
        now = now.AddSeconds(9);
        Type(engine, "sit");
        Assert.Equal(2, played.Count(x => x.Path != "stopped"));
    });

    [Fact]
    public void BlockedAppDropsThePartialBuffer() => Run((engine, s, played) =>
    {
        s.KeywordTriggerIgnoreOwnFocus = true;
        engine.Character('s', "editor", false);
        engine.Character('i', "ccp", true);
        engine.Character('t', "editor", false);
        Assert.Empty(played);
        engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
        Type(engine, "sit"); Assert.Single(played);
    });

    [Fact]
    public void AwarenessSwitchStartsRuntimeAndPanicCutsOnlyTheClip() => Run((_, s, played) =>
    {
        // Linux headless test deliberately never installs a native keyboard hook.
        s.KeywordTriggersEnabled = false;
        var clock = new DateTime(2026, 10, 14, 12, 0, 0);
        var realNow = KeywordTriggerHead.Engine.Now;
        KeywordTriggerHead.Engine.Now = () => clock;   // the re-type below must not depend on wall-clock cooldowns
        try
        {
        var view = new AwarenessTabView();
        view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = true;
        Assert.True(KeywordTriggerHead.Engine.IsActive);
        Type(KeywordTriggerHead.Engine, "sit");
        Assert.Single(played);
        PanicSurfaces.All.Single(x => x.Id == "keyword-triggers").Stop(null);
        Assert.Equal("stopped", played.Last().Path);
        Assert.True(KeywordTriggerHead.Engine.IsActive);   // WPF panic leaves the service running
        clock = clock.AddHours(2);   // the view clamps cooldowns to AwarenessCooldownScale (1 s..1 h); step past them
        Type(KeywordTriggerHead.Engine, "sit"); Assert.Equal(3, played.Count);
        // WPF MainWindow.Awareness.cs:493: Keyboard off drops the key source only; engine and clip stay.
        view.FindControl<CheckBox>("ChkAwarenessKeyboard")!.IsChecked = false;
        Assert.True(KeywordTriggerHead.Engine.IsActive);
        Assert.NotEqual("stopped", played.Last().Path);
        view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = false;
        }
        finally { KeywordTriggerHead.Engine.Now = realNow; }
    });

    [Fact]
    public void SavedOnSwitchArmsTheRuntimeWhenTheShellStarts() => Run((_, s, _) =>
    {
        KeywordTriggerHead.Stop();
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        _ = new MainShellWindow();   // WPF MainWindow.xaml.cs:341 starts the service at boot
        Assert.True(KeywordTriggerHead.Engine.IsActive);
    });
}
