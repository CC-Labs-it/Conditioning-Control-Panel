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
    [InlineData("LevelUp", "level", 12)]
    [InlineData("XPChanged", "xp", 42)]
    [InlineData("CompanionLevelUp", "level", 7)]
    [InlineData("MantraCompleted", null, 0)]
    [InlineData("MantraStreakChanged", "streak", 9)]
    [InlineData("MantraStreakBroken", null, 0)]
    [InlineData("BubblePopped", null, 0)]
    public void ExistingHostEventsReachBarksWithWpfPayloads(string trigger, string? key, int value) => WithSeamsRestored(s =>
    {
        var old = BarkHead.Engine;
        var xp = s.PlayerXP;
        var e = Seeded(Rule(trigger, "reaction", key == null ? null : (key, value)), out var spoken);
        BarkHead.WireSources(e);
        try
        {
            // Exercise the subscription boundary without windows, timers, XP awards or real notifications.
            object? owner;
            System.Type type;
            string name;
            object?[] args;
            if (trigger == "LevelUp" || trigger == "XPChanged")
            {
                owner = null; type = typeof(ProgressionBank);
                name = trigger == "LevelUp" ? "LevelUp" : "Awarded";
                s.PlayerXP = value;
                args = trigger == "LevelUp" ? new object?[] { value } : new object?[] { 1.0, "Test" };
            }
            else if (trigger == "CompanionLevelUp")
            {
                owner = null; type = typeof(ConditioningControlPanel.Services.Companion.CompanionCore);
                name = "LevelUp";
                args = new object?[] { (ConditioningControlPanel.Models.CompanionId)0, value };
            }
            else if (trigger == "BubblePopped")
            {
                owner = null; type = typeof(BubbleOverlay); name = "Popped"; args = System.Array.Empty<object?>();
            }
            else
            {
                owner = global::ConditioningControlPanel.Avalonia.App.Mantra;
                type = owner.GetType(); name = trigger.Replace("MantraStreak", "Streak");
                args = key == null ? System.Array.Empty<object?>() : new object?[] { value };
            }
            var field = type.GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!;
            var handlers = ((System.Delegate)field.GetValue(owner)!).GetInvocationList().Length;
            ((System.Delegate)field.GetValue(owner)!).DynamicInvoke(args);
            Assert.Equal("reaction", spoken());
            BarkHead.Stop();
            // Stop detaches all subscriptions; replaying the host event cannot speak again.
            Assert.Equal(handlers - 1, ((System.Delegate?)field.GetValue(owner))?.GetInvocationList().Length ?? 0);
        }
        finally { BarkHead.Stop(); BarkHead.Engine = old; s.PlayerXP = xp; }
    });

    [Theory]
    [InlineData("QuestCompleted")]
    [InlineData("QuestProgressChanged")]
    [InlineData("QuestsRefreshed")]
    [InlineData("AchievementUnlocked")]
    [InlineData("LockdownActivated")]
    [InlineData("LockdownDeactivated")]
    [InlineData("LockdownCountdownTick")]
    public void ServiceEventsMapToBarksAndDetach(string trigger) => WithSeamsRestored(_ =>
    {
        var app = typeof(global::ConditioningControlPanel.Avalonia.App);
        var property = app.GetProperty("Achievements", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var oldAchievements = property.GetValue(null);
        var oldQuests = global::ConditioningControlPanel.Avalonia.App.Quests;
        var oldLockdown = ConditioningControlPanel.Services.LockdownService.Current;
        var oldEngine = BarkHead.Engine;
        var sandbox = Path.Combine(Path.GetTempPath(), "bark-events-" + System.Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(sandbox);
        using var quests = new ConditioningControlPanel.Services.QuestService(null, sandbox);
        using var lockdown = new ConditioningControlPanel.Services.LockdownService();
        var achievements = new ConditioningControlPanel.Services.AchievementEngine(
            new ConditioningControlPanel.Services.AchievementStore(Path.Combine(sandbox, "achievements.json")));
        property.SetValue(null, achievements);
        global::ConditioningControlPanel.Avalonia.App.Quests = quests;
        ConditioningControlPanel.Services.LockdownService.Current = lockdown;
        var rule = Rule(trigger, "reaction");
        if (trigger == "AchievementUnlocked") rule.Conditions = new() { ["achievement"] = "test" };
        if (trigger == "LockdownCountdownTick") rule.Conditions = new() { ["remaining_sec"] = 12.5 };
        var e = Seeded(rule, out var spoken);
        BarkHead.WireSources(e);
        try
        {
            object owner = trigger.StartsWith("Quest") ? quests : trigger.StartsWith("Lockdown") ? lockdown : achievements;
            var field = owner.GetType().GetField(trigger == "AchievementUnlocked" ? "Unlocked" : trigger == "LockdownCountdownTick" ? "CountdownTick" : trigger,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            var handler = (System.Delegate)field.GetValue(owner)!;
            object?[] args = trigger.StartsWith("Quest") ? new object?[] { null, null }
                : trigger == "AchievementUnlocked" ? new object?[] { null, new ConditioningControlPanel.Models.Achievement { Id = "test" } }
                : trigger == "LockdownCountdownTick" ? new object?[] { System.TimeSpan.FromSeconds(12.5) }
                : System.Array.Empty<object?>();
            handler.DynamicInvoke(args);
            Assert.Equal("reaction", spoken());
            var count = handler.GetInvocationList().Length;
            BarkHead.Stop();
            Assert.Equal(count - 1, ((System.Delegate?)field.GetValue(owner))?.GetInvocationList().Length ?? 0);
        }
        finally
        {
            BarkHead.Stop(); BarkHead.Engine = oldEngine;
            property.SetValue(null, oldAchievements);
            global::ConditioningControlPanel.Avalonia.App.Quests = oldQuests;
            ConditioningControlPanel.Services.LockdownService.Current = oldLockdown;
            Directory.Delete(sandbox, true);
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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont()
                .With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseSkia()
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
