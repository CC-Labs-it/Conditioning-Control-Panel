using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Bark;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The companion / bark head adapters: App startup starts both, Start seeds every seam WPF pointed at
/// App.Companion / App.Bark over the shipped rules, and the CoreBark doorbell reaches the engine.</summary>
public sealed class CompanionBarkHeadTests
{
    [Fact]
    public void IdleVoiceIsHeldWithTheEngineOffUnlessOptedIn()
    {
        var engine = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var s = CoreSettings.Current;
        var opt = s.CompanionSpeaksIdleWhenEngineOff;
        try
        {
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => false;
            s.CompanionSpeaksIdleWhenEngineOff = false;
            Assert.True(BarkHead.HoldIdleVoice());
            CoreSession.IsEngineRunningProvider = () => true;
            Assert.False(BarkHead.HoldIdleVoice());
            CoreSession.IsEngineRunningProvider = () => false;
            s.CompanionSpeaksIdleWhenEngineOff = true;
            Assert.False(BarkHead.HoldIdleVoice());
        }
        finally
        {
            CoreSession.IsEngineRunningProvider = engine;
            CoreSession.IsSessionRunningProvider = session;
            s.CompanionSpeaksIdleWhenEngineOff = opt;
        }
    }

    [Fact]
    public void OpenTubeIdleTickReachesBarksAndPanicAndCloseStopItsTimer() => WithSeamsRestored(s =>
    {
        var old = (BarkHead.Engine, s.MasterVolume, s.IdleGiggleIntervalSeconds, s.BarkIdleRotation);
        var shell = new MainShellWindow();
        try
        {
            s.MasterVolume = 50;
            s.IdleGiggleIntervalSeconds = 120;
            s.BarkIdleRotation = new();
            var e = Seeded(Rule("Idle", "idle hello"), out var spoken);
            // Keep the headless test's mouth observational, avoiding audio or notifications.
            e.Speak = _ => { };
            BarkHead.Engine = e;
            shell.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.True(tube.IdleTimerRunning);
            var latch = tube.GetType().GetField("_isGiggling", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            latch.SetValue(tube, true); // a collapsed bubble left the WPF speech latch stranded
            tube.OnIdleTick();
            Assert.False(tube.IsSpeaking);
            Assert.Equal("idle hello", spoken());
            s.IdleGiggleIntervalSeconds = 60;
            tube.RestartIdleTimer();
            Assert.Equal(System.TimeSpan.FromSeconds(60), tube.IdleInterval);
            tube.PanicSilence();
            Assert.False(tube.IdleTimerRunning);
            tube.RestartIdleTimer();
            Assert.True(tube.IdleTimerRunning);
            shell.Close();
            Assert.False(tube.IdleTimerRunning);
        }
        finally
        {
            shell.Close();
            (BarkHead.Engine, s.MasterVolume, s.IdleGiggleIntervalSeconds, s.BarkIdleRotation) = old;
        }
    });

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

    [Theory]   // WPF BarkService.cs:727-733: OnBounce -> BouncingTextBounce, OnCornerHit -> BouncingTextCorner
    [InlineData("BouncingTextBounce", 2.0, 300.0)]   // left wall only
    [InlineData("BouncingTextCorner", 2.0, 2.0)]     // both walls at once
    public void ABouncingTextWallOrCornerRaisesItsBark(string trigger, double x, double y) => WithSeamsRestored(_ =>
    {
        var old = (BarkHead.Engine, ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.CornerHit);
        var eng = ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.Engine;
        var bounds = (eng.MinX, eng.MinY, eng.MaxX, eng.MaxY);
        BarkHead.Engine = Seeded(Rule(trigger, "boing"), out var spoken);
        try
        {
            ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.CornerHit = () => { };
            ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.Stop();
            eng.SetBounds(0, 0, 1000, 800);
            eng.Measure = (_, _) => (200, 60);
            eng.Start(new ConditioningControlPanel.Models.AppSettings());
            var l = eng.Logos[0];
            (l.PosX, l.PosY, l.VelX, l.VelY) = (400, 300, 100, 100);
            ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.Advance(0.05);   // mid-screen: nothing
            Assert.Null(spoken());
            (l.PosX, l.PosY, l.VelX, l.VelY) = (x, y, -100, -100);
            ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.Advance(0.05);
            Assert.Equal("boing", spoken());
        }
        finally
        {
            (BarkHead.Engine, ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.CornerHit) = old;
            ConditioningControlPanel.Avalonia.Views.Overlays.BouncingTextOverlay.Stop();
            eng.Measure = null;
            eng.SetBounds(bounds.MinX, bounds.MinY, bounds.MaxX, bounds.MaxY);
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
