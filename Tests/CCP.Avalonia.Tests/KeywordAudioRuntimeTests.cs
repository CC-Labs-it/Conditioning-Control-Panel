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

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public Task RepeatGapPrecedesLaterActionsAndStopOrPanicCancelsThem(int stop) => RunAsync(async (engine, s, played) =>
    {
        var calls = new List<string>();
        engine.Haptic = (_, _) => calls.Add("haptic"); engine.AddXp = _ => calls.Add("xp");
        engine.ChasterBook = _ => { calls.Add("chaster"); return 0; };
        var audio = (PlayAudioAction)s.KeywordTriggers[0].Actions[0];
        audio.PlayCount = 2; audio.DelayBetweenMs = 50;
        s.KeywordTriggers[0].Actions.AddRange(new KeywordAction[] { new HapticAction(),
            new AddXpAction { Amount = 5 }, new ChasterAddTimeAction { Minutes = 1 } });
        var gap = new TaskCompletionSource(); engine.Delay = (_, _) => gap.Task;
        try
        {
            Type(engine, "sit"); Assert.Single(played); Assert.Empty(calls);
            if (stop == 1) engine.Stop();
            if (stop == 2) { engine.StopAudio(); ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet(); }
            gap.SetResult(); await engine.LastDispatch;
            Assert.Equal(stop == 0 ? new[] { "haptic", "xp", "chaster" } : Array.Empty<string>(), calls);
            Assert.Equal(stop == 0 ? 2 : 1, played.Count(x => x.Path != "stopped"));
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
}
