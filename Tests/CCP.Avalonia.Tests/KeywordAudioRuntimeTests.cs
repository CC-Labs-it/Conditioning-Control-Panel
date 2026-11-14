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
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;
using IconKind = FluentIcons.Common.Symbol;

namespace CCP.Avalonia.Tests;

public sealed class KeywordAudioRuntimeTests
{
    private static DateTime _singletonNow = new(2026, 10, 14, 12, 0, 0);
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
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled, CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider);
        var singletonClock = KeywordTriggerHead.Engine.Now;
        _singletonNow = _singletonNow.AddHours(3);
        KeywordTriggerHead.Engine.Now = () => _singletonNow;
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
            // A panic another test ran leaves its quiet armed, and the quiet holds every keyword (b9368d963).
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreSession.IsEngineRunningProvider = () => true;
            CoreSession.IsSessionRunningProvider = () => false;
            KeywordTriggerHead.Engine.ResetCooldownsForTest();   // the shared engine outlives each test (Stop keeps OCR guards)
            engine.Start();
            await body(engine, s, played);
        }
        finally
        {
            engine.Stop(); KeywordTriggerHead.Stop(); KeywordTriggerHead.Engine.ResetCooldownsForTest(); File.Delete(file);
            KeywordTriggerHead.Engine.Now = singletonClock;
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled, CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider) = saved;
        }
    });

    // restack b220 (#4299, Mich): the OCR consumer and scan lifecycle.
    [Fact] // WPF KeywordTriggerService.cs:193-200,712-739,842-862,913-943
    public void OcrRequiresConsecutivePositionsKeepsThemThroughALapseAndStopAndClearsOnScopeLoss() => Run((engine, s, played) =>
    {
        var saved = (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll, s.AwarenessLoopProtectionEnabled);
        try
        {
            s.ScreenOcrEnabled = true; s.OcrConfirmationScans = 2; s.OcrHighlightAll = true;
            s.AwarenessLoopProtectionEnabled = false;
            var words = new[] { new ScreenOcrService.Word("sit.", new System.Drawing.Rectangle(120, 200, 30, 20)) };
            var fired = 0; engine.TriggerFired += _ => fired++;
            Assert.True(engine.CheckOcrWords(words)); Assert.Equal(0, fired);
            CoreEntitlement.HasPremiumProvider = () => false;
            Assert.False(engine.CheckOcrWords(words));   // WPF :719 returns without clearing the streak
            CoreEntitlement.HasPremiumProvider = () => true;
            Assert.False(engine.CheckOcrWords(words)); Assert.Equal(1, fired);
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 10);   // cooldowns clamp to at least 1 s
            s.KeywordTriggerIgnoreOwnFocus = true;
            Assert.False(engine.CheckOcrWords(words, "ccp", true));
            Assert.True(engine.CheckOcrWords(words, "editor", false)); Assert.Equal(1, fired);
            Assert.False(engine.CheckOcrWords(words, "editor", false)); Assert.Equal(2, fired);
            Assert.False(engine.CheckOcrWords(words, "editor", false)); Assert.Equal(2, fired);
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 20);
            engine.CheckOcrWords(Array.Empty<ScreenOcrService.Word>(), "editor", false);
            Assert.True(engine.CheckOcrWords(words, "editor", false));
            Assert.False(engine.CheckOcrWords(words, "editor", false)); Assert.Equal(3, fired);
            engine.Stop(); engine.Start();
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 30);   // past every cooldown
            Assert.False(engine.CheckOcrWords(words, "editor", false)); Assert.Equal(3, fired);   // still guarded
            Assert.Equal("OCR", engine.GetRecentFires()[0].Source);
        }
        finally { (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll, s.AwarenessLoopProtectionEnabled) = saved; }
    });

    [Fact] // WPF KeywordTriggerService.cs:774-784,999-1154
    public void OcrWholeWordsPhrasesAndRegexMatchWithoutSubstrings() => Run((engine, s, played) =>
    {
        var saved = (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll, s.AwarenessLoopProtectionEnabled);
        try
        {
            s.ScreenOcrEnabled = true; s.OcrConfirmationScans = 1; s.OcrHighlightAll = true;
            s.AwarenessLoopProtectionEnabled = false;
            var fired = 0; engine.TriggerFired += _ => fired++;
            ScreenOcrService.Word W(string text, int x) => new(text, new System.Drawing.Rectangle(x, 0, 20, 10));
            engine.CheckOcrWords([W("sitting", 0)]); Assert.Equal(0, fired);
            s.KeywordTriggers[0].Keyword = "good boy";
            engine.CheckOcrWords([W("Good,", 0), W("boy!", 30)]); Assert.Equal(1, fired);
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 10);
            engine.CheckOcrWords(Array.Empty<ScreenOcrService.Word>());
            s.KeywordTriggers[0].MatchType = KeywordMatchType.Regex;
            s.KeywordTriggers[0].Keyword = "good\\s+boy";
            engine.CheckOcrWords([W("good", 0), W("boy", 30)]); Assert.Equal(2, fired);
            engine.CheckOcrWords(Array.Empty<ScreenOcrService.Word>());
            s.KeywordTriggers[0].Keyword = "[";
            engine.CheckOcrWords([W("good", 0)]); Assert.Equal(2, fired);
        }
        finally { (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll, s.AwarenessLoopProtectionEnabled) = saved; }
    });

    [Fact] // WPF MainWindow.Awareness.cs:414-428,461-468, Patreon.cs:190; ScreenOcrService.cs:143-198
    public Task AwarenessSwitchesDriveTheOcrSourceIntoTheConsumer() => RunAsync(async (_, s, played) =>
    {
        var saved = (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll, s.AwarenessIgnoreOwnUi, s.ScreenOcrIntervalMs, s.AwarenessLoopProtectionEnabled);
        var realNow = KeywordTriggerHead.Engine.Now;
        var scans = 0;
        var clock = new DateTime(2026, 10, 14, 12, 0, 0);
        KeywordTriggerHead.Engine.Now = () => clock;
        KeywordTriggerHead.OcrSource = _ => { scans++; return Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>(
            [new("sit", new System.Drawing.Rectangle(500, 500, 20, 10))]); };
        try
        {
            s.KeywordTriggersEnabled = false; s.ScreenOcrEnabled = false;
            s.OcrConfirmationScans = 1; s.OcrHighlightAll = true; s.AwarenessIgnoreOwnUi = true;
            s.ScreenOcrIntervalMs = 10000;   // the real timer must not tick between the manual ticks
            s.AwarenessLoopProtectionEnabled = false;   // no echo mute: only the position guard keeps it quiet
            var view = new AwarenessTabView();
            var master = view.FindControl<CheckBox>("ChkAwarenessMaster")!;
            var ocr = view.FindControl<CheckBox>("ChkAwarenessOcr")!;
            master.IsChecked = true;
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(0, scans);   // OCR switch off
            ocr.IsChecked = true;
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            Assert.Single(played);
            Assert.Equal("OCR", KeywordTriggerHead.Engine.GetRecentFires()[0].Source);
            CoreEntitlement.HasPremiumProvider = () => false;   // lapse: the screen is not even captured
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            CoreEntitlement.HasPremiumProvider = () => true;
            clock = clock.AddSeconds(10);   // past every cooldown
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(2, scans);
            Assert.Single(played);   // the fired position stays guarded while it is on screen
            master.IsChecked = false;
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(2, scans);
            clock = clock.AddSeconds(10);
            master.IsChecked = true;
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(3, scans);
            ocr.IsChecked = false;
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(3, scans);
            Assert.Equal(1, played.Count(p => p.Path != "stopped"));   // the guard survived the restart (WPF :193-200)
        }
        finally
        {
            KeywordTriggerHead.OcrSource = null; KeywordTriggerHead.Engine.Now = realNow;
            (s.ScreenOcrEnabled, s.OcrConfirmationScans, s.OcrHighlightAll, s.AwarenessIgnoreOwnUi, s.ScreenOcrIntervalMs, s.AwarenessLoopProtectionEnabled) = saved;
        }
    });

    [Fact] // WPF MainWindow.xaml.cs:1157,1192-1206 (restack b221, #4551)
    public Task PanicSuspendsOcrAndRecoveryChecksSwitchesAndAccess() => RunAsync(async (_, s, _) =>
    {
        var saved = (s.ScreenOcrEnabled, s.ScreenOcrIntervalMs);
        var oldTeardown = PanicWatchdog.Teardown;
        var oldPost = PanicWatchdog.PostRecovery;
        Action? recovery = null;
        var scans = 0;
        var scansDuringFallback = -1;
        KeywordTriggerHead.OcrSource = _ => { scans++; return Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([]); };
        try
        {
            s.KeywordTriggersEnabled = true; s.ScreenOcrEnabled = true; s.ScreenOcrIntervalMs = 60000;
            KeywordTriggerHead.Sync();
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            PanicWatchdog.PostRecovery = action => recovery = action;
            PanicWatchdog.Teardown = () =>
            {
                KeywordTriggerHead.SyncOcr(); // a concurrent switch sync cannot restart capture
                _ = KeywordTriggerHead.TickOcrForTest();
                scansDuringFallback = scans;
            };
            await PanicWatchdog.Arm(new TaskCompletionSource().Task, TimeSpan.FromMilliseconds(10));
            Assert.Equal(1, scansDuringFallback);
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            Assert.NotNull(recovery);
            s.KeywordTriggersEnabled = false; recovery!();
            s.KeywordTriggersEnabled = true; await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            var generation = KeywordTriggerHead.SuspendOcrForPanic();
            s.ScreenOcrEnabled = false; KeywordTriggerHead.RecoverOcrAfterPanic(generation);
            s.ScreenOcrEnabled = true; await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            generation = KeywordTriggerHead.SuspendOcrForPanic();
            CoreEntitlement.HasPremiumProvider = () => false;
            KeywordTriggerHead.RecoverOcrAfterPanic(generation);
            CoreEntitlement.HasPremiumProvider = () => true;
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            var older = KeywordTriggerHead.SuspendOcrForPanic();
            generation = KeywordTriggerHead.SuspendOcrForPanic();
            KeywordTriggerHead.RecoverOcrAfterPanic(older);
            KeywordTriggerHead.SyncOcr(); await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            KeywordTriggerHead.RecoverOcrAfterPanic(generation);
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(2, scans);
        }
        finally
        {
            PanicWatchdog.Teardown = oldTeardown; PanicWatchdog.PostRecovery = oldPost;
            KeywordTriggerHead.RecoverOcrAfterPanic(KeywordTriggerHead.SuspendOcrForPanic());
            KeywordTriggerHead.OcrSource = null;
            (s.ScreenOcrEnabled, s.ScreenOcrIntervalMs) = saved;
        }
    });


    [Fact] // owner call 2026-10-10 (handoff-1010/PARITY-CHECKPOINT-1010.md): panic switches keyword triggers off
    public Task PanicPassSwitchesKeywordTriggersOffUntilTheUserTurnsThemBackOn() => RunAsync(async (_, s, played) =>
    {
        var saved = (s.ScreenOcrEnabled, s.ScreenOcrIntervalMs);
        var scans = 0;
        KeywordTriggerHead.OcrSource = _ => { scans++; return Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([]); };
        try
        {
            s.KeywordTriggersEnabled = true; s.ScreenOcrEnabled = true; s.ScreenOcrIntervalMs = 60000;
            KeywordTriggerHead.Sync();
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            var row = Assert.Single(PanicSurfaces.All, x => x.Id == "keyword-triggers");
            row.Stop(null);
            Assert.False(s.KeywordTriggersEnabled);   // the per-run master
            Assert.False(s.ScreenOcrEnabled);         // the saved screen read
            Assert.False(KeywordTriggerHead.Engine.IsActive);
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            KeywordTriggerHead.SyncOcr(); KeywordTriggerHead.Sync();   // nothing re-arms on its own
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            Type(KeywordTriggerHead.Engine, "sit"); Assert.Empty(played);
            row.Stop(null);   // idempotent with nothing on
            s.KeywordTriggersEnabled = true; s.ScreenOcrEnabled = true; KeywordTriggerHead.Sync();   // the user turns them back on
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(2, scans);
        }
        finally
        {
            KeywordTriggerHead.OcrSource = null;
            (s.ScreenOcrEnabled, s.ScreenOcrIntervalMs) = saved;
        }
    });

    [Fact] // hard rule 6 (restack fixkwstop): the 6-blink stop reaches the same real row, never a second copy
    public Task BlinkStopSwitchesKeywordTriggersOffThroughThePanicRow() => RunAsync(async (_, s, played) =>
    {
        var saved = (s.ScreenOcrEnabled, s.ScreenOcrIntervalMs);
        var prev = PanicSurfaces.All;
        var scans = 0;
        KeywordTriggerHead.OcrSource = _ => { scans++; return Task.FromResult<IReadOnlyList<ScreenOcrService.Word>>([]); };
        try
        {
            // Only the real keyword row: the other blink rows (engine, audio) are not this test's business.
            PanicSurfaces.All = new[] { Assert.Single(prev, x => x.Id == "keyword-triggers") };
            s.KeywordTriggersEnabled = true; s.ScreenOcrEnabled = true; s.ScreenOcrIntervalMs = 60000;
            KeywordTriggerHead.Sync();
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            MainShellWindow.StopAllForRecalibration(null);
            Assert.False(s.KeywordTriggersEnabled);
            Assert.False(s.ScreenOcrEnabled);
            Assert.False(KeywordTriggerHead.Engine.IsActive);
            await KeywordTriggerHead.TickOcrForTest(); Assert.Equal(1, scans);
            Type(KeywordTriggerHead.Engine, "sit"); Assert.Empty(played);
        }
        finally
        {
            PanicSurfaces.All = prev;
            KeywordTriggerHead.Stop();
            KeywordTriggerHead.OcrSource = null;
            (s.ScreenOcrEnabled, s.ScreenOcrIntervalMs) = saved;
        }
    });

    [Fact] // WPF BarkService.cs:1972,2008 (SelfEchoMuteMs 8000); KeywordTriggerService.cs:128-146
    public void SpokenBarkMutesItsExactLineForEightSeconds() => Run((engine, s, played) =>
    {
        var saved = (s.AwarenessLoopProtectionEnabled, s.AvatarEnabled, s.BarkChatSuppressionMs, s.BarkVariantRotation, BarkHead.EchoTarget);
        var clock = new DateTime(2026, 10, 14, 12, 0, 0);
        try
        {
            s.BarkVariantRotation = s.BarkVariantRotation.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
            s.AwarenessLoopProtectionEnabled = false; s.AvatarEnabled = true; s.BarkChatSuppressionMs = 0;
            engine.Now = () => clock;
            BarkHead.EchoTarget = () => engine;
            var rule = new ConditioningControlPanel.Services.Bark.BarkRule { Id = "echo", Trigger = "T",
                VariantPool = [new ConditioningControlPanel.Services.Bark.BarkVariant("sit")] };
            var bark = new ConditioningControlPanel.Services.Bark.BarkEngine(
                () => new ConditioningControlPanel.Services.Bark.BarkRuleSet([rule]), new Random(7)) { Speak = _ => { } };
            BarkHead.AttachEcho(bark); bark.Start();
            Assert.True(bark.Raise("T"));
            Type(engine, "sit"); Assert.Empty(played);
            engine.ClearInput(); clock = clock.AddSeconds(7.9); Type(engine, "sit"); Assert.Empty(played);
            engine.ClearInput(); clock = new DateTime(2026, 10, 14, 12, 0, 8); Type(engine, "sit"); Assert.Single(played);
        }
        finally
        {
            (s.AwarenessLoopProtectionEnabled, s.AvatarEnabled, s.BarkChatSuppressionMs, s.BarkVariantRotation, BarkHead.EchoTarget) = saved;
        }
    });

    private static void Type(KeywordAudioRuntime engine, string text) { foreach (var ch in text) engine.Character(ch); }

    // restack b186: ducking slices of #3968, #4057 and #4139 (Mich), adapted to the trunk dispatcher.
    // A match that ducks but never plays a clip unducks after 500 ms; with ducking off nothing is released.
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
            else Assert.False(unducked.Task.IsCompleted);
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

    // restack b220 (#4235, Mich): WPF DispatchActionAsync :1638 plays every audio action of one trigger in order.
    [Fact]
    public void SingleTriggerDispatchesBothAudioActionsInOrder() => Run((engine, s, played) =>
    {
        var first = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        s.KeywordTriggers[0].Actions.Add(new PlayAudioAction { FilePath = first.FilePath, Volume = 100, DuckSystemAudio = false });
        Type(engine, "sit");
        Assert.Equal(new[] { 0.125f, (float)Math.Pow(0.5, 1.5) }, played.Where(p => p.Path != "stopped").Select(p => p.Volume));
        Assert.Equal(new[] { first.FilePath, "stopped", first.FilePath }, played.Select(p => p.Path));   // the second replaces the first
    });

    // restack b220 (#4235): one duck over the response (WPF :1594-1628). The second clip ducks before the
    // replaced first clip lets go, so the shared count never reaches zero between them; the last clip's duck
    // ends 500 ms after it finishes, and a stop releases it at once.
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task BatchDuckSurvivesClipReplacementAndReleasesAfterTheLastClip(bool firstDuck, bool secondDuck) => RunAsync(async (engine, s, played) =>
    {
        var saved = (s.AudioDuckingEnabled, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, CoreAudio.PlayStoppableProvider);
        var finishes = new List<Action>();
        var calls = new List<string>();
        var tail = new TaskCompletionSource();
        var tailStarted = new TaskCompletionSource();
        var count = 0;
        try
        {
            s.AudioDuckingEnabled = true;
            CoreAudio.DuckProvider = _ => calls.Add("duck:" + ++count);
            CoreAudio.DuckGenerationProvider = () => 42;
            CoreAudio.UnduckProvider = gen => calls.Add($"unduck:{gen}:{--count}");
            CoreAudio.PlayStoppableProvider = (_, _, _, _, finish) => { finishes.Add(finish!); return () => calls.Add("stop"); };
            engine.Delay = (ms, _) => { Assert.Equal(500, ms); tailStarted.TrySetResult(); return tail.Task; };
            var first = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
            first.DuckSystemAudio = firstDuck;
            s.KeywordTriggers[0].Actions.Add(new PlayAudioAction { FilePath = first.FilePath, DuckSystemAudio = secondDuck });
            Type(engine, "sit");
            Assert.Equal(2, finishes.Count);
            Assert.Equal(new[] { "duck:1", "duck:2", "stop", "unduck:42:1" }, calls);   // never back to zero
            finishes[1]();
            await tailStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(4, calls.Count);
            tail.SetResult();
            for (var until = DateTime.UtcNow.AddSeconds(5); calls.Count < 6 && DateTime.UtcNow < until;)
                await Task.Delay(10);
            Assert.Equal(new[] { "duck:1", "duck:2", "stop", "unduck:42:1", "stop", "unduck:42:0" }, calls);
            engine.StopAudio();
            Assert.Equal(6, calls.Count);   // nothing left to release
        }
        finally
        {
            tail.TrySetResult();
            engine.StopAudio();
            (s.AudioDuckingEnabled, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, CoreAudio.PlayStoppableProvider) = saved;
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
            if (quiet) ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Type(engine, "sit");
            Assert.Empty(played);
            var waits = requested && enabled && !highlightOnly && !merged && !quiet;
            Assert.Equal(waits ? new[] { 67 } : Array.Empty<int>(), ducks);
            Assert.Equal(waits ? new[] { 500 } : Array.Empty<int>(), delays);
            Assert.False(unducked.Task.IsCompleted);
            engine.StopAudio(); // WPF leaves the pending legacy release independent of clip stop.
            Assert.False(unducked.Task.IsCompleted);
            if (waits)
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
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
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
            var waits = requested && enabled;
            Assert.Equal(waits ? new[] { 67 } : Array.Empty<int>(), ducks);
            Assert.Equal(waits ? new[] { 500 } : Array.Empty<int>(), delays);
            Assert.False(unducked.Task.IsCompleted);
            engine.StopAudio();
            Assert.False(unducked.Task.IsCompleted);
            if (waits)
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

    /// <summary>#4416 (restack b204): every permitted keyboard match feeds the program verifier once per
    /// trigger through QuestService; entitlement and panic quiet hold it (WPF KeywordTriggerService.cs:1253).</summary>
    [Fact]
    public void KeyboardMatchesForwardEachPermittedTriggerToProgramVerifier() => Run((engine, s, played) =>
    {
        var dir = Directory.CreateTempSubdirectory("ccp-keyword-program-").FullName;
        var oldQuests = ConditioningControlPanel.Avalonia.App.Quests;
        var oldVerifier = CoreQuests.TrackProgramVerifierProvider;
        var quests = new ConditioningControlPanel.Services.QuestService(null, dir);
        var credits = new List<(QuestCategory, int)>();
        try
        {
            ConditioningControlPanel.Avalonia.App.Quests = quests;
            CoreQuests.TrackProgramVerifierProvider = (category, amount) => credits.Add((category, amount));
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:one", Keyword = "sit", CooldownSeconds = 0 });
            CoreEntitlement.HasPremiumProvider = () => false;
            Type(engine, "sit");
            Assert.Empty(credits);
            CoreEntitlement.HasPremiumProvider = () => true;
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Type(engine, "sit");
            Assert.Empty(credits);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 2);
            Type(engine, "sit");
            Assert.Equal(new[] { (QuestCategory.KeywordTrigger, 1), (QuestCategory.KeywordTrigger, 1) }, credits);
        }
        finally
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            CoreQuests.TrackProgramVerifierProvider = oldVerifier;
            ConditioningControlPanel.Avalonia.App.Quests = oldQuests;
            quests.Dispose();
            try { Directory.Delete(dir, true); } catch { }
        }
    });

    [Fact]   // WPF FireDemoTrigger never reads saved triggers or plays audio (#887 back door)
    public void TutorialDemoNeverPlaysTheSavedTriggerAudio() => Run((engine, s, played) =>
    {
        var fired = new List<KeywordTrigger>(); engine.TriggerFired += fired.Add;
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.FireDemo("sit", "tutorial");
        Assert.Empty(engine.GetRecentFires()); Assert.Empty(fired);
        CoreEntitlement.HasPremiumProvider = () => true;
        engine.Stop(); s.KeywordTriggersEnabled = false; engine.FireDemo("  ", "tutorial"); Assert.Empty(fired);
        engine.FireDemo("sit", "tutorial");
        var record = Assert.Single(engine.GetRecentFires()); Assert.Equal(("sit", "tutorial", engine.Now()), (record.Keyword, record.Source, record.FiredAt));
        Assert.Equal(new[] { "Highlight", "AvatarComment" }, record.ActionKeys);
        var demo = Assert.Single(fired); var comment = Assert.IsType<AvatarCommentAction>(demo.Actions[1]); Assert.Equal(("tutorial:demo", "PuppyPraise", false), (demo.Id, comment.FallbackPhraseCategory, comment.RequireAiAvailable));
        CoreEntitlement.HasPremiumProvider = () => false; CoreEntitlement.IsFreeTodayProvider = _ => true; engine.FireDemo("sit", "tutorial"); Assert.Equal(2, fired.Count);
        Assert.Empty(played);
    });

    [Fact]
    public void AcceptedMatchesRecordBeforeNotificationAndRespectEveryGate() => Run((engine, s, _) =>
    {
        var notifications = 0;
        engine.TriggerFired += trigger =>
        {
            notifications++;
            var record = engine.Fires.Recent()[0];
            Assert.Equal(trigger.Id, record.TriggerId);
            Assert.Equal("Keyboard", record.Source);
            Assert.Equal(engine.Now(), record.FiredAt);
        };
        CoreEntitlement.HasPremiumProvider = () => false;
        Type(engine, "sit"); Assert.Empty(engine.Fires.Recent());
        CoreEntitlement.HasPremiumProvider = () => true;
        s.KeywordTriggersEnabled = false;
        Type(engine, "sit"); Assert.Empty(engine.Fires.Recent());
        s.KeywordTriggersEnabled = true;
        s.KeywordTriggerIgnoreOwnFocus = true;
        foreach (var ch in "sit") engine.Character(ch, "ccp", true);
        Assert.Empty(engine.Fires.Recent());
        s.KeywordGlobalCooldownSeconds = 10;
        Type(engine, "sit");
        Assert.Single(engine.Fires.Recent());
        Type(engine, "sit");
        Assert.Equal(1, notifications);
        engine.Stop(); Type(engine, "sit");
        Assert.Single(engine.Fires.Recent()); // stop retains the session history, as WPF does
    });

    [Fact]
    public void DemoHistoryNeedsEntitlementButBypassesMasterAndCooldowns() => Run((engine, s, played) =>
    {
        var notifications = 0;
        engine.TriggerFired += _ => notifications++;
        engine.Stop(); s.KeywordTriggersEnabled = false;
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.FireDemo("sit", "Tutorial");
        Assert.Empty(engine.Fires.Recent());
        CoreEntitlement.HasPremiumProvider = () => true;
        engine.FireDemo(" ", "Tutorial");
        Assert.Empty(engine.Fires.Recent());
        engine.FireDemo("sit", "Tutorial"); engine.FireDemo("sit", "Tutorial");
        Assert.Equal(2, notifications);
        Assert.Equal(2, engine.Fires.Recent().Count);
        var record = engine.Fires.Recent()[0];
        Assert.Equal("tutorial:demo", record.TriggerId);
        Assert.Equal("Tutorial", record.Source);
        Assert.Equal(new[] { "Highlight", "AvatarComment" }, record.ActionKeys);
        Assert.Empty(played);
    });

    [Fact]
    public void DemoMutesSavedKeyboardActionsUntilExpiryAndRepeatedDemosExtendTheMute() => Run((engine, s, played) =>
    {
        var now = engine.Now();
        engine.Now = () => now;
        s.AwarenessLoopProtectionEnabled = false;
        s.KeywordPerKeywordCooldownSeconds = 10;
        void TryKeyboardMatch()
        {
            engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
            Type(engine, "SIT");
        }
        // The mute must already apply when subscribers receive the demo notification.
        engine.TriggerFired += trigger => { if (trigger.Id == "tutorial:demo") TryKeyboardMatch(); };
        engine.FireDemo("sit", "Tutorial");
        Assert.Empty(played);
        Assert.Single(engine.Fires.Recent());

        now = now.AddSeconds(5);
        engine.FireDemo("sit", "Tutorial");
        engine.FireDemo("sit", "Tutorial");
        Assert.Equal(3, engine.Fires.Recent().Count); // demos bypass their own mute
        Assert.Empty(played);

        now = now.AddMilliseconds(9999);
        TryKeyboardMatch();
        Assert.Empty(played); // later demos extended the original expiry
        Assert.Equal(3, engine.Fires.Recent().Count);
        now = now.AddMilliseconds(1);
        TryKeyboardMatch();
        Assert.Single(played);
        Assert.Equal(4, engine.Fires.Recent().Count);
        Assert.Equal("Keyboard", engine.Fires.Recent()[0].Source);
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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task OverlappingFiresWaitForRepeatsAndStopOrPanicCancelsBoth(int stop) => RunAsync(async (engine, s, played) =>
    {
        var calls = new List<string>();
        engine.Haptic = (_, _) => calls.Add("haptic"); engine.AddXp = _ => calls.Add("xp");
        engine.ChasterBook = _ => { calls.Add("chaster"); return 0; };
        var finished = new List<Action?>(); var play = CoreAudio.PlayStoppableProvider!;
        CoreAudio.PlayStoppableProvider = (path, volume, tag, started, done) =>
        { finished.Add(done); return play(path, volume, tag, started, done); };
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.PlayCount = 2; audio.DelayBetweenMs = 50;
        s.KeywordTriggers[0].Actions.AddRange(new KeywordAction[] { new HapticAction(),
            new AddXpAction { Amount = 5 }, new ChasterAddTimeAction { Minutes = 1 } });
        var gap = new TaskCompletionSource(); engine.Delay = (_, _) => gap.Task;
        try
        {
            Type(engine, "sit"); Assert.Single(played); Assert.Empty(calls);
            var first = engine.LastDispatch;
            s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "lie", Actions = new List<KeywordAction> {
                new PlayAudioAction { FilePath = audio.FilePath, DuckSystemAudio = false }, new HapticAction(),
                new AddXpAction { Amount = 5 }, new ChasterAddTimeAction { Minutes = 1 } } });
            var later = engine.Now().AddSeconds(2); engine.Now = () => later;
            Type(engine, "lie"); Assert.NotSame(first, engine.LastDispatch); await engine.LastDispatch.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(new[] { "haptic", "xp", "chaster" }, calls); calls.Clear();
            if (stop == 1) engine.Stop();
            if (stop == 2) { engine.StopAudio(); ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet(); }
            gap.SetResult(); await first.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(stop == 0 ? new[] { "haptic", "xp", "chaster" } : Array.Empty<string>(), calls);
            Assert.Equal(stop == 0 ? 3 : 2, played.Count(x => x.Path != "stopped"));
            if (stop == 0) { finished[1]!(); Dispatcher.UIThread.RunJobs(); Assert.NotEqual("stopped", played.Last().Path); }
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
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

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void LostAccessOrMasterGatePreservesPartialInput(bool loseAccess) => Run((engine, s, played) =>
    {
        engine.Character('s');
        if (loseAccess) CoreEntitlement.HasPremiumProvider = () => false;
        else s.KeywordTriggersEnabled = false;
        engine.Character('x'); // a rejected key must not enter the retained buffer
        CoreEntitlement.HasPremiumProvider = () => true;
        s.KeywordTriggersEnabled = true;
        Type(engine, "it");
        Assert.Single(played);
    });

    [Fact]
    public void KeyboardDetachClearsPartialInput() => Run((_, s, played) =>
    {
        KeywordTriggerHead.Engine.Start();
        KeywordTriggerHead.Engine.Character('s');
        KeywordTriggerHead.DetachKeyboard();
        Type(KeywordTriggerHead.Engine, "it");
        Assert.Empty(played);
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
    public void AwarenessSwitchStartsRuntimeAndPanicSwitchesItOff() => Run((_, s, played) =>
    {
        // Linux headless test deliberately never installs a native keyboard hook.
        s.KeywordTriggersEnabled = false;
        var clock = _singletonNow;
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
        // Owner call 2026-10-10: panic switches them off (WPF main leaves the service running).
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        Assert.False(s.KeywordTriggersEnabled);
        clock = clock.AddHours(2);   // the view clamps cooldowns to AwarenessCooldownScale (1 s..1 h); step past them
        Type(KeywordTriggerHead.Engine, "sit"); Assert.Equal(2, played.Count);
        view.SyncAwarenessTabUi();   // the shell repaints the tab after the stop pass
        Assert.False(view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked);
        view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = true;   // the user turns them back on
        Assert.True(KeywordTriggerHead.Engine.IsActive);
        Type(KeywordTriggerHead.Engine, "sit"); Assert.Equal(3, played.Count);
        // WPF MainWindow.Awareness.cs:493: Keyboard off drops the key source only; engine and clip stay.
        view.FindControl<CheckBox>("ChkAwarenessKeyboard")!.IsChecked = false;
        Assert.True(KeywordTriggerHead.Engine.IsActive);
        Assert.NotEqual("stopped", played.Last().Path);
        view.FindControl<CheckBox>("ChkAwarenessMaster")!.IsChecked = false;
        }
        finally { KeywordTriggerHead.Engine.Now = realNow; }
    });

    [Fact]   // WPF DispatchMergedAsync :1284 + DispatchActionAsync :1411 + RecordFire :135
    public void ResponseActionsMergeOncePerTypeCreditEachTriggerAndFeedThePulse() => Run((engine, s, played) =>
    {
        var calls = new List<string>();
        engine.QuestCredit = () => calls.Add("quest");
        engine.Haptic = (k, i) => calls.Add($"haptic:{k}:{Math.Round(i * 10)}");
        engine.AddXp = a => calls.Add($"xp:{a}");
        var fired = new List<string>();
        engine.TriggerFired += t => fired.Add(t.Id);
        s.KeywordTriggers[0].Id = "one";
        s.KeywordTriggers[0].Actions.AddRange(new KeywordAction[] { new HapticAction { Intensity = 0.4 },
            new AddXpAction { Amount = 5 }, new HighlightAction(), new HapticAction { Intensity = 0.2, Enabled = false } });
        s.KeywordTriggers.Add(new KeywordTrigger { Id = "two", Keyword = "sit", CooldownSeconds = 0, Actions = new List<KeywordAction> {
            new HapticAction { Intensity = 0.9 }, new AddXpAction { Amount = 9 } } });
        Type(engine, "sit");
        Assert.Equal(new[] { "quest", "quest", "haptic:sit:4", "xp:5" }, calls);
        Assert.Single(played);
        Assert.Equal(new[] { "one", "two" }, fired);
        var feed = engine.GetRecentFires();
        Assert.Equal(new[] { "two", "one" }, feed.Select(f => f.TriggerId));
        Assert.Equal(new[] { "PlayAudio", "Haptic", "Highlight" }, feed[1].ActionKeys);   // AddXp never shows
        Assert.All(feed, f => Assert.Equal("Keyboard", f.Source));
    });
    [Fact]   // WPF DispatchLegacyFlatFieldsAsync :1692: flat haptic/XP still fire; HighlightOnly runs nothing else
    public void LegacyFlatTriggerFiresHapticAndXp() => Run((engine, s, played) =>
    {
        var calls = new List<string>();
        engine.Haptic = (k, i) => calls.Add($"haptic:{Math.Round(i * 10)}");
        engine.AddXp = a => calls.Add($"xp:{a}");
        s.KeywordTriggers[0] = new KeywordTrigger { Keyword = "sit", CooldownSeconds = 0, Actions = new List<KeywordAction>(),
            HapticEnabled = true, HapticIntensity = 0.7, XPAward = 7 };
        Type(engine, "sit");
        Assert.Equal(new[] { "haptic:7", "xp:7" }, calls);
        s.KeywordTriggers[0].VisualEffect = KeywordVisualEffect.HighlightOnly;
        engine.Now = () => new DateTime(2026, 10, 14, 14, 0, 0);
        Type(engine, "sit");
        Assert.Equal(2, calls.Count);
    });
    [Fact]   // WPF DispatchChasterAddTime :1485: per-trigger day allowance (3 bookings / 15 min), persisted
    public void ChasterActionBooksWithinTheTriggersDayAllowance() => Run((engine, s, played) =>
    {
        var dir = Directory.CreateTempSubdirectory();
        try
        {
            var booked = new List<int>();
            engine.ChasterBook = seconds => { booked.Add(seconds); return seconds; };
            engine.ChasterCapPath = () => Path.Combine(dir.FullName, "caps.json");
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].Actions = new List<KeywordAction> { new ChasterAddTimeAction { Minutes = 10 } };
            var now = new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Now = () => now;
            for (var i = 0; i < 3; i++) { Type(engine, "sit"); now = now.AddSeconds(2); }   // past the 1 s per-word floor
            Assert.Equal(new[] { 600, 300 }, booked);
            Assert.True(File.Exists(engine.ChasterCapPath()));
            var persisted = ConditioningControlPanel.Services.KeywordTriggerChasterCap.Load(engine.ChasterCapPath());
            Assert.Equal(0, persisted.Allowance(s.KeywordTriggers[0].Id, 60, now));
            Assert.Equal(60, persisted.Allowance(s.KeywordTriggers[0].Id, 60, now.AddDays(1)));
        }
        finally { dir.Delete(true); }
    });
    [Fact]   // WPF RefreshAwarenessPulseFeed :144: a fire shows as a row with icon chips; none shows the empty state
    public void AwarenessPulseFeedShowsTheFireAsARow() => Run((engine, _, _) =>
    {
        var view = new AwarenessTabView { PulseSource = engine };
        var feed = view.FindControl<StackPanel>("AwarenessPulseFeed")!;
        view.RefreshAwarenessPulseFeed();
        Assert.IsType<TextBlock>(Assert.Single(feed.Children));   // empty state
        Type(engine, "sit");
        view.RefreshAwarenessPulseFeed();
        var row = Assert.IsType<Border>(Assert.Single(feed.Children));
        Assert.Contains(row.GetLogicalDescendants().OfType<IconGlyph>(), g => g.Kind == IconKind.Speaker2);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("awareness_pulse_count", 1), view.FindControl<TextBlock>("TxtAwarenessFireCount")!.Text);
        Assert.Equal(IconKind.Speaker2, AwarenessTabView.ActionChip("PlayAudio")!.Value.Kind);
        Assert.Null(AwarenessTabView.ActionChip("AddXp"));
        Assert.Equal("2m ago", AwarenessTabView.FormatTimeAgo(new DateTime(2026, 1, 1, 12, 0, 0), new DateTime(2026, 1, 1, 12, 2, 5)));
    });
    [Fact]
    public void SessionXpUsesTheConfiguredMultiplierAndDisabledActionsDoNothing() => Run((engine, s, _) =>
    {
        var amounts = new List<double>();
        engine.AddXp = amounts.Add;
        var saved = (CoreSession.IsSessionRunningProvider, s.KeywordSessionMultiplier);
        try
        {
            s.KeywordSessionMultiplier = 2.5;
            CoreSession.IsSessionRunningProvider = () => true;
            s.KeywordTriggers[0].Actions = new List<KeywordAction> { new AddXpAction { Amount = 4 },
                new AddXpAction { Amount = 90, Enabled = false } };
            Type(engine, "sit");
            Assert.Equal(new[] { 10.0 }, amounts);
        }
        finally { (CoreSession.IsSessionRunningProvider, s.KeywordSessionMultiplier) = saved; }
        Assert.Same(saved.Item1, CoreSession.IsSessionRunningProvider);
        Assert.Equal(saved.Item2, s.KeywordSessionMultiplier);
    });
    [Fact]
    public void PanicQuietBlocksResponseActionsAndQuestCredit() => Run((engine, s, played) =>
    {
        var calls = 0;
        engine.QuestCredit = () => calls++;
        engine.Haptic = (_, _) => calls++;
        engine.AddXp = _ => calls++;
        engine.ChasterBook = _ => { calls++; return 0; };
        s.KeywordTriggers[0].Actions.AddRange(new KeywordAction[] { new HapticAction(),
            new AddXpAction { Amount = 5 }, new ChasterAddTimeAction() });
        ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
        try
        {
            Type(engine, "sit");
            Assert.Equal(0, calls);
            Assert.Empty(played);
            Assert.Single(engine.GetRecentFires()); // WPF records the match before dispatch is held.
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    });
    [Fact]
    public void HistoryIsBoundedAndKeepsTheActionSnapshotAfterEditing() => Run((engine, s, _) =>
    {
        s.AwarenessLoopProtectionEnabled = false;
        var now = engine.Now();
        engine.Now = () => now;
        for (var i = 0; i < 22; i++)
        {
            s.KeywordTriggers[0].Id = i.ToString();
            Type(engine, "sit");
            now = now.AddSeconds(2);
        }
        s.KeywordTriggers[0].Actions.Clear();
        var fires = engine.GetRecentFires();
        Assert.Equal(20, fires.Count);
        Assert.Equal("21", fires[0].TriggerId);
        Assert.Equal("2", fires[19].TriggerId);
        Assert.Equal(new[] { "PlayAudio" }, fires[0].ActionKeys);
        var view = new AwarenessTabView { PulseSource = engine };
        view.RefreshAwarenessPulseFeed();
        Assert.Equal(5, view.FindControl<StackPanel>("AwarenessPulseFeed")!.Children.Count);
    });
    [Fact]
    public void PulseFeedUpdatesOnFireAndDetachesWithTheView() => Run((engine, _, _) =>
    {
        var view = new AwarenessTabView { PulseSource = engine };
        var window = new Window { Content = view };
        try
        {
            window.Show();
            Type(engine, "sit");
            Dispatcher.UIThread.RunJobs();
            Assert.IsType<Border>(Assert.Single(view.FindControl<StackPanel>("AwarenessPulseFeed")!.Children));
            window.Content = null;
            var subscribers = typeof(KeywordAudioRuntime).GetField("TriggerFired",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
            Assert.Null(subscribers.GetValue(engine));
        }
        finally { window.Close(); }
    });
    [Fact]
    public void PulseFeedUsesTheSelectedLanguage() => Run((engine, _, _) =>
    {
        var loc = ConditioningControlPanel.Localization.LocalizationManager.Instance;
        var language = loc.CurrentLanguage;
        var view = new AwarenessTabView { PulseSource = engine };
        var window = new Window { Content = view };
        try
        {
            window.Show(); Type(engine, "sit");
            loc.SetLanguage("de"); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Auslösungen heute: 1", view.FindControl<TextBlock>("TxtAwarenessFireCount")!.Text);
            Assert.Contains(view.GetLogicalDescendants().OfType<TextBlock>(), t => t.Text == "TASTATUR");
            Assert.Equal("vor 10 s", AwarenessTabView.FormatTimeAgo(DateTime.Today, DateTime.Today.AddSeconds(10)));
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("deeper_friendly_action_play_audio"),
                AwarenessTabView.ActionChip("PlayAudio")!.Value.Tip);
        }
        finally { window.Close(); loc.SetLanguage(language); }
    });
    [Fact]
    public void EveryPulseKeyExistsInEveryLanguage() => AuditPulseKeys();
    private static void AuditPulseKeys([System.Runtime.CompilerServices.CallerFilePath] string here = "")
    {
        var root = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "../.."));
        var source = File.ReadAllText(Path.Combine(root, "CCP.Avalonia/Views/Tabs/AwarenessTabView.Pulse.cs"));
        var keys = System.Text.RegularExpressions.Regex.Matches(source, "Loc.GetF?\\(\"([^\"]+)\"")
            .Select(m => m.Groups[1].Value).Distinct().ToArray();
        Assert.NotEmpty(keys);
        foreach (var file in Directory.GetFiles(Path.Combine(root, "CCP.Core/Localization/Languages"), "*.json"))
        {
            var dictionary = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(file));
            foreach (var key in keys) Assert.False(string.IsNullOrWhiteSpace((string?)dictionary[key]), $"{file}: {key}");
        }
    }
    [Fact]
    public void PulseTimesAndActionChipsFollowWpf() => Run((_, _, _) =>
    {
        var now = new DateTime(2026, 10, 31, 12, 0, 0);
        Assert.Equal("just now", AwarenessTabView.FormatTimeAgo(now.AddSeconds(-9), now));
        Assert.Equal("10s ago", AwarenessTabView.FormatTimeAgo(now.AddSeconds(-10), now));
        Assert.Equal("1m ago", AwarenessTabView.FormatTimeAgo(now.AddMinutes(-1), now));
        Assert.Equal("1h ago", AwarenessTabView.FormatTimeAgo(now.AddHours(-1), now));
        Assert.Equal("12:00", AwarenessTabView.FormatTimeAgo(now.AddDays(-1), now));
        foreach (var key in new[] { "Highlight", "Haptic", "AvatarComment", "ExtendSession:2", "ChasterAddTime:2",
            "VisualEffect:SubliminalFlash", "VisualEffect:ExactSubliminal", "VisualEffect:ImageFlash",
            "VisualEffect:OverlayPulse", "VisualEffect:MindWipe", "VisualEffect:Bubbles" })
            Assert.NotNull(AwarenessTabView.ActionChip(key));
        Assert.Null(AwarenessTabView.ActionChip("unknown"));
    });
    [Fact]
    public void SavedOnSwitchArmsTheRuntimeWhenTheShellStarts() => Run((_, s, _) =>
    {
        KeywordTriggerHead.Stop();
        Assert.False(KeywordTriggerHead.Engine.IsActive);
        _ = new MainShellWindow();   // WPF MainWindow.xaml.cs:341 starts the service at boot
        Assert.True(KeywordTriggerHead.Engine.IsActive);
    });

    /// <summary>WPF b9368d963 KeywordTriggerService.DispatchMergedAsync: a keyword never plays inside a panic's quiet.</summary>
    [Fact]
    public void PanicQuietHoldsAMatchedKeyword() => Run((engine, s, played) =>
    {
        s.KeywordGlobalCooldownSeconds = 30;
        try
        {
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Type(engine, "sit"); Assert.Empty(played);
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
        Type(engine, "sit"); Assert.Empty(played);   // WPF records the held match's cooldown (KeywordTriggerService :1194-1200)
        engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 31);   // past the 30 s global cooldown
        Type(engine, "sit"); Assert.Single(played);
    });

    [Fact] // WPF KeywordTriggerService.cs:176-188
    public void StartRequiresAccessAndAcceptsTheAwarenessFreeDay() => Run((engine, _, _) =>
    {
        engine.Stop();
        CoreEntitlement.HasPremiumProvider = () => false;
        engine.Start();
        Assert.False(engine.IsActive);
        CoreEntitlement.IsFreeTodayProvider = key => key == "awareness";
        engine.Start();
        Assert.True(engine.IsActive);
    });

    [Fact] // WPF KeywordTriggerService.cs:178
    public void RepeatedStartPreservesPartialInput() => Run((engine, _, played) =>
    {
        Type(engine, "si");
        engine.Start();
        Type(engine, "t");
        Assert.Single(played);
    });

    [Theory] // WPF KeywordTriggerService.cs:1841-1855
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    [InlineData(true, true, 1)]
    public void AudioRequiresAnEngineOrSession(bool engineOn, bool sessionOn, int count) => Run((engine, _, played) =>
    {
        CoreSession.IsEngineRunningProvider = () => engineOn;
        CoreSession.IsSessionRunningProvider = () => sessionOn;
        Type(engine, "sit");
        Assert.Equal(count, played.Count);
    });

    [Fact] // Recheck WPF's engine-off audio hold before each pending repeat.
    public void EngineStoppingDuringRepeatGapCutsTheClipAndHoldsTheRepeat() => Run((engine, s, played) =>
    {
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.PlayCount = 2; audio.DelayBetweenMs = 50;
        var gap = new TaskCompletionSource();
        engine.Delay = (_, _) => gap.Task;
        Type(engine, "sit");
        Assert.Single(played);
        CoreSession.IsEngineRunningProvider = () => false;
        gap.SetResult();
        Dispatcher.UIThread.RunJobs();
        Assert.Single(played.Where(x => x.Path != "stopped"));
        Assert.Equal("stopped", played.Last().Path);
    });

    [Fact] // WPF MainWindow.Patreon.cs:187-189,452-454
    public void EntitlementRefreshStopsAudioAndRearmsSavedMasterWhenAccessResolves() => Run((_, s, played) =>
    {
        s.KeywordTriggers[0].Keyword = "access-resolution-word";
        var shell = new MainShellWindow();
        try
        {
            Type(KeywordTriggerHead.Engine, "access-resolution-word");
            Assert.Single(played);
            CoreEntitlement.HasPremiumProvider = () => false;
            shell.RefreshEntitlementVeils(persist: true);
            Assert.False(s.KeywordTriggersEnabled);
            Assert.False(KeywordTriggerHead.Engine.IsActive);
            Assert.Equal("stopped", played.Last().Path);
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            shell.RefreshEntitlementVeils();
            Assert.True(KeywordTriggerHead.Engine.IsActive);
        }
        finally { shell.Close(); }
    });

    [Fact] // WPF MainWindow.Patreon.cs:452-454
    public void AccessResolutionRearmsASavedMaster() => Run((_, s, _) =>
    {
        KeywordTriggerHead.Stop();
        CoreEntitlement.HasPremiumProvider = () => false;
        var shell = new MainShellWindow();
        try
        {
            KeywordTriggerHead.Stop(); // isolate late resolution from the startup path
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            shell.RefreshEntitlementVeils();
            Assert.True(KeywordTriggerHead.Engine.IsActive);
        }
        finally { shell.Close(); }
    });

    [Fact]   // WPF DispatchMergedAsync :1322 + FireVisualEffect: visuals run in action order, once per variant, enabled only
    public void VisualActionsRunInOrderMergeByVariantAndSkipDisabled() => Run((engine, s, played) =>
    {
        var calls = new List<string>();
        var (oldFlash, oldSub, pool) = (CoreFlash.ShowProvider, CoreSubliminal.ShowProvider, s.SubliminalPool);
        try
        {
            CoreFlash.ShowProvider = () => calls.Add("flash");
            CoreSubliminal.ShowProvider = text => calls.Add("sub:" + text);
            s.SubliminalPool = new() { ["pool phrase"] = true };
            engine.BubbleEffect = () => calls.Add("bubble");
            engine.Haptic = (_, _) => calls.Add("haptic");
            s.KeywordTriggers[0].Actions.AddRange(new KeywordAction[] {
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash },
                new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal, Enabled = false },
                new HapticAction { Intensity = 0.5 },
                new VisualEffectAction { Effect = KeywordVisualEffect.SubliminalFlash } });
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "two", Keyword = "sit", CooldownSeconds = 0, Actions = new() {
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash },
                new VisualEffectAction { Effect = KeywordVisualEffect.Bubbles } } });
            Type(engine, "sit");
            Assert.Single(played);
            Assert.Equal(new[] { "flash", "haptic", "sub:pool phrase", "bubble" }, calls);
        }
        finally { (CoreFlash.ShowProvider, CoreSubliminal.ShowProvider, s.SubliminalPool) = (oldFlash, oldSub, pool); }
    });

    [Fact]   // WPF FireVisualEffect ExactSubliminal: FlashSubliminalCustom(keyword upper) + ForceMuteKeyword(3000)
    public void ExactSubliminalEchoesTheKeywordAndMutesItForThreeSeconds() => Run((engine, s, _) =>
    {
        var shown = new List<string>();
        var old = CoreSubliminal.CustomShowProvider;
        try
        {
            CoreSubliminal.CustomShowProvider = shown.Add;
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal } };
            var now = engine.Now();
            Type(engine, "sit");
            Assert.Equal(new[] { "SIT" }, shown);
            engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
            engine.Now = () => now.AddMilliseconds(2999);
            Type(engine, "sit"); Assert.Single(shown);
            engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
            engine.Now = () => now.AddMilliseconds(3000);
            Type(engine, "sit"); Assert.Equal(2, shown.Count);
        }
        finally { CoreSubliminal.CustomShowProvider = old; }
    });

    [Fact]   // WPF DispatchLegacyFlatFieldsAsync: the flat visual follows the audio; the panic quiet holds both
    public void LegacyFlatVisualFollowsTheAudioAndPanicQuietHoldsIt() => Run((engine, s, played) =>
    {
        var flashes = 0;
        var old = CoreFlash.ShowProvider;
        try
        {
            CoreFlash.ShowProvider = () => { Assert.Single(played); flashes++; };
            var file = ((PlayAudioAction)s.KeywordTriggers[0].Actions[0]).FilePath;
            s.KeywordTriggers[0] = new KeywordTrigger { Keyword = "sit", CooldownSeconds = 0, Actions = new List<KeywordAction>(),
                AudioFilePath = file, DuckAudio = false, VisualEffect = KeywordVisualEffect.ImageFlash };
            Type(engine, "sit");
            Assert.Equal(1, flashes);
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            engine.Now = () => new DateTime(2026, 10, 14, 13, 0, 0);
            Type(engine, "sit");
            Assert.Equal(1, flashes); Assert.Single(played);
        }
        finally { CoreFlash.ShowProvider = old; ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    });

    [Fact]   // WPF FireVisualEffect MindWipe: audio, so held while idle and skipped with an empty pool; flashes still fire
    public void MindWipeVisualIsHeldWhileIdleButImageFlashStillFires() => Run((engine, s, _) =>
    {
        var (wipes, flashes, clips) = (0, 0, 2);
        var (oldFlash, oldCount, oldWipe) = (CoreFlash.ShowProvider, CoreMindWipe.ClipCountProvider, CoreMindWipe.TriggerOnceProvider);
        try
        {
            CoreFlash.ShowProvider = () => flashes++;
            CoreMindWipe.ClipCountProvider = () => clips;
            CoreMindWipe.TriggerOnceProvider = () => wipes++;
            s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.MindWipe },
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash } };
            Type(engine, "sit");
            Assert.Equal((1, 1), (wipes, flashes));
            CoreSession.IsEngineRunningProvider = () => false;
            engine.Now = () => new DateTime(2026, 10, 14, 13, 0, 0);
            Type(engine, "sit");
            Assert.Equal((1, 2), (wipes, flashes));
            CoreSession.IsEngineRunningProvider = () => true;
            clips = 0;
            engine.Now = () => new DateTime(2026, 10, 14, 14, 0, 0);
            Type(engine, "sit");
            Assert.Equal((1, 3), (wipes, flashes));
        }
        finally { (CoreFlash.ShowProvider, CoreMindWipe.ClipCountProvider, CoreMindWipe.TriggerOnceProvider) = (oldFlash, oldCount, oldWipe); }
    });
}
