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
        ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
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
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
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
    [Fact] // WPF KeywordTriggerService.cs:1257-1267,1805-1808
    public void BrainDrainKeywordRoutesEnabledActionsAndLegacyButDeduplicatesMergedMatches() => Run((engine, s, _) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        var payloads = new List<ConditioningControlPanel.Services.Chaos.EffectPayload>();
        ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = payloads.Add;
        try
        {
            var now = new DateTime(2026, 10, 14, 12, 0, 0); engine.Now = () => now;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].Actions = new List<KeywordAction> {
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain },
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain },
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain, Enabled = false },
                new VisualEffectAction { Effect = KeywordVisualEffect.Bubbles } };
            now = now.AddSeconds(2); Type(engine, "sit");
            Assert.Equal(2, payloads.Count);
            Assert.All(payloads, p => Assert.IsType<ConditioningControlPanel.Services.Chaos.BrainDrainMeltPayload>(p));
            payloads.Clear();
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:drain", Keyword = "sit", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain } } });
            now = now.AddSeconds(2); Type(engine, "sit");
            Assert.Single(payloads); payloads.Clear();
            s.KeywordTriggers.RemoveAt(1);
            s.KeywordTriggers[0].Actions.Clear(); s.KeywordTriggers[0].VisualEffect = KeywordVisualEffect.BrainDrain;
            now = now.AddSeconds(2); Type(engine, "sit");
            Assert.Single(payloads); payloads.Clear();
            s.KeywordTriggers[0].VisualEffect = KeywordVisualEffect.None;
            now = now.AddSeconds(2); Type(engine, "sit");
            Assert.Empty(payloads);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });
    [Fact] // WPF KeywordTriggerService.cs:1187-1200,1257-1267
    public void PanicQuietSuppressesKeywordAudioAndBrainDrainUntilUserEndsQuiet() => Run((engine, s, played) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        var payloads = new List<ConditioningControlPanel.Services.Chaos.EffectPayload>();
        ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = payloads.Add;
        ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
        try
        {
            var now = new DateTime(2026, 10, 14, 12, 0, 0); engine.Now = () => now;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            now = now.AddSeconds(2); Type(engine, "sit");
            Assert.Empty(payloads); Assert.Empty(played);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            now = now.AddSeconds(2); Type(engine, "sit");
            Assert.Single(payloads); Assert.Single(played);
        }
        finally
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous;
        }
    });
    [Fact] // WPF KeywordTriggerService.cs:1361-1367,1490-1518,1698-1713
    public Task DrainWaitsForAudioRepeatsAndRespectsActionOrder() => RunAsync(async (engine, s, played) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        var events = new List<string>();
        ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = _ => events.Add("drain");
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.PlayCount = 2; audio.DelayBetweenMs = 50;
        var gap = new TaskCompletionSource(); engine.Delay = (_, _) => gap.Task;
        try
        {
            s.KeywordTriggers[0].Actions.Insert(0, new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
            s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
            Type(engine, "sit"); Assert.Equal(new[] { "drain" }, events); Assert.Single(played);
            gap.SetResult(); await Task.Yield();
            Assert.Equal(2, played.Count(x => x.Path != "stopped")); Assert.Equal(new[] { "drain", "drain" }, events);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });
    [Fact] // WPF KeywordTriggerService.cs:1698-1713
    public Task LegacyDrainWaitsForAudioRepeats() => RunAsync(async (engine, s, played) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer; int drains = 0;
        ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = _ => drains++;
        var t = s.KeywordTriggers[0];
        t.AudioFilePath = ((PlayAudioAction)t.Actions[0]).FilePath;
        t.AudioPlayCount = 2; t.AudioDelayBetweenMs = 50; t.DuckAudio = false;
        t.Actions.Clear(); t.VisualEffect = KeywordVisualEffect.BrainDrain;
        var gap = new TaskCompletionSource(); engine.Delay = (_, _) => gap.Task;
        try
        {
            Type(engine, "sit"); Assert.Single(played); Assert.Equal(0, drains);
            gap.SetResult(); await Task.Yield();
            Assert.Equal(2, played.Count(x => x.Path != "stopped")); Assert.Equal(1, drains);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });
    [Fact]
    public void FixtureClearsPanicQuietBeforeAndAfterUse()
    {
        ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
        Run((_, _, _) => Assert.False(ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced()));
        Assert.False(ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced());
    }
    [Fact]
    public void FixtureClearsPanicQuietAfterBody()
    {
        try
        {
            Run((_, _, _) => ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet());
            Assert.False(ConditioningControlPanel.Services.Safety.PanicAftermath.TriggersSilenced());
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    }
    [Fact] // WPF KeywordTriggerService.cs:1361-1367,1490-1518; panic interrupts this head's queued effects.
    public Task PanicDuringRepeatCannotFireTheDelayedDrain() => RunAsync(async (engine, s, _) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer; int drains = 0;
        ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = _ => drains++;
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.PlayCount = 2; audio.DelayBetweenMs = 50;
        s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
        var gap = new TaskCompletionSource(); engine.Delay = (_, _) => gap.Task;
        try
        {
            Type(engine, "sit"); engine.StopAudio();
            gap.SetResult(); await Task.Yield();
            Assert.Equal(0, drains);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });
    [Fact] // WPF KeywordTriggerService.cs:1361-1367,1490-1518,1859-1865
    public Task OverlappingKeywordReplacesClipWithoutCancellingEarlierDispatch() => RunAsync(async (engine, s, played) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer; int drains = 0;
        ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = _ => drains++;
        var savedDuck = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled);
        bool ducked = false; CoreAudio.DuckProvider = _ => ducked = true; CoreAudio.UnduckProvider = _ => ducked = false;
        CoreAudio.DuckGenerationProvider = () => 1; s.AudioDuckingEnabled = true; var first = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        first.DuckSystemAudio = true; first.PlayCount = 2; first.DelayBetweenMs = 50;
        s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
        s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "stand", CooldownSeconds = 1,
            Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = first.FilePath, DuckSystemAudio = false } } });
        var now = new DateTime(2026, 10, 14, 12, 0, 0); engine.Now = () => now;
        var gap = new TaskCompletionSource(); var cleanup = new TaskCompletionSource();
        var ended = new List<Action?>();
        CoreAudio.PlayStoppableProvider = (path, volume, _, _, finish) =>
        { played.Add((path, volume)); ended.Add(finish); return () => played.Add(("stopped", 0)); };
        engine.Delay = (ms, _) => ms == 50 ? gap.Task : cleanup.Task;
        try
        {
            Type(engine, "sit"); Assert.Single(played);
            now = now.AddSeconds(2); Type(engine, "stand");
            Assert.Equal(2, played.Count(x => x.Path != "stopped")); Assert.Equal(0, drains); Assert.True(ducked);
            ended[1]!(); Dispatcher.UIThread.RunJobs(); cleanup.SetResult(); await Task.Yield();
            gap.SetResult(); await Task.Yield();
            Assert.Equal(3, played.Count(x => x.Path != "stopped")); Assert.Equal(1, drains); Assert.True(ducked);
            ended[1]!(); Dispatcher.UIThread.RunJobs(); await Task.Yield();
            Assert.NotEqual("stopped", played.Last().Path); // stale completion cannot stop the replacement
        }
        finally { engine.StopAudio(); (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled) = savedDuck; ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });
    [Fact] // WPF KeywordTriggerService.cs:1361-1388,1859-1894
    public Task ReplacementKeepsSingleClipDuckForOriginalDuration() => RunAsync(async (engine, s, _) =>
    {
        var saved = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled);
        bool ducked = false; int delayMs = 0; var timer = new TaskCompletionSource(); var released = new TaskCompletionSource(); CoreAudio.DuckProvider = _ => ducked = true; CoreAudio.UnduckProvider = _ => { ducked = false; released.TrySetResult(); };
        CoreAudio.DuckGenerationProvider = () => 1; s.AudioDuckingEnabled = true; var first = (PlayAudioAction)s.KeywordTriggers[0].Actions[0]; first.DuckSystemAudio = true;
        s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "stand", Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = first.FilePath, DuckSystemAudio = false } } });
        var now = new DateTime(2026, 10, 14, 12, 0, 0); engine.Now = () => now;
        CoreAudio.PlayStoppableProvider = (_, _, _, start, finish) => { start?.Invoke(TimeSpan.FromSeconds(10)); return () => finish?.Invoke(); };
        engine.Delay = (ms, _) => { delayMs = ms; return timer.Task; };
        try {
            Type(engine, "sit"); Dispatcher.UIThread.RunJobs(); now = now.AddSeconds(2); Type(engine, "stand");
            Dispatcher.UIThread.RunJobs(); Assert.True(ducked); Assert.Equal(10500, delayMs);
            timer.SetResult(); await released.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.False(ducked); }
        finally { engine.StopAudio(); (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled) = saved; }
    });
    [Fact]
    public void EnabledEngineDoesNotClaimAnUnimplementedBaseDrain() => Run((_, s, _) =>
    {
        var running = typeof(CoreEngine).GetField("_running", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!;
        var saved = (running.GetValue(null), s.BrainDrainEnabled, s.AudioOnlySession);
        try { running.SetValue(null, true); s.BrainDrainEnabled = true; s.AudioOnlySession = false; Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.UserDrainRunning()); }
        finally { running.SetValue(null, saved.Item1); (s.BrainDrainEnabled, s.AudioOnlySession) = (saved.BrainDrainEnabled, saved.AudioOnlySession); }
    });
    [Theory]
    [InlineData(false, 30)]
    [InlineData(true, 60)]
    public Task DrainFrameClockUsesSelectedIntervalAndStops(bool highRefresh, int fps) => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var source = new FakeFrames();
        var clock = new ConditioningControlPanel.Avalonia.Controls.Fx.FrameClock(source);
        var interval = ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.FrameInterval(highRefresh);
        try
        {
            ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.StartFrames(clock, interval);
            Assert.Equal(TimeSpan.FromMilliseconds(1000.0 / fps), clock.Interval);
            Assert.True(clock.IsEnabled);
            Assert.Equal(1, source.Count);
        }
        finally { ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.Stop(); }
        Assert.False(clock.IsEnabled);
        Assert.Equal(0, source.Count);
        return Task.CompletedTask;
    });
    [Fact] // WPF KeywordTriggerService.cs:1805-1808, EffectPayloadFiring.cs:70-99
    public void KeywordUsesTimedDrainHandlerWithWpfGuardsAndPanicStop() => Run((engine, s, _) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        var show = ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.ShowOverride;
        var running = ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.UserDrainRunning;
        var schedule = ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.Schedule;
        var strength = s.BrainDrainBlurStrength;
        var motion = s.MotionLevel; var gentle = s.GentleVisuals; var probe = UiMotionPreference.OsProbe;
        UiMotionPreference.OsProbe = () => true; UiMotionPreference.CacheStamp = long.MinValue; s.GentleVisuals = false;
        var scheduled = new List<(Action Fire, TimeSpan Due)>();
        var starts = new List<(int Strength, bool Melt)>();
        bool userDrain = false;
        var now = new DateTime(2026, 10, 14, 12, 0, 0); engine.Now = () => now;
        ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.Seed();
        ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.ShowOverride = (n, melt) => { starts.Add((n, melt)); return true; };
        ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.UserDrainRunning = () => userDrain;
        ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.Schedule = (a, t) => scheduled.Add((a, t));
        try
        {
            s.AwarenessLoopProtectionEnabled = false; s.BrainDrainBlurStrength = 47;
            s.MotionLevel = MotionLevel.Full;
            s.KeywordTriggers[0].Actions = new List<KeywordAction> { new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain } };
            Type(engine, "sit");
            Assert.Equal((47, true), Assert.Single(starts));
            Assert.Equal(TimeSpan.FromSeconds(10), Assert.Single(scheduled).Due);
            Assert.True(ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.IsShowing);
            now = now.AddSeconds(2); Type(engine, "sit"); Assert.Single(starts); // do not overlap
            scheduled[0].Fire(); Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.IsShowing);
            now = now.AddSeconds(2); s.BrainDrainBlurStrength = 0; Type(engine, "sit"); Assert.Single(starts);
            now = now.AddSeconds(2); s.BrainDrainBlurStrength = 47; userDrain = true; Type(engine, "sit"); Assert.Single(starts);
            now = now.AddSeconds(2); userDrain = false; s.MotionLevel = MotionLevel.Reduced; Type(engine, "sit"); Assert.Equal((47, false), starts[1]);
            ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow.StopEngine(); // panic reaches the same stop
            Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.IsShowing);
            now = now.AddSeconds(2); Type(engine, "sit"); Assert.Equal(3, starts.Count);
            scheduled[1].Fire(); // a stale timer cannot stop the new effect
            Assert.True(ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.IsShowing);
            scheduled[2].Fire(); Assert.False(ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.IsShowing);
        }
        finally
        {
            ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.Stop();
            ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.ShowOverride = show;
            ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.UserDrainRunning = running;
            ConditioningControlPanel.Avalonia.Views.Overlays.BrainDrainOverlay.Schedule = schedule;
            ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous;
            s.BrainDrainBlurStrength = strength; s.MotionLevel = motion; s.GentleVisuals = gentle;
            UiMotionPreference.OsProbe = probe; UiMotionPreference.CacheStamp = long.MinValue;
        }
    });
    [Fact] // WPF StopTriggerAudio, KeywordTriggerService.cs:1902-1911; panic cancels queued repeats on this head.
    public Task PanicCancelsAllOverlappingKeywordRepeats() => RunAsync(async (engine, s, played) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer; int drains = 0; ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = _ => drains++;
        var first = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        first.PlayCount = 2; first.DelayBetweenMs = 50;
        s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
        s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "stand", CooldownSeconds = 1,
            Actions = new List<KeywordAction> { new PlayAudioAction { FilePath = first.FilePath, PlayCount = 2, DelayBetweenMs = 50, DuckSystemAudio = false },
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain } } });
        var now = new DateTime(2026, 10, 14, 12, 0, 0); engine.Now = () => now;
        var gap = new TaskCompletionSource(); engine.Delay = (_, _) => gap.Task;
        try
        {
            Type(engine, "sit"); now = now.AddSeconds(2); Type(engine, "stand");
            Assert.Equal(2, played.Count(x => x.Path != "stopped"));
            engine.StopAudio(); gap.SetResult(); await Task.Yield();
            Assert.Equal(2, played.Count(x => x.Path != "stopped")); Assert.Equal(0, drains);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });

}
