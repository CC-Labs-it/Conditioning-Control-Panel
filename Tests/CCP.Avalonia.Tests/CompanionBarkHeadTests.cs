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
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
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
