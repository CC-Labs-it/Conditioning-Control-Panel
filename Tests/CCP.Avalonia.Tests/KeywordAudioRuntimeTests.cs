using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

// Reads the process-wide 5 min panic quiet that MandatoryVideoOverlayTests and PanicAftermathHeadTests arm.
[Collection(RunsAloneCollection.Name)]
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
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled,
            CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider,
            CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled, CoreFlash.ShowProvider);
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
            CoreSession.IsEngineRunningProvider = () => true;
            CoreSession.IsSessionRunningProvider = () => false;
            (CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider) = (_ => { }, _ => { }, () => 41);
            CoreAudio.PlayStoppableProvider = (path, volume, _, started, _) => { played.Add((path, volume)); started?.Invoke(TimeSpan.FromSeconds(1)); return () => played.Add(("stopped", 0)); };
            engine.Now = () => new DateTime(2026, 10, 14, 12, 0, 0);
            engine.Clock = () => 0;
            engine.Start();
            await body(engine, s, played);
        }
        finally
        {
            engine.Stop(); KeywordTriggerHead.Stop(); File.Delete(file);
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled,
                CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider,
                CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider, s.AudioDuckingEnabled, CoreFlash.ShowProvider) = saved;
        }
    });

    [Fact]
    public void KeywordBubbleFiresOnceAndPanicQuietSuppressesIt() => Run((engine, s, _) =>
    {
        var count = 0;
        engine.BubbleEffect = () => count++;
        s.AwarenessLoopProtectionEnabled = false;
        s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.Bubbles } };
        try
        {
            Type(engine, "sit"); Assert.Equal(1, count);
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            var now = engine.Now(); engine.Now = () => now.AddMinutes(10);
            Type(engine, "sit"); Assert.Equal(1, count);
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    });

    [Fact]
    public void TriggerBubbleCapFollowsTheLatchedAmbientHost() => Run((_, s, _) =>
    {
        var oldPrepare = BubbleOverlay.PrepareWindowOverride;
        var oldShared = s.BubbleSharedHost;
        var host = new Window();
        try
        {
            BubbleOverlay.Stop();
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.ResetForTests();
            BubbleOverlay.PrepareWindowOverride = (_, _) => true;
            s.BubbleSharedHost = true;
            host.Show();
            for (var i = 0; i < 5; i++) BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count); // idle, even with the setting on
            BubbleOverlay.BeginChaos(host, _ => { });
            BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count); // Chaos windows are not the ambient host
            BubbleOverlay.Start(host);
            s.BubbleSharedHost = false; // WPF latches the host until Stop
            for (var i = 0; i < 45; i++) BubbleOverlay.SpawnOnce(host);
            Assert.Equal(40, BubbleOverlay.Field.Bubbles.Count);
            BubbleOverlay.Stop(); // Chaos stays open, but the ambient host is gone
            s.BubbleSharedHost = true;
            for (var i = 0; i < 5; i++) BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count);
            BubbleOverlay.EndChaos();
            BubbleOverlay.Stop();
            s.BubbleSharedHost = false;
            BubbleOverlay.Start(host);
            s.BubbleSharedHost = true;
            for (var i = 0; i < 5; i++) BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count); // enabling mid-run does not start a host
        }
        finally
        {
            BubbleOverlay.EndChaos(); BubbleOverlay.Stop(); host.Close();
            BubbleOverlay.PrepareWindowOverride = oldPrepare;
            s.BubbleSharedHost = oldShared;
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.ResetForTests();
        }
    });

    [Fact]
    public void TriggerBubbleHonoursCapDisplayChangeAndIdleLifetime() => Run((_, s, _) =>
    {
        var oldPrepare = BubbleOverlay.PrepareWindowOverride;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var oldHost = s.BubbleSharedHost;
        var oldClickable = s.BubblesClickable;
        var host = new Window();
        var opened = 0;
        try
        {
            BubbleOverlay.Stop();
            BubbleOverlay.PrepareWindowOverride = (_, _) => { opened++; return true; };
            CoreSession.IsSessionRunningProvider = () => false;
            s.BubbleSharedHost = false; s.BubblesClickable = false;
            host.Show();
            BubbleOverlay.SpawnOnce(host);
            Assert.False(BubbleOverlay.SpawnTimerRunning);
            Assert.Single(BubbleOverlay.Field.Bubbles);
            Assert.True(BubbleOverlay.Field.Bubbles[0].Clickable);
            Assert.False(BubbleOverlay.IsRunning);
            BubbleOverlay.BeginChaos(host, _ => { });
            BubbleOverlay.EndChaos();
            Assert.Single(BubbleOverlay.Field.Bubbles);
            BubbleOverlay.SpawnOnce(host); BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count);
            BubbleOverlay.SpawnOnce(host);
            Assert.Equal(3, BubbleOverlay.Field.Bubbles.Count);
            BubbleOverlay.Field.Bubbles.Clear();
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.NotifyDisplayChange("test");
            BubbleOverlay.SpawnOnce(host);
            Assert.Empty(BubbleOverlay.Field.Bubbles);
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.ResetForTests();
            BubbleOverlay.OnFrame(TimeSpan.FromSeconds(1));
            Assert.False(BubbleOverlay.IsRunning);
            Assert.False(BubbleOverlay.SpawnTimerRunning);
            BubbleOverlay.SpawnOnce(host);
            Assert.Equal(2, opened);
        }
        finally
        {
            ConditioningControlPanel.Services.UI.DisplayChangeCoordinator.ResetForTests();
            BubbleOverlay.Stop(); host.Close();
            BubbleOverlay.PrepareWindowOverride = oldPrepare;
            CoreSession.IsSessionRunningProvider = oldSession;
            s.BubbleSharedHost = oldHost; s.BubblesClickable = oldClickable;
        }
    });

    [Fact]
    public void VisualChoicesReachExistingRenderersAndExactEchoWaitsThreeSeconds() => Run((engine, s, _) =>
    {
        var shown = new List<string>();
        var old = SubliminalWhisperShow.Draw;
        var oldXp = CoreProgression.AddXPProvider;
        var xp = new List<double>();
        try
        {
            SubliminalWhisperShow.Draw = shown.Add;
            CoreProgression.AddXPProvider = (amount, source) => { Assert.Equal("Subliminal", source); xp.Add(amount); };
            s.AwarenessLoopProtectionEnabled = false;
            // The real drawer's effect picker rebuilds the actions consumed by keyboard dispatch.
            var panel = new ConditioningControlPanel.Avalonia.Views.Controls.Companion.KeywordTriggersPanel();
            panel.GetLogicalDescendants().OfType<ComboBox>().Single(c => c.Items.Count == 8).SelectedIndex = 3;
            Assert.Contains(s.KeywordTriggers[0].Actions, a => a is VisualEffectAction { Effect: KeywordVisualEffect.ExactSubliminal });
            Type(engine, "sit");
            Assert.Equal(new[] { "SIT" }, shown);
            engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
            Type(engine, "sit"); Assert.Single(shown);
            engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
            var now = engine.Now(); engine.Now = () => now.AddMilliseconds(2999);
            Type(engine, "sit"); Assert.Single(shown);
            engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
            engine.Now = () => now.AddMilliseconds(3000);
            Type(engine, "sit"); Assert.Equal(2, shown.Count);
            Assert.Equal(new double[] { 10, 10 }, xp);
        }
        finally { SubliminalWhisperShow.Draw = old; CoreProgression.AddXPProvider = oldXp; }
    });

    [Theory]
    [InlineData("<b>sit</b>", "SIT")]
    [InlineData("  sit  ", "SIT")]
    [InlineData("xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx|sit", "XXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX")]
    public void ExactCustomTextUsesWpfSanitizing(string keyword, string expected) => Run((engine, s, _) =>
    {
        var shown = new List<string>(); var old = SubliminalWhisperShow.Draw;
        try
        {
            SubliminalWhisperShow.Draw = shown.Add;
            s.KeywordTriggers[0].Keyword = keyword;
            s.KeywordTriggers[0].MatchType = KeywordMatchType.Regex;
            s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal } };
            Type(engine, keyword);
            Assert.Equal(expected, Assert.Single(shown));
        }
        finally { SubliminalWhisperShow.Draw = old; }
    });

    [Fact]
    public void VisualMergeSkipsDisabledAndDeduplicatesVariantsWithPresetKeyword() => Run((engine, s, _) =>
    {
        var shown = new List<string>(); var flashes = 0;
        var old = CoreSubliminal.ShowProvider; var oldDraw = SubliminalWhisperShow.Draw; var oldFlash = CoreFlash.ShowProvider;
        var pool = s.SubliminalPool;
        try
        {
            CoreSubliminal.ShowProvider = shown.Add; SubliminalWhisperShow.Draw = shown.Add; CoreFlash.ShowProvider = () => flashes++;
            s.SubliminalPool = new() { ["pool phrase"] = true };
            s.KeywordTriggers[0].Actions = new() {
                new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal, Enabled = false },
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash },
                new VisualEffectAction { Effect = KeywordVisualEffect.SubliminalFlash } };
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:visual", Keyword = "SIT", CooldownSeconds = 0,
                Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash },
                    new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal } } });
            Type(engine, "sit");
            Assert.Equal(1, flashes);
            Assert.Equal(new[] { "SIT", "pool phrase" }, shown);
        }
        finally { CoreSubliminal.ShowProvider = old; SubliminalWhisperShow.Draw = oldDraw; CoreFlash.ShowProvider = oldFlash; s.SubliminalPool = pool; }
    });

    [Fact]
    public void DisabledVisualActionDoesNotReachTheRenderer() => Run((engine, s, _) =>
    {
        var flashes = 0; var old = CoreFlash.ShowProvider;
        try
        {
            CoreFlash.ShowProvider = () => flashes++;
            s.KeywordTriggers[0].Actions = new() {
                new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash, Enabled = false } };
            Type(engine, "sit"); Assert.Equal(0, flashes);
        }
        finally { CoreFlash.ShowProvider = old; }
    });

    [Fact]
    public void LegacyVisualFallbackAndPanicQuietApplyToAudioAndVisuals() => Run((engine, s, played) =>
    {
        var flashes = 0; var old = CoreFlash.ShowProvider;
        try
        {
            CoreFlash.ShowProvider = () => { Assert.Single(played); flashes++; };
            s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash });
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Type(engine, "sit"); Assert.Empty(played); Assert.Equal(0, flashes);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
            s.AwarenessLoopProtectionEnabled = false;
            s.KeywordTriggers[0].AudioFilePath = ((PlayAudioAction)s.KeywordTriggers[0].Actions[0]).FilePath;
            s.KeywordTriggers[0].Actions.Clear();
            s.KeywordTriggers[0].VisualEffect = KeywordVisualEffect.ImageFlash;
            var now = engine.Now(); engine.Now = () => now.AddMinutes(10);
            Type(engine, "sit"); Assert.Equal(1, flashes); Assert.Single(played);
        }
        finally { CoreFlash.ShowProvider = old; ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void KeywordHapticAndXpUseConfiguredValuesAndSessionMultiplier(bool legacy) => Run((engine, s, _) =>
    {
        var old = (CoreHaptics.Service, CoreProgression.AddXPProvider, s.KeywordSessionMultiplier, s.AwarenessLoopProtectionEnabled);
        var hs = new HapticSettings { Enabled = true };
        hs.EnsureV2Migrated();
        hs.V2.Rule(ConditioningControlPanel.Services.Haptics.Core.HapticEventKind.KeywordTrigger).Enabled = true;
        hs.V2.Rule(ConditioningControlPanel.Services.Haptics.Core.HapticEventKind.KeywordTrigger).Intensity = 1;
        using var haptics = new ConditioningControlPanel.Services.HapticService(hs);
        var pulses = new List<string>(); var xp = new List<(double, string)>();
        haptics.HapticTriggered += (_, message) => { if (message.StartsWith("KeywordTrigger:")) pulses.Add(message); };
        try
        {
            CoreHaptics.Service = haptics;
            CoreProgression.AddXPProvider = (amount, source) => xp.Add((amount, source));
            s.KeywordSessionMultiplier = 2; s.AwarenessLoopProtectionEnabled = false;
            var t = s.KeywordTriggers[0];
            t.Actions = legacy ? new() : new() { new HapticAction { Intensity = .4 }, new AddXpAction { Amount = 7 },
                new HapticAction { Intensity = .8, Enabled = false }, new AddXpAction { Amount = 99, Enabled = false } };
            t.VisualEffect = KeywordVisualEffect.None; t.HapticEnabled = true; t.HapticIntensity = .4; t.XPAward = 7; t.DuckAudio = false;
            Type(engine, "sit");
            Assert.Equal(new[] { "KeywordTrigger: 40%" }, pulses);
            Assert.Equal(new[] { (7d, "KeywordTrigger") }, xp);
            CoreSession.IsSessionRunningProvider = () => true;
            var now = engine.Now(); engine.Now = () => now.AddMinutes(1);
            Type(engine, "sit");
            Assert.Equal(2, pulses.Count);
            Assert.Equal((14d, "KeywordTrigger"), xp.Last());
            if (legacy)
            {
                t.VisualEffect = KeywordVisualEffect.HighlightOnly;
                engine.Now = () => now.AddMinutes(2);
                Type(engine, "sit");
                Assert.Equal(2, pulses.Count); Assert.Equal(2, xp.Count);
            }
        }
        finally { (CoreHaptics.Service, CoreProgression.AddXPProvider, s.KeywordSessionMultiplier, s.AwarenessLoopProtectionEnabled) = old; }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MindWipeRequiresAClipAndRunningEngineOrSession(bool legacy) => Run((engine, s, _) =>
    {
        var old = (CoreMindWipe.ClipCountProvider, CoreMindWipe.TriggerOnceProvider, s.AwarenessLoopProtectionEnabled);
        var count = 0; var plays = 0; var engineRunning = false; var session = false;
        try
        {
            CoreMindWipe.ClipCountProvider = () => count;
            CoreMindWipe.TriggerOnceProvider = () => plays++;
            CoreSession.IsEngineRunningProvider = () => engineRunning;
            CoreSession.IsSessionRunningProvider = () => session;
            s.AwarenessLoopProtectionEnabled = false;
            var t = s.KeywordTriggers[0]; t.DuckAudio = false; t.HapticEnabled = false; t.XPAward = 0;
            t.VisualEffect = KeywordVisualEffect.MindWipe;
            t.Actions = legacy ? new() : new() { new VisualEffectAction { Effect = KeywordVisualEffect.MindWipe } };
            var now = engine.Now();
            void Fire() { engine.Now = () => now; Type(engine, "sit"); now = now.AddMinutes(1); }
            Fire(); Assert.Equal(0, plays);
            count = 1; Fire(); Assert.Equal(0, plays);
            engineRunning = true; count = 0; Fire(); Assert.Equal(0, plays);
            count = 1; Fire(); Assert.Equal(1, plays);
            engineRunning = false; session = true; Fire(); Assert.Equal(2, plays);
        }
        finally { (CoreMindWipe.ClipCountProvider, CoreMindWipe.TriggerOnceProvider, s.AwarenessLoopProtectionEnabled) = old; }
    });

    [Fact]
    public Task ActionOrderWaitsForRepeatGapsAndKeepsDuplicatesForSingleTrigger() => RunAsync(async (engine, s, played) =>
    {
        var old = (CoreFlash.ShowProvider, CoreProgression.AddXPProvider);
        var events = new List<string>(); var gap = new TaskCompletionSource();
        try
        {
            CoreFlash.ShowProvider = () => events.Add("flash");
            CoreProgression.AddXPProvider = (amount, _) => events.Add($"xp:{amount}");
            var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
            audio.PlayCount = 2; audio.DelayBetweenMs = 250;
            engine.Delay = (ms, _) => { Assert.Equal(250, ms); return gap.Task; };
            s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }, audio,
                new AddXpAction { Amount = 3 }, new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }, new AddXpAction { Amount = 4 } };
            Type(engine, "sit");
            Assert.Equal(new[] { "flash" }, events); Assert.Single(played);
            gap.SetResult(); await Task.Yield();
            Assert.Equal(2, played.Count(p => p.Path != "stopped"));
            Assert.Equal(new[] { "flash", "xp:3", "flash", "xp:4" }, events);
        }
        finally { (CoreFlash.ShowProvider, CoreProgression.AddXPProvider) = old; }
    });

    [Fact]
    public void MergedActionsKeepFirstOfEachTypeInOrder() => Run((engine, s, _) =>
    {
        var old = (CoreFlash.ShowProvider, CoreProgression.AddXPProvider);
        var events = new List<string>();
        try
        {
            CoreFlash.ShowProvider = () => events.Add("flash");
            CoreProgression.AddXPProvider = (amount, _) => events.Add($"xp:{amount}");
            s.KeywordTriggers[0].Actions = new() { new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash }, new AddXpAction { Amount = 8 } };
            s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:first", Keyword = "sit", Actions = new() {
                new AddXpAction { Amount = 2 }, new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash } } });
            Type(engine, "sit");
            Assert.Equal(new[] { "xp:2", "flash" }, events);
        }
        finally { (CoreFlash.ShowProvider, CoreProgression.AddXPProvider) = old; }
    });

    [Fact]
    public Task OverlappingKeywordAudioKeepsTheFirstFireResponses() => RunAsync(async (engine, s, played) =>
    {
        var old = (CoreHaptics.Service, CoreFlash.ShowProvider, CoreProgression.AddXPProvider, s.AwarenessLoopProtectionEnabled);
        var settings = new HapticSettings { Enabled = true };
        settings.EnsureV2Migrated();
        var rule = settings.V2.Rule(ConditioningControlPanel.Services.Haptics.Core.HapticEventKind.KeywordTrigger);
        rule.Enabled = true; rule.Intensity = 1;
        using var haptics = new ConditioningControlPanel.Services.HapticService(settings);
        var events = new List<string>(); var gap = new TaskCompletionSource();
        haptics.HapticTriggered += (_, message) => { if (message.StartsWith("KeywordTrigger:")) events.Add("haptic"); };
        try
        {
            CoreHaptics.Service = haptics;
            CoreFlash.ShowProvider = () => events.Add("flash");
            CoreProgression.AddXPProvider = (amount, _) => events.Add($"xp:{amount}");
            s.AwarenessLoopProtectionEnabled = false;
            var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
            audio.PlayCount = 2; audio.DelayBetweenMs = 5000;
            s.KeywordTriggers[0].Actions.AddRange(new KeywordAction[] { new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash },
                new HapticAction { Intensity = .4 }, new AddXpAction { Amount = 7 } });
            s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "stay", Actions = new() {
                new PlayAudioAction { FilePath = audio.FilePath, DuckSystemAudio = false } } });
            engine.Delay = (_, _) => gap.Task;
            var now = engine.Now();
            Type(engine, "sit");
            engine.Now = () => now.AddSeconds(2);
            Type(engine, "stay");
            Assert.Equal(2, played.Count(p => p.Path != "stopped")); Assert.Empty(events);
            gap.SetResult(); await Task.Yield();
            Assert.Equal(3, played.Count(p => p.Path != "stopped"));
            Assert.Equal(new[] { "flash", "haptic", "xp:7" }, events);
        }
        finally { (CoreHaptics.Service, CoreFlash.ShowProvider, CoreProgression.AddXPProvider, s.AwarenessLoopProtectionEnabled) = old; }
    });

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public Task AudioActionsDuckOnceForTheWholeList(bool firstDucks, bool secondDucks) => RunAsync(async (engine, s, _) =>
    {
        var events = new List<string>(); var delays = new List<int>(); var release = new TaskCompletionSource(); var play = 0;
        s.AudioDuckingEnabled = true;
        CoreAudio.DuckProvider = _ => events.Add("duck");
        CoreAudio.DuckGenerationProvider = () => 31;
        CoreAudio.UnduckProvider = gen => events.Add($"unduck:{gen}");
        CoreAudio.PlayStoppableProvider = (_, _, _, started, finished) =>
        {
            var n = ++play; events.Add($"play:{n}");
            started?.Invoke(TimeSpan.FromMilliseconds(n == 1 ? 2000 : 400));
            return () => { events.Add($"stop:{n}"); finished?.Invoke(); };
        };
        engine.Delay = (ms, _) => { delays.Add(ms); return release.Task; };
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.DuckSystemAudio = firstDucks;
        s.KeywordTriggers[0].Actions.Add(new PlayAudioAction { FilePath = audio.FilePath, DuckSystemAudio = secondDucks });
        Type(engine, "sit");
        Assert.Equal(new[] { "duck", "play:1", "stop:1", "play:2" }, events);
        Assert.Equal(new[] { 2500 }, delays);
        release.SetResult(); await Task.Yield();
        Assert.Equal("unduck:31", events.Last());
        Assert.Single(events.Where(e => e.StartsWith("unduck:")));
    });

    [Fact]
    public Task ReplacedClipKeepsItsParsedDurationWithoutBlockingTheNextFire() => RunAsync(async (engine, s, _) =>
    {
        var release = new TaskCompletionSource(); var full = new TaskCompletionSource(); var delays = new List<int>();
        var firstProbe = new TaskCompletionSource<double?>(); var probes = 0; var unducked = new TaskCompletionSource(); var play = 0;
        s.AudioDuckingEnabled = true; s.AwarenessLoopProtectionEnabled = false;
        CoreAudio.UnduckProvider = _ => unducked.TrySetResult();
        engine.DurationProbe = (_, _) => ++probes == 1 ? firstProbe.Task : Task.FromResult<double?>(.4);
        CoreAudio.PlayStoppableProvider = (_, _, _, started, finished) =>
        {
            // The first clip reports no length before the second fire replaces it.
            started?.Invoke(++play == 1 ? TimeSpan.Zero : TimeSpan.FromMilliseconds(400));
            return () => finished?.Invoke();
        };
        engine.Delay = (ms, _) => { lock (delays) delays.Add(ms); if (ms == 10500) full.TrySetResult(); return release.Task; };
        ((PlayAudioAction)s.KeywordTriggers[0].Actions[0]).DuckSystemAudio = true;
        Type(engine, "sit"); var now = engine.Now(); engine.Now = () => now.AddSeconds(2); Type(engine, "sit");
        Assert.Equal(2, play);
        firstProbe.SetResult(10);
        await full.Task.WaitAsync(TimeSpan.FromSeconds(5));
        lock (delays) Assert.Equal(new[] { 900, 10500 }, delays.OrderBy(d => d));
        Assert.False(unducked.Task.IsCompleted);
        release.SetResult(); await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    });

    [Theory, InlineData(3000, 7500), InlineData(12000, -1)]   // WPF :1383-1387 times the release from dispatch end.
    public Task SlowLengthParseCountsAgainstTheDuckRelease(long parseMs, int expected) => RunAsync(async (engine, s, _) =>
    {
        var probe = new TaskCompletionSource<double?>(); var delays = new List<int>(); var unducked = new TaskCompletionSource(); long clock = 0;
        s.AudioDuckingEnabled = true; ((PlayAudioAction)s.KeywordTriggers[0].Actions[0]).DuckSystemAudio = true;
        (engine.Clock, engine.DurationProbe) = (() => clock, (_, _) => probe.Task);
        engine.Delay = (ms, _) => { delays.Add(ms); return Task.CompletedTask; };
        (CoreAudio.UnduckProvider, CoreAudio.PlayStoppableProvider) = (_ => unducked.TrySetResult(), (_, _, _, _, f) => () => f?.Invoke());
        Type(engine, "sit"); clock = parseMs; probe.SetResult(10);
        await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5)); Assert.Equal(expected < 0 ? Array.Empty<int>() : new[] { expected }, delays);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task ParsedDurationSurvivesReplacementBeforePlaybackStarts(bool replace) => RunAsync(async (engine, s, _) =>
    {
        var firstProbe = new TaskCompletionSource<double?>(); var delay = new TaskCompletionSource<int>();
        var release = new TaskCompletionSource(); var unducked = new TaskCompletionSource(); var probes = 0; var plays = 0;
        s.AudioDuckingEnabled = true;
        engine.DurationProbe = (_, _) => ++probes == 1 ? firstProbe.Task : Task.FromResult<double?>(.4);
        CoreAudio.UnduckProvider = _ => unducked.TrySetResult();
        CoreAudio.PlayStoppableProvider = (_, _, _, started, finished) =>
        {
            if (++plays == 2) started?.Invoke(TimeSpan.FromMilliseconds(400));
            return () => finished?.Invoke();
        };
        engine.Delay = (ms, _) => { delay.TrySetResult(ms); return release.Task; };
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.DuckSystemAudio = true;
        if (replace) s.KeywordTriggers[0].Actions.Add(new PlayAudioAction { FilePath = audio.FilePath, DuckSystemAudio = false });
        Type(engine, "sit"); Assert.Equal(replace ? 2 : 1, plays);
        firstProbe.SetResult(10);
        Assert.Equal(10500, await delay.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(replace ? 2 : 1, plays); Assert.False(unducked.Task.IsCompleted);
        release.SetResult(); await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5));
    });

    [Fact]
    public Task PendingDurationNeitherDelaysNorReordersOverlappingFires() => RunAsync(async (engine, s, played) =>
    {
        var pending = new TaskCompletionSource<double?>(); var other = Path.GetTempFileName();
        try
        {
            s.AudioDuckingEnabled = true; s.AwarenessLoopProtectionEnabled = false;
            var sit = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
            sit.DuckSystemAudio = true;
            s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "stay", Actions = new() { new PlayAudioAction { FilePath = other } } });
            engine.DurationProbe = (_, _) => pending.Task;
            var now = engine.Now(); Type(engine, "sit");
            Assert.Equal(new[] { sit.FilePath }, played.Select(p => p.Path));
            engine.Now = () => now.AddSeconds(2); Type(engine, "stay");
            Assert.Equal(new[] { sit.FilePath, "stopped", other }, played.Select(p => p.Path));
            pending.SetResult(10); await Task.Yield();
            Assert.Equal(3, played.Count);
        }
        finally { engine.StopAudio(); File.Delete(other); }
    });

    [Fact]
    public async Task KeywordAndVideoLengthsShareOneSavedCache()
    {
        try { _ = new LibVlcAudio("--aout=dummy"); }
        catch (Exception e) { Assert.Skip("libvlc not available: " + e.Message); }
        var lengths = MandatoryVideoOverlay.Lengths!;
        var files = Enumerable.Range(0, 3).Select(_ => Path.Combine(Path.GetTempPath(), $"ccp-kw-{Guid.NewGuid():N}.wav")).ToArray();
        try
        {
            foreach (var f in files) LayeredAudioLoopTests.WriteSilentWav(f, 300);
            var engine = new KeywordAudioRuntime();
            // Interleave the keyword path and the video overlay path, saving after each.
            Assert.NotNull(await engine.ReadDuration(files[0], default)); lengths.Save();
            for (var i = 0; i < 100 && MandatoryVideoOverlay.LengthOf(files[1]) == null; i++) await Task.Delay(50);
            lengths.Save();
            Assert.NotNull(await engine.ReadDuration(files[2], default)); lengths.Save();
            var reloaded = new ConditioningControlPanel.Services.VideoMetadataCache(LibVlcAudio.Shared!);
            Assert.All(files, f => Assert.NotNull(reloaded.TryGetDuration(f)));
        }
        finally { foreach (var f in files) File.Delete(f); }
    }

    [Fact]
    public Task StopWhileDurationIsLoadingStopsTheClip() => RunAsync(async (engine, s, played) =>
    {
        var pending = new TaskCompletionSource<double?>();
        s.AudioDuckingEnabled = true;
        ((PlayAudioAction)s.KeywordTriggers[0].Actions[0]).DuckSystemAudio = true;
        engine.DurationProbe = (_, _) => pending.Task;
        Type(engine, "sit"); Assert.Single(played);
        engine.StopAudio(); pending.SetResult(10); await Task.Yield();
        Assert.Equal(new[] { "stopped" }, played.Skip(1).Select(p => p.Path));
    });

    [Fact]
    public Task MissingClipStillDucksForHalfASecond() => RunAsync(async (engine, s, played) =>
    {
        var events = new List<string>(); var release = new TaskCompletionSource();
        s.AudioDuckingEnabled = true;
        CoreAudio.DuckProvider = _ => events.Add("duck");
        CoreAudio.DuckGenerationProvider = () => 17;
        CoreAudio.UnduckProvider = gen => events.Add($"unduck:{gen}");
        engine.Delay = (ms, _) => { Assert.Equal(500, ms); return release.Task; };
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.FilePath = null; audio.DuckSystemAudio = true;
        Type(engine, "sit");
        Assert.Empty(played); Assert.Equal(new[] { "duck" }, events);
        release.SetResult(); await Task.Yield();
        Assert.Equal(new[] { "duck", "unduck:17" }, events);
    });

    [Fact]
    public Task UnknownClipDurationStaysDuckedUntilPlaybackEnds() => RunAsync(async (engine, s, _) =>
    {
        var events = new List<string>(); var release = new TaskCompletionSource();
        var delayed = new TaskCompletionSource(); var unducked = new TaskCompletionSource(); Action? finish = null;
        s.AudioDuckingEnabled = true;
        CoreAudio.DuckProvider = _ => events.Add("duck");
        CoreAudio.UnduckProvider = _ => { events.Add("unduck"); unducked.TrySetResult(); };
        CoreAudio.PlayStoppableProvider = (_, _, _, started, finished) =>
        {
            events.Add("play"); started?.Invoke(TimeSpan.Zero); finish = finished; return () => { };
        };
        engine.Delay = (ms, _) => { Assert.Equal(500, ms); events.Add("delay"); delayed.TrySetResult(); return release.Task; };
        ((PlayAudioAction)s.KeywordTriggers[0].Actions[0]).DuckSystemAudio = true;
        Type(engine, "sit"); await Task.Yield();
        Assert.Equal(new[] { "duck", "play" }, events);
        finish!(); await delayed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "duck", "play", "delay" }, events);
        release.SetResult(); await unducked.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(new[] { "duck", "play", "delay", "unduck" }, events);
    });

    [Fact]
    public Task StopCancelsOverlappingResponsesAndReleasesEachDuck() => RunAsync(async (engine, s, played) =>
    {
        var ducks = 0; var unducks = 0; var flashes = 0; var gap = new TaskCompletionSource();
        s.AudioDuckingEnabled = true; s.AwarenessLoopProtectionEnabled = false;
        CoreAudio.DuckProvider = _ => ducks++;
        CoreAudio.UnduckProvider = _ => unducks++;
        CoreFlash.ShowProvider = () => flashes++;
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.DuckSystemAudio = true; audio.PlayCount = 2; audio.DelayBetweenMs = 5000;
        s.KeywordTriggers[0].Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash });
        s.KeywordTriggers.Add(new KeywordTrigger { Keyword = "stay", Actions = new() {
            new PlayAudioAction { FilePath = audio.FilePath, DuckSystemAudio = true, PlayCount = 2, DelayBetweenMs = 5000 },
            new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash } } });
        engine.Delay = (_, _) => gap.Task;
        var now = engine.Now(); Type(engine, "sit"); engine.Now = () => now.AddSeconds(2); Type(engine, "stay");
        Assert.Equal(2, ducks); Assert.Equal(0, unducks);
        engine.StopAudio();
        Assert.Equal(2, unducks);
        gap.SetResult(); await Task.Yield();
        Assert.Equal(2, played.Count(p => p.Path != "stopped")); Assert.Equal(0, flashes);
        Assert.Equal(2, unducks);
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task IdleHoldsKeywordAudioButNotVisualsAndEngineOrSessionAllowsIt(bool legacy) => RunAsync(async (engine, s, played) =>
    {
        var old = (CoreFlash.ShowProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
            s.AudioDuckingEnabled, s.AwarenessLoopProtectionEnabled);
        var flashes = 0; var ducks = 0; var unducks = new List<long>();
        var idleGap = new TaskCompletionSource(); var gaps = new List<int>();
        var running = false; var session = false;
        try
        {
            CoreSession.IsEngineRunningProvider = () => running;
            CoreSession.IsSessionRunningProvider = () => session;
            CoreFlash.ShowProvider = () => flashes++;
            CoreAudio.DuckProvider = _ => ducks++;
            CoreAudio.DuckGenerationProvider = () => 41;
            CoreAudio.UnduckProvider = unducks.Add;
            engine.Delay = (ms, _) => { gaps.Add(ms); return idleGap.Task; };
            s.AudioDuckingEnabled = true;
            s.AwarenessLoopProtectionEnabled = false;
            var t = s.KeywordTriggers[0];
            var audio = (PlayAudioAction)t.Actions[0];
            audio.DuckSystemAudio = true;
            if (legacy)
            {
                t.AudioFilePath = audio.FilePath; t.DuckAudio = true;
                t.VisualEffect = KeywordVisualEffect.ImageFlash; t.Actions.Clear();
            }
            else t.Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.ImageFlash });
            var now = engine.Now();
            Type(engine, "sit");
            Assert.Empty(played); Assert.Equal(legacy ? 1 : 0, ducks); Assert.Equal(1, flashes);
            if (legacy)
            {
                Assert.Equal(new[] { 500 }, gaps); Assert.Empty(unducks);
                idleGap.SetResult(); await Task.Yield();
                Assert.Equal(new long[] { 41 }, unducks);
            }
            running = true; now = now.AddMinutes(1); engine.Now = () => now;
            Type(engine, "sit");
            Assert.Single(played, p => p.Path != "stopped"); Assert.Equal(legacy ? 2 : 1, ducks); Assert.Equal(2, flashes);
            engine.StopAudio();
            running = false; session = true; now = now.AddMinutes(1);
            Type(engine, "sit");
            Assert.Equal(2, played.Count(p => p.Path != "stopped")); Assert.Equal(legacy ? 3 : 2, ducks); Assert.Equal(3, flashes);
        }
        finally
        {
            engine.StopAudio();
            (CoreFlash.ShowProvider, CoreAudio.DuckProvider, CoreAudio.UnduckProvider, CoreAudio.DuckGenerationProvider,
                s.AudioDuckingEnabled, s.AwarenessLoopProtectionEnabled) = old;
        }
    });

    [Fact]
    public void LegacyHighlightOnlyDoesNotPlayOrDuckEvenWhenEngineIsRunning() => Run((engine, s, played) =>
    {
        var old = (CoreAudio.DuckProvider, s.AudioDuckingEnabled);
        var ducks = 0;
        try
        {
            CoreAudio.DuckProvider = _ => ducks++;
            s.AudioDuckingEnabled = true;
            var t = s.KeywordTriggers[0];
            t.AudioFilePath = ((PlayAudioAction)t.Actions[0]).FilePath;
            t.Actions.Clear(); t.DuckAudio = true; t.VisualEffect = KeywordVisualEffect.HighlightOnly;
            Type(engine, "sit");
            Assert.Empty(played); Assert.Equal(0, ducks);
        }
        finally { (CoreAudio.DuckProvider, s.AudioDuckingEnabled) = old; }
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
}
