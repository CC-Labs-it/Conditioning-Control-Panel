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
            engine.Stop(); KeywordTriggerHead.Stop(); File.Delete(file);
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled,
                CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider) = saved;
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
