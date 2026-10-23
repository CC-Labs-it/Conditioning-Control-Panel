using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The companion / bark head adapters: App startup starts both, Start seeds every seam WPF pointed at
/// App.Companion / App.Bark over the shipped rules, and the CoreBark doorbell reaches the engine.</summary>
public sealed class CompanionBarkHeadTests
{
    [Fact]
    public void AppStartupStartsBothHeadsOnceQuestsExist()
    {
        var src = Regex.Replace(File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")),
            @"//[^\n]*|/\*.*?\*/|@?""(?:[^""\\]|\\.)*""", " ", RegexOptions.Singleline);
        Assert.Matches(@"StartQuests\(\);\s*Platform\.CompanionHead\.Start\(\);\s*Platform\.BarkHead\.Start\(\);", src);
    }

    [Fact]
    public void StartSeedsTheSeamsAndTheDoorbellReachesTheEngine() => WithSeamsRestored(s =>
    {
        bool ownCompanion = !CompanionHead.Started, ownBark = BarkHead.Engine == null;
        try
        {
            CompanionHead.Start();
            BarkHead.Start();
            Assert.True(CompanionHead.Started);
            Assert.Equal(s.ActiveCompanionId, (int)CoreModsHooks.ActiveCompanionProvider!()!);
            Assert.NotNull(CoreModsHooks.SwitchCompanion);
            Assert.NotNull(CoreModsHooks.ReloadBarkRules);
            Assert.NotNull(CoreSettingsHooks.SettingChangedSink);
            Assert.NotNull(CoreBark.AvatarClicked);
            Assert.True(BarkHead.Engine!.RuleCount > 0);   // the shipped bark_rules.json
            Assert.NotEmpty(CoreBark.AllLines);            // the Phrase Manager's bark rows

            var e = Seeded(Rule("UiAction", "bye", ("action", "minimize")), out var spoken);
            CoreBark.NotifyUiAction("minimize");
            Assert.Equal("bye", spoken());
        }
        finally
        {
            if (ownCompanion) CompanionHead.Stop();
            if (ownBark) BarkHead.Stop();
        }
    });

    [Fact]   // WPF MainWindow.xaml.cs:1755: a ladder-mode panic press raises her safety line before the stop pass
    public void ALadderPanicPressRaisesThePanicBark() => WithSeamsRestored(s =>
    {
        var old = (BarkHead.Engine, s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll);
        var rule = Rule("Panic", "breathe");
        rule.ClassRaw = "safety";
        BarkHead.Engine = Seeded(rule, out var spoken);
        var shell = new MainShellWindow();
        try
        {
            (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll) = (true, "F8", false);
            shell.HandlePanicKeyPress(new System.DateTime(2026, 1, 1, 12, 0, 0));
            Assert.Equal("breathe", spoken());
        }
        finally
        {
            shell.Close();
            (BarkHead.Engine, s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll) = old;
        }
    });

    [Fact]   // WPF BarkService.cs:728-733: a bouncing-text wall bounce and a corner hit each raise their bark
    public void ABounceAndACornerHitEachRaiseTheirBark() => WithSeamsRestored(_ =>
    {
        var old = (BarkHead.Engine, BouncingTextOverlay.CornerHit);
        var eng = BouncingTextOverlay.Engine;
        try
        {
            BouncingTextOverlay.CornerHit = () => { };
            BouncingTextOverlay.Stop();
            eng.SetBounds(0, 0, 1000, 800);
            eng.Measure = (_, _) => (200, 60);
            eng.Start(new ConditioningControlPanel.Models.AppSettings());
            var l = eng.Logos[0];

            BarkHead.Engine = Seeded(Rule("BouncingTextBounce", "boing"), out var spoken);
            (l.PosX, l.PosY, l.VelX, l.VelY) = (400, 300, 100, 100);
            BouncingTextOverlay.Advance(0.05);                       // mid-screen: no bounce, no bark
            Assert.Null(spoken());
            (l.PosX, l.PosY, l.VelX, l.VelY) = (2, 300, -100, 0);
            BouncingTextOverlay.Advance(0.05);                       // left wall
            Assert.Equal("boing", spoken());

            BarkHead.Engine = Seeded(Rule("BouncingTextCorner", "corner!"), out spoken);   // fresh engine: the 60 s gap is per engine
            (l.PosX, l.PosY, l.VelX, l.VelY) = (2, 2, -100, -100);
            BouncingTextOverlay.Advance(0.05);                       // both walls: a corner hit
            Assert.Equal("corner!", spoken());
        }
        finally
        {
            BouncingTextOverlay.Stop();
            eng.Measure = null;
            (BarkHead.Engine, BouncingTextOverlay.CornerHit) = old;
        }
    });

