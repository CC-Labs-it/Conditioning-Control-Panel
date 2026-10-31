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
            CoreSession.IsEngineRunningProvider = () => true;
            CoreSession.IsSessionRunningProvider = () => false;
            engine.Start();
            await body(engine, s, played);
        }
        finally
        {
            engine.Stop(); KeywordTriggerHead.Stop(); File.Delete(file);
            KeywordTriggerHead.Engine.Now = singletonClock;
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled, CoreSession.IsEngineRunningProvider, CoreSession.IsSessionRunningProvider) = saved;
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

    [Theory] // WPF KeywordTriggerService.cs:1713,1774-1782: flat and action-list paths.
    [InlineData(false)]
    [InlineData(true)]
    public void ExactSubliminalEchoesUppercaseAndForcesThreeSecondMute(bool legacy) => Run((engine, s, _) =>
    {
        var shown = new List<string>();
        ConditioningControlPanel.Avalonia.Views.Overlays.SubliminalWhisperShow.Draw = shown.Add;
        s.AwarenessLoopProtectionEnabled = false;
        var t = s.KeywordTriggers[0];
        t.VisualEffect = KeywordVisualEffect.ExactSubliminal;
        t.Actions.Clear();
        if (!legacy) t.Actions.Add(new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal });
        var now = engine.Now();
        engine.Now = () => now;
        Type(engine, "sit");
        Assert.Equal(new[] { "SIT" }, shown);
        now = now.AddMilliseconds(2999);
        Type(engine, "sit");
        Assert.Single(shown);
        engine.Key(ConditioningControlPanel.Services.KeywordInputKey.Clear);
        now = now.AddMilliseconds(1);
        Type(engine, "sit");
        Assert.Equal(new[] { "SIT", "SIT" }, shown);
    });

    [Fact] // WPF Subliminal/SubliminalService.cs:311-324.
    public void ExactSubliminalUsesCustomTextWithoutWhisperAndAwardsSubliminalXp() => Run((engine, s, played) =>
    {
        var shown = new List<string>();
        var xp = new List<(double Amount, string Source)>();
        ConditioningControlPanel.Avalonia.Views.Overlays.SubliminalWhisperShow.Draw = shown.Add;
        CoreProgression.AddXPProvider = (amount, source) => xp.Add((amount, source));
        s.KeywordTriggers[0].Keyword = "<b>sit</b>";
        s.KeywordTriggers[0].MatchType = KeywordMatchType.Regex;
        s.KeywordTriggers[0].Actions = new List<KeywordAction> { new VisualEffectAction {
            Effect = KeywordVisualEffect.ExactSubliminal } };
        Type(engine, "<b>sit</b>");
        Assert.Equal(new[] { "SIT" }, shown);
        Assert.Equal((10d, "Subliminal"), Assert.Single(xp));
        Assert.Empty(played);
    });

    [Fact] // WPF KeywordTriggerService.cs:1263,1288-1293,1309.
    public void ExactSubliminalHonorsEnabledDeduplicatesAndStaysQuietAfterPanic() => Run((engine, s, _) =>
    {
        var shown = new List<string>();
        ConditioningControlPanel.Avalonia.Views.Overlays.SubliminalWhisperShow.Draw = shown.Add;
        var t = s.KeywordTriggers[0];
        t.Actions = new List<KeywordAction> { new VisualEffectAction {
            Effect = KeywordVisualEffect.ExactSubliminal, Enabled = false } };
        Type(engine, "sit");
        Assert.Empty(shown);
        t.Actions[0].Enabled = true;
        s.KeywordTriggers.Add(new KeywordTrigger { Id = "preset:echo", Keyword = "sit", CooldownSeconds = 0,
            Actions = new List<KeywordAction> { new VisualEffectAction { Effect = KeywordVisualEffect.ExactSubliminal } } });
        engine.Now = () => new DateTime(2026, 10, 15);
        Type(engine, "sit");
        Assert.Equal(new[] { "SIT" }, shown);
        engine.Now = () => new DateTime(2026, 10, 16);
        ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
        try
        {
            Type(engine, "sit");
            Assert.Single(shown);
        }
        finally { ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet(); }
    });

}
