using System.Linq;
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
    public void IdleVoiceIsHeldWithTheEngineOffUnlessOptedIn()
    {
        var engine = CoreSession.IsEngineRunningProvider;
        var session = CoreSession.IsSessionRunningProvider;
        var s = CoreSettings.Current;
        var opt = s.CompanionSpeaksIdleWhenEngineOff;
        try {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => false;
            s.CompanionSpeaksIdleWhenEngineOff = false;
            Assert.True(BarkHead.HoldIdleVoice());
            CoreSession.IsEngineRunningProvider = () => true;
            Assert.False(BarkHead.HoldIdleVoice());
            CoreSession.IsEngineRunningProvider = () => false;
            s.CompanionSpeaksIdleWhenEngineOff = true;
            Assert.False(BarkHead.HoldIdleVoice());
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Assert.True(BarkHead.HoldIdleVoice());
        }
        finally {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSession.IsEngineRunningProvider = engine;
            CoreSession.IsSessionRunningProvider = session;
            s.CompanionSpeaksIdleWhenEngineOff = opt;
        }
    }
    [Fact]
    public void IdleMouthKeepsPresetRoutingAndRecoversAfterPanicQuiet() => WithSeamsRestored(s =>
    {
        var old = (BarkHead.Engine, s.MasterVolume, s.IdleGiggleIntervalSeconds, s.BarkIdleRotation);
        var lifetime = Application.Current!.ApplicationLifetime;
        // SetupWithoutStarting seals the public setter; supply the lifetime only for this live-window lookup.
        var lifetimeField = typeof(Application).GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            .Single(f => typeof(global::Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime).IsAssignableFrom(f.FieldType));
        using var desktop = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
            { ShutdownMode = global::Avalonia.Controls.ShutdownMode.OnExplicitShutdown };
        desktop.GetType().GetMethod("SubscribeGlobalEvents", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(desktop, null);
        lifetimeField.SetValue(Application.Current, desktop);
        var shell = new MainShellWindow();
        try {
            s.MasterVolume = 50;
            s.IdleGiggleIntervalSeconds = 120;
            s.BarkIdleRotation = new();
            var rule = Rule("Idle", "idle hello");
            rule.Priority = 50; rule.VariantPool![0] = new BarkVariant("idle hello", "idle_hum_1.mp3");
            var e = Seeded(rule, out var spoken);
            e.ResolveAudio = _ => typeof(BarkHead).Assembly.Location;
            var clock = new BarkClock();
            e.Clock = clock;
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => false;
            s.CompanionSpeaksIdleWhenEngineOff = false;
            s.AvatarMuted = false;
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            int audio = 0;
            CoreModArt.OverridePathProvider = path => path.StartsWith("sounds/") ? typeof(BarkHead).Assembly.Location : null;
            CoreAudio.PlayStoppableProvider = (_, _, tag, _, _) => { Assert.Equal("bark-voice", tag); audio++; return () => { }; };
            BarkHead.Engine = e;
            shell.Show();
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            var tube = shell.Tube!;
            Assert.True(tube.IdleTimerRunning);
            tube.SpeechClock = clock;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var counter = tube.GetType().GetField("_presetGiggleCounter", flags)!;
            counter.SetValue(tube, 4); // the next preset would make its every-fifth sound
            var latch = tube.GetType().GetField("_isGiggling", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
            latch.SetValue(tube, true); // a collapsed bubble left the WPF speech latch stranded
            tube.OnIdleTick();
            Assert.True(tube.IsSpeaking);
            Assert.Equal("idle hello", spoken());
            Assert.Empty(tube.ChatHistory);
            Assert.Equal(false, tube.GetType().GetField("_isShowingAiBubble", flags)!.GetValue(tube));
            Assert.Equal(0, audio);
            System.Action? delayed = null;
            tube.SpeechDelayScheduler = (_, fire) => { delayed = fire; return () => { }; };
            tube.Giggle("queued idle", unprompted: true);
            Assert.Equal(1, tube.QueuedSpeechCount);
            tube.EndSpeechBubble();
            clock.Advance(2);
            counter.SetValue(tube, 4);
            delayed!();
            Assert.Equal(0, audio); // queued preset retains the idle voice hold
            Assert.Empty(tube.ChatHistory);
            tube.EndSpeechBubble();
            clock.Advance(2);
            Assert.True(tube.IsSpeechReady()); // preset gap, no AI bonus
            BarkHead.Speak(new BarkSpeech("held clip", "unused.mp3", false, null, false, true));
            Assert.Empty(tube.ChatHistory);
            Assert.Equal(0, audio);
            tube.EndSpeechBubble();
            clock.Advance(2);
            CoreSession.IsEngineRunningProvider = () => true;
            clock.Advance(60);
            Assert.True(e.DispatchIdle()); // shipped shape: priority 50, clip present, voice not held
            Assert.Empty(tube.ChatHistory);
            Assert.Equal(false, tube.GetType().GetField("_isShowingAiBubble", flags)!.GetValue(tube));
            Assert.Equal(1, audio);
            BarkHead.Speak(new BarkSpeech("queued voiced idle", e.ResolveAudio("idle_hum_1.mp3"), false, null, false, true));
            Assert.Equal(1, tube.QueuedSpeechCount);
            tube.EndSpeechBubble(); clock.Advance(2); counter.SetValue(tube, 0); delayed!();
            Assert.Equal(1, audio); // queued path drops the clip, as WPF Speech.cs:283
            Assert.Empty(tube.ChatHistory);
            tube.EndSpeechBubble(); clock.Advance(2);
            Assert.True(tube.IsSpeechReady()); // voiced presets also keep the 2 s gap
            CoreSession.IsEngineRunningProvider = () => false; audio = 0;
            s.IdleGiggleIntervalSeconds = 60;
            BarkHead.OnSettingChanged(e, nameof(s.IdleGiggleIntervalSeconds));
            Assert.Equal(System.TimeSpan.FromSeconds(60), tube.IdleInterval);
            CoreSettingsHooks.SettingChangedSink = null; // isolate the speech reset from the slider notification
            s.IdleGiggleIntervalSeconds = 30;
            tube.GigglePriority("idle reset", playSound: false);
            Assert.Equal(System.TimeSpan.FromSeconds(30), tube.IdleInterval);
            tube.ChatHistory.Clear(); // the explicit priority speech above is a chat-log line
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            tube.PanicSilence();
            Assert.True(tube.IdleTimerRunning);
            s.CompanionSpeaksIdleWhenEngineOff = true;
            Assert.True(BarkHead.HoldIdleVoice());
            clock.Advance(60);
            tube.OnIdleTick(); // still ticking, with no audible voice during quiet
            Assert.Equal(0, audio);
            tube.EndSpeechBubble();
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet(
                System.Environment.TickCount64 - (long)ConditioningControlPanel.Services.Safety.PanicAftermath.QuietWindow.TotalMilliseconds);
            clock.Advance(60);
            counter.SetValue(tube, 4);
            Assert.False(BarkHead.HoldIdleVoice());
            tube.OnIdleTick(); // no speech, slider change or explicit timer restart needed
            Assert.Equal(1, audio);
            Assert.Empty(tube.ChatHistory);
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            BarkHead.Speak(new BarkSpeech("priority idle", "unused.mp3", true, null, false, true));
            Assert.Single(tube.ChatHistory); // priority remains priority while held
            Assert.Equal(1, audio);
            shell.Close();
            Assert.False(tube.IdleTimerRunning);
        }
        finally {
            shell.Close();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            lifetimeField.SetValue(Application.Current, lifetime);
            (BarkHead.Engine, s.MasterVolume, s.IdleGiggleIntervalSeconds, s.BarkIdleRotation) = old;
        }
    });
    private sealed class BarkClock : System.TimeProvider {
        private global::System.DateTimeOffset _now = global::System.DateTimeOffset.UtcNow;
        public override global::System.DateTimeOffset GetUtcNow() => _now;
        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
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
        AmbientBeatLoopsTests.Setup();
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