    [Theory]
    [InlineData("flash", "FlashDisplayed")]
    [InlineData("bubbles", "BubblePopped")]
    public void FeatureEventsReachTheMouthAndStopUnsubscribes(string feature, string trigger) => WithSeamsRestored(_ =>
    {
        var rule = Rule(trigger, "reaction"); rule.ClassRaw = "safety";
        var e = Seeded(rule, out var spoken);
        var count = 0;
        e.Speak = _ => count++;
        try
        {
            BarkHead.WireSources(e);
            global::ConditioningControlPanel.Avalonia.App.NoteFeatureUsed(feature);
            Assert.Equal("reaction", spoken());
            Assert.Equal(1, count);
            BarkHead.Stop();
            e.ReloadRules();
            global::ConditioningControlPanel.Avalonia.App.NoteFeatureUsed(feature);
            Assert.Equal(1, count);
        }
        finally { BarkHead.Stop(); }
    });

    [Fact]
    public void ServiceEventsCarryTheWpfPayloadAndEverySubscriptionIsRemoved() => WithSeamsRestored(s =>
    {
        var app = typeof(global::ConditioningControlPanel.Avalonia.App);
        var achProp = app.GetProperty("Achievements", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var old = (achProp.GetValue(null), global::ConditioningControlPanel.Avalonia.App.Quests,
            ConditioningControlPanel.Services.LockdownService.Current, CoreEngine.Video, CoreEngine.BubbleCount, s.PlayerXP);
        var patProp = typeof(AccountSeed).GetProperty("Patreon", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldPat = patProp.GetValue(null);
        using var q = new ConditioningControlPanel.Services.QuestService();
        using var ld = new ConditioningControlPanel.Services.LockdownService();
        var ach = new ConditioningControlPanel.Services.AchievementEngine(new ConditioningControlPanel.Services.AchievementStore(
            Path.Combine(TestUserDataProfile.Root, "bark-achievements.json")));
        var pat = new ConditioningControlPanel.Services.ProviderSubscription("patreon", () => s);
        var video = new ConditioningControlPanel.Services.MandatoryVideoScheduler(null!);
        var bubbles = new ConditioningControlPanel.Services.BubbleCountScheduler(null!);
        var mantra = global::ConditioningControlPanel.Avalonia.App.Mantra;
        var sources = new List<(System.Type Type, object? Owner, string Event, object?[] Args, string Trigger, string Text)>
        {
            (typeof(ProgressionBank), null, "LevelUp", new object?[] { 42 }, "LevelUp", "level {level}"),
            (typeof(ProgressionBank), null, "Awarded", new object?[] { 1d, "Test" }, "XPChanged", "xp {xp}"),
            (typeof(ConditioningControlPanel.Services.Companion.CompanionCore), null, "LevelUp", new object?[] { (ConditioningControlPanel.Models.CompanionId)0, 3 }, "CompanionLevelUp", "level {level}"),
            (ach.GetType(), ach, "Unlocked", new object?[] { ach, new ConditioningControlPanel.Models.Achievement { Id = "sandbox" } }, "AchievementUnlocked", "id {achievement}"),
            (q.GetType(), q, "QuestCompleted", new object?[] { q, null }, "QuestCompleted", "done"),
            (q.GetType(), q, "QuestProgressChanged", new object?[] { q, null }, "QuestProgressChanged", "progress"),
            (q.GetType(), q, "QuestsRefreshed", new object?[] { q, System.EventArgs.Empty }, "QuestsRefreshed", "refreshed"),
            (mantra.GetType(), mantra, "MantraCompleted", new object?[] { }, "MantraCompleted", "mantra"),
            (mantra.GetType(), mantra, "StreakChanged", new object?[] { 3 }, "MantraStreakChanged", "streak {streak}"),
            (mantra.GetType(), mantra, "StreakBroken", new object?[] { }, "MantraStreakBroken", "broken"),
            (ld.GetType(), ld, "LockdownActivated", new object?[] { }, "LockdownActivated", "locked"),
            (ld.GetType(), ld, "LockdownDeactivated", new object?[] { }, "LockdownDeactivated", "free"),
            (ld.GetType(), ld, "CountdownTick", new object?[] { System.TimeSpan.FromSeconds(12) }, "LockdownCountdownTick", "left {remaining_sec}"),
            (video.GetType(), video, "VideoAboutToStart", new object?[] { }, "VideoAboutToStart", "before"),
            (video.GetType(), video, "VideoStarted", new object?[] { }, "VideoStarted", "video"),
            (video.GetType(), video, "VideoEnded", new object?[] { }, "VideoEnded", "after"),
            (bubbles.GetType(), bubbles, "GameCompleted", new object?[] { }, "BubbleCountCompleted", "counted"),
            (bubbles.GetType(), bubbles, "GameFailed", new object?[] { }, "BubbleCountFailed", "missed"),
            (typeof(ConditioningControlPanel.Services.AIService.LocalAiService), null, "PersistentMemoryRecalled", new object?[] { null, System.EventArgs.Empty }, "PersistentMemoryRecalled", "memory"),
            (global::ConditioningControlPanel.Avalonia.Tours.TutorialHead.Service.GetType(), global::ConditioningControlPanel.Avalonia.Tours.TutorialHead.Service, "TutorialCompleted", new object?[] { null, System.EventArgs.Empty }, "TutorialCompleted", "tour"),
            (pat.GetType(), pat, "TierChanged", new object?[] { pat, ConditioningControlPanel.Models.PatreonTier.Level1 }, "PatreonTierChanged", "tier {tier} {tier_up}"),
        };
        var said = new List<string>();
        var rules = new List<BarkRule>();
        foreach (var src in sources) { var r = Rule(src.Trigger, src.Text); r.ClassRaw = "safety"; rules.Add(r); }
        var engine = new BarkEngine(() => new BarkRuleSet(rules)) { Speak = line => said.Add(line.Text) };
        engine.Start();
        void EmitAll()
        {
            foreach (var src in sources)
            {
                var field = src.Type.GetField(src.Event, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance)!;
                (field.GetValue(src.Owner) as System.Delegate)?.DynamicInvoke(src.Args);
            }
        }
        try
        {
            achProp.SetValue(null, ach);
            patProp.SetValue(null, pat);
            global::ConditioningControlPanel.Avalonia.App.Quests = q;
            ConditioningControlPanel.Services.LockdownService.Current = ld;
            CoreEngine.Video = video; CoreEngine.BubbleCount = bubbles; s.PlayerXP = 19;
            BarkHead.WireSources(engine);
            EmitAll();
            Assert.Equal(new[] { "level 42", "xp 19", "level 3", "id sandbox", "done", "progress", "refreshed", "mantra", "streak 3", "broken", "locked", "free", "left 12", "before", "video", "after", "counted", "missed", "memory", "tour", "tier Level1 True" }, said);
            BarkHead.Stop();
            EmitAll();
            Assert.Equal(sources.Count, said.Count);
        }
        finally
        {
            BarkHead.Stop();
            achProp.SetValue(null, old.Item1); patProp.SetValue(null, oldPat);
            (global::ConditioningControlPanel.Avalonia.App.Quests, ConditioningControlPanel.Services.LockdownService.Current,
                CoreEngine.Video, CoreEngine.BubbleCount, s.PlayerXP) = (old.Item2, old.Item3, old.Item4, old.Item5, old.Item6);
        }
    });

    private static BarkRule Rule(string trigger, string line, (string Key, object Value)? condition = null) => new()
    {
        Id = "t_" + trigger, Trigger = trigger, VariantPool = new() { new BarkVariant(line) },
        Conditions = condition is { } c ? new Dictionary<string, object> { [c.Key] = c.Value } : null,
    };

    private static BarkEngine Seeded(BarkRule rule, out System.Func<string?> spoken)
    {
        var e = new BarkEngine(() => new BarkRuleSet(new[] { rule }));
        BarkHead.Seed(e);
        e.Start();
        string? said = null;
        e.BarkSpoken += (_, line) => said = line;
        spoken = () => said;
        return e;
    }

    /// <summary>Every seam BarkHead / CompanionHead set, and the settings the engine writes, restored (P02).</summary>
    private static void WithSeamsRestored(System.Action<ConditioningControlPanel.Models.AppSettings> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var hooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion, CoreModsHooks.ReloadBarkRules,
            CoreSettingsHooks.SettingChangedSink, CoreBark.UiAction, CoreBark.TabNavigated, CoreBark.FeatureOpened, CoreBark.AvatarClicked);
        var chaos = (CoreBark.ChaosDraftAutopick, CoreBark.ChaosRankUp, CoreBark.ChaosResultsShown, CoreBark.AllLinesProvider);
        var state = (s.AvatarEnabled, s.BarkChatSuppressionMs, new Dictionary<string, List<string>>(s.BarkVariantRotation));
        try
        {
            (s.AvatarEnabled, s.BarkChatSuppressionMs) = (true, 0);
            body(s);
        }
        finally
        {
            (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion, CoreModsHooks.ReloadBarkRules,
                CoreSettingsHooks.SettingChangedSink, CoreBark.UiAction, CoreBark.TabNavigated, CoreBark.FeatureOpened, CoreBark.AvatarClicked) = hooks;
            (CoreBark.ChaosDraftAutopick, CoreBark.ChaosRankUp, CoreBark.ChaosResultsShown, CoreBark.AllLinesProvider) = chaos;
            (s.AvatarEnabled, s.BarkChatSuppressionMs) = (state.AvatarEnabled, state.BarkChatSuppressionMs);
            s.BarkVariantRotation.Clear();
            foreach (var kv in state.Item3) s.BarkVariantRotation[kv.Key] = kv.Value;
        }
    });

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
