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
    // The singleton deliberately retains cooldowns across Stop. Every fixture moves forward
    // beyond the previous fixture (including the panic test's two-hour second match).
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
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled, CoreSession.IsEngineRunningProvider);
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
            CoreSession.IsEngineRunningProvider = () => true; // WPF holds keyword audio while the engine is off.
            engine.Start();
            await body(engine, s, played);
        }
        finally
        {
            engine.Stop(); KeywordTriggerHead.Stop(); File.Delete(file);
            KeywordTriggerHead.Engine.Now = singletonClock;
            (s.KeywordTriggers, s.KeywordTriggersEnabled, s.KeywordTriggerIgnoreOwnFocus, s.KeywordTriggerAppScope,
                s.KeywordGlobalCooldownSeconds, s.KeywordPerKeywordCooldownSeconds, s.MasterVolume,
                CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreAudio.PlayStoppableProvider, s.AwarenessLoopProtectionEnabled, CoreSession.IsEngineRunningProvider) = saved;
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
    [Fact]
    public void PulseFeedShowsFiveNewestRecordsAndTheirSavedActionChips() => Run((_, _, _) =>
    {
        var view = new AwarenessTabView();
        var window = new Window { Content = view };
        window.Show();
        try
        {
            for (var i = 0; i < 6; i++) KeywordTriggerHead.Engine.FireDemo("pulse " + i, "Tutorial");
            var feed = view.FindControl<StackPanel>("AwarenessPulseFeed")!;
            Assert.Equal(5, feed.Children.Count);
            var row = Assert.IsType<Grid>(Assert.IsType<Border>(feed.Children[0]).Child);
            Assert.Equal("\"pulse 5\"", Assert.IsType<TextBlock>(row.Children[1]).Text);
            var chips = Assert.IsType<StackPanel>(row.Children[2]).Children.OfType<ConditioningControlPanel.Avalonia.Controls.IconGlyph>().ToList();
            Assert.Equal(2, chips.Count);   // WPF tooltips are prose, not raw action keys
            Assert.Equal("Draws a glowing box around the matched word on screen (OCR matches only)", ToolTip.GetTip(chips[0]));
            Assert.Equal(4, Grid.GetColumn(row.Children[3]));   // source chip right of the * filler (WPF :204)
            Assert.Equal("TUTORIAL", Assert.IsType<TextBlock>(Assert.IsType<Border>(row.Children[3]).Child).Text);
            Assert.Contains("fires today", view.FindControl<TextBlock>("TxtAwarenessFireCount")!.Text!);
        }
        finally { window.Close(); KeywordTriggerHead.Engine.ClearRecentFires(); }
    });

    [Fact]
    public void AppScopeListCommitsCanonicalProcessNamesOnFocusLoss() => Run((_, s, _) =>
    {
        var saved = s.KeywordTriggerApps;
        try
        {
            s.KeywordTriggerApps = new();
            var view = new AwarenessTabView();
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            box.Text = "editor.exe; browser, EDITOR";
            box.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Input.InputElement.LostFocusEvent));
            Assert.Equal(new[] { "editor", "browser" }, s.KeywordTriggerApps);
            Assert.Equal("editor, browser", box.Text);
        }
        finally { s.KeywordTriggerApps = saved; }
    });

    [Fact]
    public void RecentAppChipsExcludeOwnAndListedAppsAndAddWithoutDuplicates() => Run((_, s, _) =>
    {
        var saved = s.KeywordTriggerApps;
        try
        {
            s.KeywordTriggerApps = new() { "editor.exe" };
            s.KeywordTriggerAppScope = AwarenessAppScope.ExceptListed;
            var engine = KeywordTriggerHead.Engine;
            engine.Start();
            engine.Character('x', "editor");
            engine.Character('x', "Browser");
            engine.Character('x', "ccp", own: true);
            var view = new AwarenessTabView();
            var panel = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
            var chip = Assert.Single(panel.Children.OfType<Button>());
            Assert.Equal("+ Browser", chip.Content);
            chip.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { "editor.exe", "Browser" }, s.KeywordTriggerApps);
            Assert.Empty(panel.Children);
            view.SyncAwarenessTabUi();
            Assert.Empty(panel.Children);
            Assert.False(view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!.IsVisible);
        }
        finally { s.KeywordTriggerApps = saved; }
    });

    [Fact]
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
            // A saved-on master can precede asynchronous entitlement validation.
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            shell.RefreshEntitlementVeils();
            Assert.True(KeywordTriggerHead.Engine.IsActive);
        }
        finally { shell.Close(); }
    });

    [Fact]
    public void MasterSwitchDrivesTheExistingSharedBeatStatusGlow() => Run((_, s, _) =>
    {
        s.KeywordTriggersEnabled = false;
        var shell = new MainShellWindow();
        shell.Show();
        shell.Activate();
        try
        {
            shell.ShowTab("awareness");
            Dispatcher.UIThread.RunJobs();
            var view = shell.Named<AwarenessTabView>("AwarenessTab")!;
            var master = view.FindControl<CheckBox>("ChkAwarenessMaster")!;
            var dot = view.FindControl<Control>("AwarenessStatusDot")!;
            master.IsChecked = true;
            Assert.IsType<global::Avalonia.Media.DropShadowEffect>(dot.Effect);
            master.IsChecked = false;
            Assert.Null(dot.Effect);
            Assert.Equal(1, dot.Opacity);
        }
        finally { shell.Close(); }
    });

}
