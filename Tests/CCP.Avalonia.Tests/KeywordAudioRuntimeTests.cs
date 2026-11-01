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
            var now = new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Now = () => now;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].Actions = new List<KeywordAction> {
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain },
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain },
                new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain, Enabled = false },
                new VisualEffectAction { Effect = KeywordVisualEffect.Bubbles } };
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Equal(2, payloads.Count);
            Assert.All(payloads, p => Assert.IsType<ConditioningControlPanel.Services.Chaos.BrainDrainMeltPayload>(p));
            payloads.Clear();
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:drain", Keyword = "sit", CooldownSeconds = 0,
                Actions = new List<KeywordAction> { new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain } } });
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Single(payloads);
            payloads.Clear();
            s.KeywordTriggers.RemoveAt(1);
            s.KeywordTriggers[0].Actions.Clear();
            s.KeywordTriggers[0].VisualEffect = KeywordVisualEffect.BrainDrain;
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Single(payloads);
            payloads.Clear();
            s.KeywordTriggers[0].VisualEffect = KeywordVisualEffect.None;
            now = now.AddSeconds(2);
            Type(engine, "sit");
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
            var now = new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Now = () => now;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Empty(payloads);
            Assert.Empty(played);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Single(payloads);
            Assert.Single(played);
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
        var gap = new TaskCompletionSource();
        engine.Delay = (_, _) => gap.Task;
        try
        {
            s.KeywordTriggers[0].Actions.Insert(0, new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
            s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.BrainDrain });
            Type(engine, "sit");
            Assert.Equal(new[] { "drain" }, events);
            Assert.Single(played);
            gap.SetResult();
            await Task.Yield();
            Assert.Equal(2, played.Count(x => x.Path != "stopped"));
            Assert.Equal(new[] { "drain", "drain" }, events);
        }
        finally { ConditioningControlPanel.Services.Chaos.EffectPayload.Firer = previous; }
    });

    [Fact] // WPF KeywordTriggerService.cs:1698-1713
    public Task LegacyDrainWaitsForAudioRepeats() => RunAsync(async (engine, s, played) =>
    {
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        int drains = 0;
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
        var previous = ConditioningControlPanel.Services.Chaos.EffectPayload.Firer;
        int drains = 0;
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

}
