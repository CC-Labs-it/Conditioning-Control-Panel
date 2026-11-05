using App = ConditioningControlPanel.Avalonia.App;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Controls;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Services.Companion.Brain;
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
        var providers = (CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider, CoreAudio.PlayStoppableProvider, CoreModArt.OverridePathProvider, CoreEntitlement.HasPremiumProvider);
        var voice = (s.CompanionSpeaksIdleWhenEngineOff, s.AvatarMuted, CompanionBrain.IdleLineVoiced);
        var autonomy = (s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.AutonomyIdleTriggerEnabled, s.AutonomyIdleTimeoutMinutes);
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
            BarkHead.Start(); // production voice-memory seam is set before attaching a deterministic source
            var rule = Rule("Idle", "idle hello");
            rule.Priority = 50; rule.VariantPool![0] = new BarkVariant("idle hello", "idle_hum_1.mp3");
            var e = Seeded(rule, out var spoken);
            e.ResolveAudio = _ => typeof(BarkHead).Assembly.Location;
            using var transport = new ConditioningControlPanel.Services.AiService("http://127.0.0.1:1");
            using var memory = new MemoryStore(Path.Combine(TestUserDataProfile.Root, "bark-memory.json"));
            using var brain = new CompanionBrain(transport, memory: memory, store: new CompanionSessionStore(Path.Combine(TestUserDataProfile.Root, "bark-session.json")));
            brain.AttachBarkSource(h => e.BarkSpoken += h, h => e.BarkSpoken -= h);
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
            Assert.True(IdleTimerRunning(tube));
            tube.SpeechClock = clock;
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            CoreEntitlement.HasPremiumProvider = () => true;
            s.AutonomyModeEnabled = s.AutonomyConsentGiven = s.AutonomyIdleTriggerEnabled = true; s.AutonomyIdleTimeoutMinutes = 5;
            shell.Autonomy.Stop(); shell.Autonomy.Clock = () => clock.GetLocalNow().DateTime;
            Assert.True(shell.Autonomy.Start(withTimer: false));
            var idleDue = shell.Autonomy.GetType().GetField("_nextIdle", flags)!;
            Assert.Equal(clock.GetLocalNow().DateTime.AddMinutes(5), idleDue.GetValue(shell.Autonomy));
            clock.Advance(120); // WPF's default idle chatter interval
            var counter = tube.GetType().GetField("_presetGiggleCounter", flags)!;
            counter.SetValue(tube, 4); // the next preset would make its every-fifth sound
            // A stranded latch is IdleTickClearsAWedgedSpeechLatch's (WPF Speech.cs:3082: that tick then waits the gap).
            tube.IdleTick();
            Assert.Equal(clock.GetLocalNow().DateTime.AddMinutes(5), idleDue.GetValue(shell.Autonomy));
            Assert.True(tube.IsSpeaking);
            Assert.Equal("idle hello", spoken());
            Assert.False(Assert.Single(brain.Session.Turns).Voiced);
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
            Assert.True(brain.Session.Turns.Last().Voiced);
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
            // Slider and speech restarts are IdleTimerFollowsTheSliderAndSpeechAndStopsOnClose's.
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            tube.PanicSilence();
            Assert.True(IdleTimerRunning(tube));
            s.CompanionSpeaksIdleWhenEngineOff = true;
            Assert.True(BarkHead.HoldIdleVoice());
            clock.Advance(60);
            tube.IdleTick(); // still ticking, with no audible voice during quiet
            Assert.Equal(0, audio);
            tube.EndSpeechBubble();
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet(
                System.Environment.TickCount64 - (long)ConditioningControlPanel.Services.Safety.PanicAftermath.QuietWindow.TotalMilliseconds);
            clock.Advance(60);
            counter.SetValue(tube, 4);
            Assert.False(BarkHead.HoldIdleVoice());
            tube.IdleTick(); // no speech, slider change or explicit timer restart needed
            Assert.Equal(1, audio);
            Assert.Empty(tube.ChatHistory);
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            BarkHead.Speak(new BarkSpeech("priority idle", "unused.mp3", true, null, false, true));
            Assert.Single(tube.ChatHistory); // priority remains priority while held
            Assert.Equal(1, audio);
            shell.Close();
            Assert.False(IdleTimerRunning(tube));
        }
        finally {
            shell.Close();
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            lifetimeField.SetValue(Application.Current, lifetime);
            BarkHead.Stop(); var stoppedVoice = CompanionBrain.IdleLineVoiced;
            (CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider, CoreAudio.PlayStoppableProvider, CoreModArt.OverridePathProvider, CoreEntitlement.HasPremiumProvider) = providers;
            (s.CompanionSpeaksIdleWhenEngineOff, s.AvatarMuted, CompanionBrain.IdleLineVoiced) = voice;
            (s.AutonomyModeEnabled, s.AutonomyConsentGiven, s.AutonomyIdleTriggerEnabled, s.AutonomyIdleTimeoutMinutes) = autonomy;
            (BarkHead.Engine, s.MasterVolume, s.IdleGiggleIntervalSeconds, s.BarkIdleRotation) = old;
            Assert.Null(stoppedVoice);
        }
    });
    private static bool IdleTimerRunning(ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow tube) =>
        (tube.GetType().GetField("_idleTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(tube) as global::Avalonia.Threading.DispatcherTimer)?.IsEnabled == true;
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
            var rule = Rule(flash ? "FlashDisplayed" : "SubliminalDisplayed", "shown");
            rule.ClassRaw = "safety"; // bypasses timing, so a per-image raise would speak once per image
            BarkHead.Engine = Seeded(rule, out var spoken);
            int count = 0;
            BarkHead.Engine.BarkSpoken += (_, _) => count++;
            if (flash)
            {
                // A three-image batch whose generation is already stale: queued, never shown (WPF FlashService.cs:1672).
                var batch = Enumerable.Range(0, 3).Select(_ => ((global::Avalonia.Media.Imaging.Bitmap)new global::Avalonia.Media.Imaging.WriteableBitmap(
                    new global::Avalonia.PixelSize(1, 1), new global::Avalonia.Vector(96, 96)), default(global::Avalonia.PixelRect), "synthetic.png",
                    (global::Avalonia.Platform.Screen)null!)).ToList();
                FlashOverlay.Schedule(batch, 0, 0, int.MinValue, 99, 1, System.TimeSpan.Zero, System.TimeSpan.Zero, false, false, 0, null);
            }
            else SubliminalOverlay.Present(card);
            Assert.Equal("shown", spoken());
            Assert.Equal(1, count);
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
            tube.Giggle("busy");   // a line on screen: not ready, so the tick stays silent
            tube.IdleTick();
            Assert.Null(spoken());
            tube.Close();
            tube = new ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
            tube.IdleTick();
            Assert.Equal("idle", spoken());
        }
        finally { tube.Close(); BarkHead.Engine = old; }
    });

    [Fact]
    public void IdleTickClearsAWedgedSpeechLatch() => WithSeamsRestored(_ =>
    {
        var tube = new ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
        try
        {
            // WPF Speech.cs:1612-1614: latched, no bubble, empty queue, no gap pending -> nothing will ever lower it.
            typeof(ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow)
                .GetField("_isGiggling", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.SetValue(tube, true);
            tube.IdleTick();
            Assert.False(tube.IsSpeaking);
        }
        finally { tube.Close(); }
    });

    [Fact]
    public void ListeningOpenedBeforeAnIdleTickCancelsThatTick() => WithSeamsRestored(s =>
    {
        // WPF AvatarTubeWindow.Speech.cs:789 resets the cadence after showing the mic prompt.
        var oldEngine = BarkHead.Engine;
        var tube = new ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
        var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;
        var timerField = tube.GetType().GetField("_idleTimer", flags)!;
        try
        {
            s.AvatarMuted = false;
            s.IdleGiggleIntervalSeconds = 120;
            BarkHead.Engine = Seeded(Rule("Idle", "idle"), out var spoken);
            tube.RestartIdleTimer();
            var pending = (global::Avalonia.Threading.DispatcherTimer)timerField.GetValue(tube)!;
            // Make the existing tick runnable, as if its interval just elapsed, without waiting.
            typeof(global::Avalonia.Threading.DispatcherTimer).GetMethod("Promote", flags)!.Invoke(pending, null);
            tube.ShowListeningBubble("listening");
            global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Null(spoken());
            Assert.StartsWith("listening", tube.FindControl<TextBlock>("TxtSpeech")!.Text);
            Assert.False(pending.IsEnabled);
            var restarted = (global::Avalonia.Threading.DispatcherTimer)timerField.GetValue(tube)!;
            Assert.NotSame(pending, restarted);
            Assert.True(restarted.IsEnabled);
            Assert.Equal(System.TimeSpan.FromSeconds(120), restarted.Interval);
        }
        finally { tube.Close(); BarkHead.Engine = oldEngine; }
    });

    [Fact]
    public void IdleTimerFollowsTheSliderAndSpeechAndStopsOnClose() => WithSeamsRestored(s =>
    {
        // WPF Speech.cs:1571-1623 and MainWindow.Patreon.cs:1459: slider restart.
        var lifetime = Application.Current!.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Single(f => typeof(global::Avalonia.Controls.ApplicationLifetimes.IApplicationLifetime).IsAssignableFrom(f.FieldType));
        using var desktop = new global::Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime
        { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        lifetimeField.SetValue(Application.Current, desktop);
        var tube = new ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow(null);
        var timerField = tube.GetType().GetField("_idleTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        try
        {
            s.IdleGiggleIntervalSeconds = 120;
            ((IList<Window>)desktop.Windows).Add(tube); // Headless setup does not run the desktop window tracker.
            tube.Show();
            Assert.Same(tube, ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow.Live);
            var first = (global::Avalonia.Threading.DispatcherTimer)timerField.GetValue(tube)!;
            Assert.True(first.IsEnabled);
            s.IdleGiggleIntervalSeconds = 90;
            var cell = new ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime.WorkshopBehaviorCell();
            cell.FindControl<global::Avalonia.Controls.Slider>("SliderIdleIntervalCompanion")!.Value = 60;
            var changed = (global::Avalonia.Threading.DispatcherTimer)timerField.GetValue(tube)!;
            Assert.False(first.IsEnabled);
            Assert.Equal(System.TimeSpan.FromSeconds(60), changed.Interval);
            tube.Giggle("busy");
            var spoken = (global::Avalonia.Threading.DispatcherTimer)timerField.GetValue(tube)!;
            Assert.False(changed.IsEnabled);
            Assert.True(spoken.IsEnabled);
            s.IdleGiggleIntervalSeconds = 75;
            tube.IdleTick();
            Assert.Equal(System.TimeSpan.FromSeconds(75), spoken.Interval);
            tube.Close();
            Assert.False(spoken.IsEnabled);
        }
        finally { tube.Close(); lifetimeField.SetValue(Application.Current, lifetime); }
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
                .With(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Options())
                .ConfigureFonts(global::ConditioningControlPanel.Avalonia.Platform.AppFonts.Register).UseSkia()
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
