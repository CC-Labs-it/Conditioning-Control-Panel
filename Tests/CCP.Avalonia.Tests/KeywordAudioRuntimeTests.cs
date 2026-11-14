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
