using App = ConditioningControlPanel.Avalonia.App;
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
    [InlineData("LevelUp", "LevelUp")]
    [InlineData("Awarded", "XPChanged")]
    [InlineData("CompanionLevelUp", "CompanionLevelUp")]
    [InlineData("MantraCompleted", "MantraCompleted")]
    [InlineData("StreakChanged", "MantraStreakChanged")]
    [InlineData("StreakBroken", "MantraStreakBroken")]
    public void ExistingServiceEventsReachBarksAndStopDetaches(string source, string trigger) => WithSeamsRestored(_ =>
    {
        BarkHead.Stop();
        var rule = Rule(trigger, "reacted");
        rule.ClassRaw = "safety"; // repeated events bypass timing, exposing duplicate or dangling handlers
        var e = Seeded(rule, out var spoken);
        int count = 0;
        e.BarkSpoken += (_, _) => count++;
        BarkHead.WireSources(e);
        // Raise the service's event itself: tests the adapter without banking XP or mutating a live mantra session.
        var type = source == "CompanionLevelUp" ? typeof(ConditioningControlPanel.Services.Companion.CompanionCore)
            : source is "LevelUp" or "Awarded" ? typeof(ProgressionBank) : App.Mantra.GetType();
        var name = source == "CompanionLevelUp" ? "LevelUp" : source;
        var field = type.GetField(name, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object? target = type == App.Mantra.GetType() ? App.Mantra : null;
        object?[] args = source switch
        {
            "LevelUp" or "StreakChanged" => new object?[] { 7 },
            "Awarded" => new object?[] { 4d, "test" },
            "CompanionLevelUp" => new object?[] { ConditioningControlPanel.Models.CompanionId.OGBambiSprite, 7 },
            _ => System.Array.Empty<object?>(),
        };
        try
        {
            ((System.Delegate?)field.GetValue(target))?.DynamicInvoke(args);
            Assert.Equal("reacted", spoken());
            Assert.Equal(1, count);
            BarkHead.Stop();
            // A fresh safety rule bypasses timing so a dangling subscription would be visible.
            e.ReloadRules();
            ((System.Delegate?)field.GetValue(target))?.DynamicInvoke(args);
            Assert.Equal(1, count);
        }
        finally { BarkHead.Stop(); }
    });

    [Theory]
    [InlineData("QuestsRefreshed", "QuestsRefreshed")]
    [InlineData("QuestCompleted", "QuestCompleted")]
    [InlineData("QuestProgressChanged", "QuestProgressChanged")]
    [InlineData("Unlocked", "AchievementUnlocked")]
    [InlineData("LockdownActivated", "LockdownActivated")]
    [InlineData("LockdownDeactivated", "LockdownDeactivated")]
    [InlineData("CountdownTick", "LockdownCountdownTick")]
    [InlineData("Popped", "BubblePopped")]
    public void HostedEventsReachTheEngine(string source, string trigger) => WithSeamsRestored(_ =>
    {
        var oldQuest = App.Quests;
        var oldLock = ConditioningControlPanel.Services.LockdownService.Current;
        var achievementField = typeof(App).GetField("<Achievements>k__BackingField", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
        var oldAchievements = achievementField.GetValue(null);
        var dir = Directory.CreateTempSubdirectory();
        using var quests = new ConditioningControlPanel.Services.QuestService(null, dir.FullName);
        using var lockdown = new ConditioningControlPanel.Services.LockdownService();
        var achievements = new ConditioningControlPanel.Services.AchievementEngine(new ConditioningControlPanel.Services.AchievementStore(Path.Combine(dir.FullName, "achievements.json")));
        try
        {
            BarkHead.Stop();
            App.Quests = quests;
            ConditioningControlPanel.Services.LockdownService.Current = lockdown;
            achievementField.SetValue(null, achievements);
            var e = Seeded(Rule(trigger, "event"), out var spoken);
            BarkHead.WireSources(e);
            object? target = source == "Unlocked" ? achievements : source.StartsWith("Lockdown") || source == "CountdownTick" ? lockdown : source == "Popped" ? null : quests;
            var type = target?.GetType() ?? typeof(BubbleOverlay);
            var field = type.GetField(source, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static)!;
            var handler = (System.Delegate)field.GetValue(target)!;
            var parameters = handler.GetType().GetMethod("Invoke")!.GetParameters();
            var args = new object?[parameters.Length];
            for (int i = 0; i < args.Length; i++)
            {
                var t = parameters[i].ParameterType;
                args[i] = t == typeof(object) ? target : t.IsValueType ? System.Activator.CreateInstance(t)
                    : System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(t);
            }
            handler.DynamicInvoke(args);
            Assert.Equal("event", spoken());
        }
        finally
        {
            BarkHead.Stop();
            App.Quests = oldQuest;
            ConditioningControlPanel.Services.LockdownService.Current = oldLock;
            achievementField.SetValue(null, oldAchievements);
            dir.Delete(true);
        }
    });

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisplayedVisualsRaiseTheirBark(bool flash) => WithSeamsRestored(s =>
    {
        var old = BarkHead.Engine;
        var card = new global::Avalonia.Controls.Window();
        try
        {
            BarkHead.Engine = Seeded(Rule(flash ? "FlashDisplayed" : "SubliminalDisplayed", "shown"), out var spoken);
            if (flash) FlashOverlay.Paid("synthetic.png", s, 0, 1);
            else SubliminalOverlay.Present(card);
            Assert.Equal("shown", spoken());
        }
        finally { card.Close(); BarkHead.Engine = old; }
    });

    [Fact]
    public void IdleTickCallsTheEngineOnlyWhenSpeechIsReady() => WithSeamsRestored(_ =>
    {
        var old = BarkHead.Engine;
        var tube = new ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
        try
        {
            BarkHead.Engine = Seeded(Rule("Idle", "idle"), out var spoken);
            tube.IdleTick();
            Assert.Equal("idle", spoken());
        }
        finally { tube.Close(); BarkHead.Engine = old; }
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
