using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using Avalonia.Threading;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Services.Companion.Brain;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The companion / bark head adapters: App startup starts both, Start seeds every seam WPF pointed at
/// App.Companion / App.Bark over the shipped rules, and the CoreBark doorbell reaches the engine.</summary>
public sealed class CompanionBarkHeadTests
{
    private sealed class Clock : System.TimeProvider
    {
        public System.DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, global::System.TimeSpan.Zero);
        public override System.DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public void OpenTubeOwnsIdleCadenceAndPanicHoldsTheNextIdleLine() => WithSeamsRestored(s =>
    {
        var old = (BarkHead.Engine, s.AvatarMuted, s.MasterVolume, s.IdleGiggleIntervalSeconds);
        var tube = new AvatarTubeWindow(null);
        var panic = Rule("Panic", "safe");
        panic.ClassRaw = "safety";
        var engine = new BarkEngine(() => new BarkRuleSet(new[] { Rule("Idle", "idle line"), panic }));
        var clock = new Clock();
        engine.Clock = tube.SpeechClock = clock;
        engine.Speak = speech => BarkHead.Speak(speech, tube);
        engine.IsSpeaking = () => tube.IsSpeaking;
        engine.Start();
        BarkHead.Engine = engine;
        (s.AvatarMuted, s.MasterVolume, s.IdleGiggleIntervalSeconds) = (false, 50, 120);
        var count = 0;
        engine.BarkSpoken += (_, _) => count++;
        try
        {
            Assert.False(tube.IdleTimerRunning); // construction alone never starts a loop
            tube.Show();
            Assert.True(tube.IdleTimerRunning);
            Assert.Equal(120, tube.IdleInterval.TotalSeconds);
            tube.TickIdle();
            Assert.Equal("idle line", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(1, count);
            s.IdleGiggleIntervalSeconds = 240;
            tube.TickIdle();
            Assert.Equal(240, tube.IdleInterval.TotalSeconds); // self-heals even while speaking
            Assert.Equal(1, count);
            Assert.True(engine.Raise("Panic"));
            tube.PanicSilence();
            clock.Now += System.TimeSpan.FromSeconds(3);
            tube.TickIdle();
            Assert.Equal(2, count);
            clock.Now += System.TimeSpan.FromSeconds(58);
            tube.TickIdle();
            Assert.Equal(3, count); // both the safety hold and 60 s floor have elapsed
            s.IdleGiggleIntervalSeconds = 180;
            tube.RestartIdleTimer();
            Assert.Equal(180, tube.IdleInterval.TotalSeconds);
            tube.Close();
            Assert.False(tube.IdleTimerRunning);
            tube.TickIdle();
            Assert.Equal(3, count);
        }
        finally
        {
            tube.Close();
            (BarkHead.Engine, s.AvatarMuted, s.MasterVolume, s.IdleGiggleIntervalSeconds) = old;
        }
    });

    [Fact]
    public void IdleVoiceKeepsItsBubbleButNeedsEngineOrOptInToPlayTheClip() => WithSeamsRestored(s =>
    {
        var old = (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff,
            CoreAudio.PlayStoppableProvider, CoreSession.IsSessionRunningProvider);
        var clip = Path.GetTempFileName();
        var tube = new AvatarTubeWindow(null);
        var clock = new Clock();
        tube.SpeechClock = clock;
        var started = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { started++; return () => { }; };
        CoreSession.IsSessionRunningProvider = () => false;
        (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff) = (false, 80, false, false);
        try
        {
            Assert.False(CoreEngine.IsRunning);
            tube.Show();
            BarkHead.Speak(new BarkSpeech("idle", clip, false, null, false, true), tube);
            Assert.Equal("idle", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.Equal(0, started);
            tube.EndSpeechBubble();
            clock.Now += System.TimeSpan.FromSeconds(3);
            s.CompanionSpeaksIdleWhenEngineOff = true;
            BarkHead.Speak(new BarkSpeech("opted in idle", clip, false, null, false, true), tube);
            Assert.Equal(1, started);
            tube.EndSpeechBubble();
            clock.Now += System.TimeSpan.FromSeconds(3);
            s.CompanionSpeaksIdleWhenEngineOff = false;
            BarkHead.Speak(new BarkSpeech("prompted", clip, false, null, false), tube);
            Assert.Equal(2, started);
            tube.Giggle("after prompted");
            Assert.Equal(1, tube.QueuedSpeechCount); // ordinary voice did not claim the AI bubble
        }
        finally
        {
            tube.Close();
            (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff,
                CoreAudio.PlayStoppableProvider, CoreSession.IsSessionRunningProvider) = old;
            File.Delete(clip);
        }
    });

    [Fact]
    public void RemotePanicCutsTheCurrentIdleClipAndBubble() => WithSeamsRestored(s =>
    {
        var old = (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted,
            s.CompanionSpeaksIdleWhenEngineOff, CoreAudio.PlayStoppableProvider);
        var clip = Path.GetTempFileName();
        var started = 0;
        var stopped = 0;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { started++; return () => stopped++; };
        (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, s.CompanionSpeaksIdleWhenEngineOff) =
            (false, 80, false, true);
        var shell = new MainShellWindow();
        try
        {
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube;
            Assert.NotNull(tube);
            BarkHead.Speak(new BarkSpeech("idle voice", clip, false, null, false, true), tube);
            Assert.Equal(1, started);
            Assert.True(tube.IsSpeaking);

            shell.OnRemoteCommandReceived("trigger_panic");
            Assert.Equal(1, stopped);
            Assert.False(tube.IsSpeaking);
            Assert.False(tube.FindControl<Border>("SpeechBubble")!.IsVisible);
        }
        finally
        {
            shell.Close();
            (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted,
                s.CompanionSpeaksIdleWhenEngineOff, CoreAudio.PlayStoppableProvider) = old;
            File.Delete(clip);
        }
    });

    [Fact]
    public void OrdinaryVoicedBarkQueuesWithoutPreemptingAndPanicCancelsIt() => WithSeamsRestored(s =>
    {
        var old = (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, CoreAudio.PlayStoppableProvider);
        var clip = Path.GetTempFileName();
        var tube = new AvatarTubeWindow(null);
        var started = 0;
        System.Action? delayed = null;
        var cancelled = false;
        CoreAudio.PlayStoppableProvider = (_, _, _, _, _) => { started++; return () => { }; };
        (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted) = (false, 80, false);
        try
        {
            tube.Show();
            tube.SpeechDelayScheduler = (_, fire) => { delayed = fire; return () => cancelled = true; };
            var text = tube.FindControl<TextBlock>("TxtSpeech")!;
            tube.Giggle("first");
            BarkHead.Speak(new BarkSpeech("queued voice", clip, false, null, false), tube);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("first", text.Text);
            Assert.Equal(1, tube.QueuedSpeechCount);
            Assert.Equal(0, started);
            tube.EndSpeechBubble();
            Assert.NotNull(delayed);
            delayed!();
            Assert.Equal("queued voice", text.Text);
            Assert.Equal(0, started); // WPF's delayed preset path deliberately drops the clip
            BarkHead.Speak(new BarkSpeech("cancelled voice", clip, false, null, false), tube);
            tube.EndSpeechBubble();
            tube.PanicSilence();
            Assert.True(cancelled);
            Assert.Equal(0, tube.QueuedSpeechCount);
            Assert.False(tube.IsSpeaking);
            BarkHead.Speak(new BarkSpeech("priority voice", clip, true, null, false), tube);
            Assert.Equal("priority voice", text.Text);
            Assert.Equal(1, started);
        }
        finally
        {
            tube.Close();
            (s.AvatarMuted, s.MasterVolume, s.CompanionVoiceLinesMuted, CoreAudio.PlayStoppableProvider) = old;
            File.Delete(clip);
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
        var startSettings = (s.AudioOnlySession, s.SpiralEnabled, s.PinkFilterEnabled, s.TotalSessions);
        var rule = Rule("Panic", "breathe");
        rule.ClassRaw = "safety";
        BarkHead.Engine = Seeded(rule, out var spoken);
        var shell = new MainShellWindow();
        try
        {
            (s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll) = (true, "F8", false);
            shell.HandlePanicKeyPress(new System.DateTime(2026, 1, 1, 12, 0, 0));
            Assert.Equal("breathe", spoken());
            Assert.True(CompanionIdleVoicePolicy.PanicQuiet);
            (s.AudioOnlySession, s.SpiralEnabled, s.PinkFilterEnabled, s.PanicKeyEnabled) = (true, false, false, false);
            shell.StartEngine();
            Assert.True(CoreEngine.IsRunning);
            Assert.False(CompanionIdleVoicePolicy.PanicQuiet); // explicit user START ends the quiet early
        }
        finally
        {
            MainShellWindow.StopEngine();
            shell.OnEngineStopped();
            shell.Close();
            (BarkHead.Engine, s.PanicKeyEnabled, s.PanicKey, s.PanicOverridesAll) = old;
            (s.AudioOnlySession, s.SpiralEnabled, s.PinkFilterEnabled, s.TotalSessions) = startSettings;
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
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().UseSkia()
                .WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var hooks = (CoreModsHooks.ActiveCompanionProvider, CoreModsHooks.SwitchCompanion, CoreModsHooks.ReloadBarkRules,
            CoreSettingsHooks.SettingChangedSink, CoreBark.UiAction, CoreBark.TabNavigated, CoreBark.FeatureOpened, CoreBark.AvatarClicked);
        var chaos = (CoreBark.ChaosDraftAutopick, CoreBark.ChaosRankUp, CoreBark.ChaosResultsShown, CoreBark.AllLinesProvider);
        var idleVoiced = CompanionBrain.IdleLineVoiced;
        var quietUntil = CompanionIdleVoicePolicy.PanicQuietUntilMs;
        var state = (s.AvatarEnabled, s.BarkChatSuppressionMs, new Dictionary<string, List<string>>(s.BarkVariantRotation),
            new List<string>(s.BarkIdleRotation));
        try
        {
            (s.AvatarEnabled, s.BarkChatSuppressionMs) = (true, 0);
            CompanionIdleVoicePolicy.EndPanicQuiet();
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
            s.BarkIdleRotation = state.Item4;
            CompanionBrain.IdleLineVoiced = idleVoiced;
            CompanionIdleVoicePolicy.PanicQuietUntilMs = quietUntil;
        }
    });

    private static string Root([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
