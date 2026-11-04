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
using ConditioningControlPanel.Services.Safety;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class KeywordAudioRuntimeTests
{
    private static void Run(Action<KeywordAudioRuntime, AppSettings, List<(string Path, float Volume)>> body) =>
        RunAsync((engine, settings, played) => { body(engine, settings, played); return Task.CompletedTask; }).GetAwaiter().GetResult();

    private static Task RunAsync(Func<KeywordAudioRuntime, AppSettings, List<(string Path, float Volume)>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        PanicAftermath.EndQuiet();
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
            s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled,
            CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider);
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
            CoreSession.IsEngineRunningProvider = () => true;
            CoreSession.IsSessionRunningProvider = () => false;
            CoreAudio.PlayStoppableProvider = (path, volume, _, _, _) => { played.Add((path, volume)); return () => played.Add(("stopped", 0)); };
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Start();
            await body(engine, s, played);
        }
        finally
        {
            PanicAftermath.EndQuiet();
            engine.Stop(); KeywordTriggerHead.Stop(); File.Delete(file);
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled,
                CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider) = saved;
        }
    });

    private static void Type(KeywordAudioRuntime engine, string text) { foreach (var ch in text) engine.Character(ch); }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HarnessClearsInheritedQuietAndCleansUpAfterTheBody(bool throwInBody)
    {
        PanicAftermath.ArmQuiet();
        try
        {
            void Exercise(KeywordAudioRuntime engine, AppSettings settings, List<(string Path, float Volume)> played)
            {
                Assert.False(PanicAftermath.TriggersSilenced());
                Type(engine, "sit");
                Assert.Single(played);
                PanicAftermath.ArmQuiet();
                if (throwInBody) throw new InvalidOperationException();
            }
            if (throwInBody) Assert.Throws<InvalidOperationException>(() => Run(Exercise));
            else Run(Exercise);
            Assert.False(PanicAftermath.TriggersSilenced());
        }
        finally { PanicAftermath.EndQuiet(); }
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public Task MissingActionClipDucksOnlyWhenRunningAndEnabled(bool running, bool enabled) => RunAsync(async (engine, s, played) =>
    {
        var action = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        action.FilePath = "missing-keyword-clip";
        action.DuckSystemAudio = true;
        var old = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled);
        var release = new TaskCompletionSource();
        var unducked = new TaskCompletionSource<long>();
        var delays = new List<int>();
        var ducks = 0;
        try
        {
            CoreSession.IsEngineRunningProvider = () => running;
            s.AudioDuckingEnabled = enabled;
            CoreAudio.DuckProvider = _ => ducks++;
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = generation => unducked.TrySetResult(generation);
            engine.Delay = (ms, token) => { delays.Add(ms); return release.Task; };
            Type(engine, "sit");
            Assert.Empty(played);
            Assert.Equal(running && enabled ? 1 : 0, ducks);
            Assert.Equal(running && enabled ? new[] { 500 } : Array.Empty<int>(), delays);
            if (running && enabled)
            {
                release.SetResult();
                Assert.Equal(42, await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            release.TrySetResult();
            if (delays.Count > 0 && !unducked.Task.IsCompleted)
                try { await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled) = old;
        }
    });

    [Theory]
    [InlineData(false, false, true, true, false, true, false)]
    [InlineData(false, true, false, true, false, true, false)]
    [InlineData(false, false, true, false, false, false, false)]
    [InlineData(true, false, true, true, false, false, false)]
    [InlineData(true, true, false, true, false, true, false)]
    [InlineData(false, false, true, true, true, true, true)]
    [InlineData(false, true, false, true, true, true, true)]
    [InlineData(true, true, false, true, true, true, false)]
    public Task MultipleAudioActionsUseWpfDuckingAndMergeSelection(bool merged, bool firstDuck,
        bool secondDuck, bool secondEnabled, bool secondValid, bool expectedDuck, bool expectedPlay) => RunAsync(async (engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        var first = (PlayAudioAction)trigger.Actions[0];
        var validPath = first.FilePath;
        first.FilePath = "missing-keyword-clip";
        first.DuckSystemAudio = firstDuck;
        var second = new PlayAudioAction { FilePath = secondValid ? validPath : "also-missing-keyword-clip",
            DuckSystemAudio = secondDuck, Enabled = secondEnabled };
        if (merged) s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "sit", CooldownSeconds = 0,
            Actions = new List<KeywordAction> { second } });
        else trigger.Actions.Add(second);
        var old = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled);
        var release = new TaskCompletionSource();
        var unducked = new TaskCompletionSource<long>();
        var delays = new List<int>();
        var ducks = 0;
        try
        {
            s.AudioDuckingEnabled = true;
            CoreAudio.DuckProvider = _ => ducks++;
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = generation => unducked.TrySetResult(generation);
            engine.Delay = (ms, token) => { delays.Add(ms); return release.Task; };
            Type(engine, "sit");
            Assert.Equal(expectedPlay ? 1 : 0, played.Count);
            Assert.Equal(expectedDuck ? 1 : 0, ducks);
            Assert.Equal(expectedDuck && !expectedPlay ? new[] { 500 } : Array.Empty<int>(), delays);
            if (expectedDuck && !expectedPlay)
            {
                Assert.False(unducked.Task.IsCompleted);
                release.SetResult();
                Assert.Equal(42, await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            release.TrySetResult();
            if (delays.Count > 0 && !unducked.Task.IsCompleted)
                try { await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            engine.StopAudio();
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled) = old;
        }
    });

    [Fact]
    public void LaterMissingActionCanDuckAPlayingFirstAction() => Run((engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        trigger.Actions.Add(new PlayAudioAction { FilePath = "missing-keyword-clip", DuckSystemAudio = true });
        var old = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled);
        var ducks = 0;
        try
        {
            s.AudioDuckingEnabled = true;
            CoreAudio.DuckProvider = _ => ducks++;
            CoreAudio.UnduckProvider = _ => { };
            CoreAudio.DuckGenerationProvider = () => 42;
            Type(engine, "sit");
            Assert.Single(played);
            Assert.Equal(1, ducks);
        }
        finally
        {
            engine.StopAudio();
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled) = old;
        }
    });

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

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void PanicQuietSuppressesKeywordAudioUntilUserStartOrExpiry(bool legacy, bool expire) => Run((engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        if (legacy)
        {
            trigger.AudioFilePath = ((PlayAudioAction)trigger.Actions[0]).FilePath;
            trigger.DuckAudio = false;
            trigger.Actions.Clear();
        }
        s.AwarenessLoopProtectionEnabled = false;
        var now = new DateTime(2026, 10, 14, 12, 0, 0);
        engine.Now = () => now;
        var quiet = ConditioningControlPanel.Services.Safety.PanicAftermath.QuietWindow;
        try
        {
            // The quiet surface is armed by tray, voice and the shell panic stop pass.
            PanicSurfaces.All.Single(x => x.Id == "panic-quiet").Stop(null);
            Assert.True(engine.IsActive); // Panic does not turn Awareness off.
            now = now.AddHours(2);
            Type(engine, "sit");
            Assert.Empty(played); // No clip during quiet, including legacy flat audio.
            Assert.Equal(now, trigger.LastTriggeredAt); // WPF records matching before refusing dispatch.
            if (expire)
                ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet(
                    Environment.TickCount64 - (long)quiet.TotalMilliseconds);
            else
                ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            now = now.AddHours(2);
            Type(engine, "sit");
            Assert.Single(played);
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    });

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void AudioRequiresEngineOrSessionAtEachMatch(bool legacy, bool running, bool session) => Run((engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        var audio = (PlayAudioAction)trigger.Actions[0];
        if (legacy)
        {
            trigger.AudioFilePath = audio.FilePath;
            trigger.DuckAudio = false;
            trigger.Actions.Clear();
        }
        else audio.DuckSystemAudio = true;
        var oldDuck = CoreAudio.DuckProvider;
        var oldEnabled = s.AudioDuckingEnabled;
        var ducks = 0;
        try
        {
            s.AudioDuckingEnabled = true;
            CoreAudio.DuckProvider = _ => ducks++;
            CoreSession.IsEngineRunningProvider = () => running;
            CoreSession.IsSessionRunningProvider = () => session;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordPerKeywordCooldownSeconds = 15;
            var now = new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Now = () => now;
            Type(engine, "sit");
            Assert.Equal(running || session ? 1 : 0, played.Count);
            if (!legacy) Assert.Equal(running || session ? 1 : 0, ducks);
            Assert.Equal(now, trigger.LastTriggeredAt);
            if (!running && !session)
            {
                // Starting later does not replay a held match or erase its cooldown.
                running = true;
                Assert.Empty(played);
                now = now.AddSeconds(1);
                Type(engine, "sit");
                Assert.Empty(played);
                now = now.AddSeconds(15);
                Type(engine, "sit");
                Assert.Single(played);
            }
        }
        finally { CoreAudio.DuckProvider = oldDuck; s.AudioDuckingEnabled = oldEnabled; }
    });

    [Theory]
    [InlineData(true, true, false, true, false, false)]
    [InlineData(true, true, false, false, false, false)]
    [InlineData(true, false, false, true, false, false)]
    [InlineData(false, true, false, true, false, false)]
    [InlineData(true, true, true, true, false, false)]
    [InlineData(true, true, false, true, true, false)]
    [InlineData(true, true, false, true, false, true)]
    public Task HeldLegacyMatchDucksAndReleasesAsWpf(bool requested, bool enabled, bool highlightOnly,
        bool hasFile, bool merged, bool quiet) => RunAsync(async (engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        trigger.AudioFilePath = hasFile ? ((PlayAudioAction)trigger.Actions[0]).FilePath : null;
        trigger.Actions.Clear();
        trigger.DuckAudio = requested;
        if (highlightOnly) trigger.VisualEffect = KeywordVisualEffect.HighlightOnly;
        if (merged) s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "sit", CooldownSeconds = 0 });
        var old = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
            s.AudioDuckingEnabled, s.DuckingLevel);
        var release = new TaskCompletionSource();
        var unducked = new TaskCompletionSource<long>();
        var delays = new List<int>();
        var ducks = new List<int>();
        try
        {
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => false;
            s.AudioDuckingEnabled = enabled;
            s.DuckingLevel = 67;
            CoreAudio.DuckProvider = level => ducks.Add(level);
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = generation => unducked.TrySetResult(generation);
            engine.Delay = (ms, token) => { Assert.Equal(System.Threading.CancellationToken.None, token); delays.Add(ms); return release.Task; };
            if (quiet) PanicAftermath.ArmQuiet();
            Type(engine, "sit");
            Assert.Empty(played);
            var waits = requested && !highlightOnly && !merged && !quiet;
            Assert.Equal(waits && enabled ? new[] { 67 } : Array.Empty<int>(), ducks);
            Assert.Equal(waits ? new[] { 500 } : Array.Empty<int>(), delays);
            Assert.False(unducked.Task.IsCompleted);
            engine.StopAudio(); // WPF leaves the pending legacy release independent of clip stop.
            Assert.False(unducked.Task.IsCompleted);
            if (waits)
            {
                release.SetResult();
                Assert.Equal(enabled ? 42 : -1, await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            release.TrySetResult();
            if (delays.Count > 0 && !unducked.Task.IsCompleted)
                try { await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            PanicAftermath.EndQuiet();
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
                s.AudioDuckingEnabled, s.DuckingLevel) = old;
        }
    });

    [Fact]
    public Task OverlappingHeldLegacyDucksReleaseTheirOwnGenerations() => RunAsync(async (engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        trigger.AudioFilePath = ((PlayAudioAction)trigger.Actions[0]).FilePath;
        trigger.Actions.Clear();
        trigger.DuckAudio = true;
        var old = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
            s.AudioDuckingEnabled, s.AwarenessLoopProtectionEnabled);
        var pending = new List<TaskCompletionSource>();
        var releases = new[] { new TaskCompletionSource<long>(), new TaskCompletionSource<long>() };
        var releaseCount = 0;
        var duckCount = 0;
        long generation = 11;
        try
        {
            CoreSession.IsEngineRunningProvider = () => false;
            CoreSession.IsSessionRunningProvider = () => false;
            s.AudioDuckingEnabled = true;
            s.AwarenessLoopProtectionEnabled = false;
            CoreAudio.DuckProvider = _ => duckCount++;
            CoreAudio.DuckGenerationProvider = () => generation;
            CoreAudio.UnduckProvider = value => releases[releaseCount++].TrySetResult(value);
            engine.Delay = (_, _) => { var gate = new TaskCompletionSource(); pending.Add(gate); return gate.Task; };
            var now = new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Now = () => now;
            Type(engine, "sit");
            generation = 12;
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Empty(played);
            Assert.Equal(2, duckCount);
            Assert.Equal(2, pending.Count);
            pending[0].SetResult();
            Assert.Equal(11, await releases[0].Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.False(releases[1].Task.IsCompleted);
            pending[1].SetResult();
            Assert.Equal(12, await releases[1].Task.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        finally
        {
            foreach (var gate in pending) gate.TrySetResult();
            if (pending.Count > 0)
                try { await Task.WhenAll(releases.Take(pending.Count).Select(x => x.Task)).WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (TimeoutException) { }
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
                s.AudioDuckingEnabled, s.AwarenessLoopProtectionEnabled) = old;
        }
    });

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, false)]
    public Task RunningLegacyWithoutClipDucksAndReleases(bool requested, bool enabled, bool missingPath) => RunAsync(async (engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        var file = ((PlayAudioAction)trigger.Actions[0]).FilePath!;
        trigger.Actions.Clear();
        trigger.AudioFilePath = missingPath ? file + ".missing" : null;
        trigger.DuckAudio = requested;
        var old = (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
            s.AudioDuckingEnabled, s.DuckingLevel);
        var release = new TaskCompletionSource();
        var unducked = new TaskCompletionSource<long>();
        var delays = new List<int>();
        var ducks = new List<int>();
        try
        {
            s.AudioDuckingEnabled = enabled;
            s.DuckingLevel = 67;
            CoreAudio.DuckProvider = level => ducks.Add(level);
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = generation => unducked.TrySetResult(generation);
            engine.Delay = (ms, token) => { Assert.Equal(System.Threading.CancellationToken.None, token); delays.Add(ms); return release.Task; };
            Type(engine, "sit");
            Assert.Empty(played);
            Assert.Equal(engine.Now(), trigger.LastTriggeredAt);
            Assert.Equal(requested && enabled ? new[] { 67 } : Array.Empty<int>(), ducks);
            Assert.Equal(requested ? new[] { 500 } : Array.Empty<int>(), delays);
            Assert.False(unducked.Task.IsCompleted);
            engine.StopAudio();
            Assert.False(unducked.Task.IsCompleted);
            if (requested)
            {
                release.SetResult();
                Assert.Equal(enabled ? 42 : -1, await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            }
        }
        finally
        {
            release.TrySetResult();
            if (delays.Count > 0 && !unducked.Task.IsCompleted)
                try { await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)); } catch (TimeoutException) { }
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
                s.AudioDuckingEnabled, s.DuckingLevel) = old;
        }
    });

    [Fact]
    public void LegacyUsesTheStoredPathWhileActionsResolveResources() => Run((engine, s, played) =>
    {
        var name = "keyword-" + Guid.NewGuid().ToString("N") + ".wav";
        var raw = Path.Combine(Environment.CurrentDirectory, name);
        var resourceDir = Path.Combine(AppContext.BaseDirectory, "Resources");
        var resource = Path.Combine(resourceDir, name);
        var trigger = s.KeywordTriggers[0];
        var audio = (PlayAudioAction)trigger.Actions[0];
        var now = engine.Now();
        engine.Now = () => now;
        s.AwarenessLoopProtectionEnabled = false;
        try
        {
            Directory.CreateDirectory(resourceDir);
            File.WriteAllText(resource, "resource");
            audio.FilePath = name;
            trigger.AudioFilePath = name;
            trigger.DuckAudio = false;
            trigger.Actions.Clear();
            File.WriteAllText(raw, "raw");
            Type(engine, "sit");
            Assert.Equal(name, Assert.Single(played).Path);
            engine.StopAudio();
            played.Clear();
            File.Delete(raw);
            now = now.AddSeconds(2);
            Type(engine, "sit"); // A resource with that name is not the stored legacy path.
            Assert.Empty(played);
            trigger.Actions.Add(audio);
            now = now.AddSeconds(2);
            Type(engine, "sit");
            Assert.Equal(resource, Assert.Single(played).Path);
        }
        finally { File.Delete(raw); File.Delete(resource); }
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void HighlightOnlyReturnsEarlyOnlyForLegacy(bool running, bool actions) => Run((engine, s, played) =>
    {
        var trigger = s.KeywordTriggers[0];
        var audio = (PlayAudioAction)trigger.Actions[0];
        trigger.VisualEffect = KeywordVisualEffect.HighlightOnly;
        trigger.AudioFilePath = audio.FilePath;
        trigger.DuckAudio = true;
        if (!actions) trigger.Actions.Clear();
        CoreSession.IsEngineRunningProvider = () => running;
        CoreSession.IsSessionRunningProvider = () => false;
        var oldDuck = CoreAudio.DuckProvider;
        var oldEnabled = s.AudioDuckingEnabled;
        var ducks = 0;
        try
        {
            s.AudioDuckingEnabled = true;
            CoreAudio.DuckProvider = _ => ducks++;
            Type(engine, "sit");
            Assert.Equal(actions && running ? 1 : 0, played.Count);
            Assert.Equal(0, ducks);
            Assert.Equal(engine.Now(), trigger.LastTriggeredAt);
        }
        finally { CoreAudio.DuckProvider = oldDuck; s.AudioDuckingEnabled = oldEnabled; }
    });

    [Fact]
    public void SavedOnSwitchArmsTheRuntimeWhenTheShellStarts() => Run((_, s, _) =>
    {
        KeywordTriggerHead.Stop();
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        _ = new MainShellWindow();   // WPF MainWindow.xaml.cs:341 starts the service at boot
        Assert.True(KeywordTriggerHead.Engine.IsActive);
    });
}
