using System;
using System.Reflection;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Bark;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Possession;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class PossessionReadoutTests
{
    [Fact]
    public Task RulesAreOfferedOnlyWhenEnabledAndSpentWhenOpened() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = LockdownService.Current;
        using var lockdown = new LockdownService(); LockdownService.Current = lockdown;
        var s = CoreSettings.Current;
        var saved = (s.LockdownPossessionEnabled, s.LockdownPossessionIntroSeen, s.AvatarEnabled, s.BarkChatSuppressionMs);
        var seen = s.SeenFeatureIntros.ToList();
        foreach (var key in FeatureIntros.All.Keys) if (!s.SeenFeatureIntros.Contains(key)) s.SeenFeatureIntros.Add(key);
        s.SeenFeatureIntros.Remove("possession");
        var oldBark = BarkHead.Engine;
        var engine = new BarkEngine(() => new BarkRuleSet(new[] { new BarkRule
        {
            Id = "test_rules", Trigger = PossessionBarkTriggers.Rules,
            VariantPool = new() { new BarkVariant("rules") },
        } }));
        int barks = 0; engine.Speak = _ => { }; engine.BarkSpoken += (_, _) => barks++; engine.Start(); BarkHead.Engine = engine;
        (s.LockdownPossessionEnabled, s.LockdownPossessionIntroSeen, s.AvatarEnabled, s.BarkChatSuppressionMs) = (false, false, true, 0);
        StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
        var opened = new List<Window>();
        using var hook = Window.WindowOpenedEvent.AddClassHandler<Window>((w, _) => opened.Add(w));
        var shell = new MainShellWindow(); shell.Show(); Dispatcher.UIThread.RunJobs();
        try
        {
            StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
            StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
            lockdown.Activate(TimeSpan.FromMinutes(10)); Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:possession")); Assert.Equal(0, barks);
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs();
            s.LockdownPossessionEnabled = true;
            lockdown.Activate(TimeSpan.FromMinutes(10)); Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, barks); Assert.False(s.LockdownPossessionIntroSeen);
            var row = Assert.Single(StartupLadder.Inbox.Items, x => x.Key == "intro:possession");
            StartupLadder.Inbox.Dismiss(row);
            Assert.False(s.LockdownPossessionIntroSeen);
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs();
            lockdown.Activate(TimeSpan.FromMinutes(10)); Dispatcher.UIThread.RunJobs();
            row = Assert.Single(StartupLadder.Inbox.Items, x => x.Key == "intro:possession");
            StartupLadder.Inbox.Open(row); Dispatcher.UIThread.RunJobs();
            Assert.True(s.LockdownPossessionIntroSeen);
            Assert.Single(opened.OfType<FeatureIntroPopup>());
            foreach (var card in opened.OfType<FeatureIntroPopup>()) card.Close();
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs();
            StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
            lockdown.Activate(TimeSpan.FromMinutes(10)); Dispatcher.UIThread.RunJobs();
            Assert.False(StartupLadder.Inbox.Contains("intro:possession"));
            Assert.Single(opened.OfType<FeatureIntroPopup>());
        }
        finally
        {
            lockdown.Deactivate(); foreach (var w in opened.Where(w => w != shell)) w.Close(); shell.Close();
            LockdownService.Current = previous; BarkHead.Engine = oldBark;
            (s.LockdownPossessionEnabled, s.LockdownPossessionIntroSeen, s.AvatarEnabled, s.BarkChatSuppressionMs) = saved;
            s.SeenFeatureIntros.Clear(); s.SeenFeatureIntros.AddRange(seen);
            StartupLadder.ResetForTests(); FeatureIntroPopup.ResetForTests();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ReadoutFollowsDirectorAndStopsWithLockdownAndShell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = LockdownService.Current;
        using var lockdown = new LockdownService();
        var now = DateTime.UtcNow;
        lockdown.UtcNow = () => now;
        LockdownService.Current = lockdown;
        var settings = CoreSettings.Current;
        var saved = (settings.LockdownPossessionEnabled, settings.LockdownPossessionIntensity);
        settings.LockdownPossessionEnabled = true;
        settings.LockdownPossessionIntensity = (int)PossessionIntensity.FullDoki;
        var shell = new MainShellWindow();
        try
        {
            var tab = shell.Named<LockdownTabView>("LockdownTab")!;
            var text = tab.FindControl<TextBlock>("TxtPossessionRung")!;
            var pips = tab.FindControl<StackPanel>("PossessionPips")!;
            var director = shell.PossessionDirector!;
            lockdown.Activate(TimeSpan.FromMinutes(10));
            Dispatcher.UIThread.RunJobs();
            Assert.True(text.IsVisible); Assert.True(pips.IsVisible);
            Assert.Equal(Color.Parse("#33FF8A5C"), ((ISolidColorBrush)((Border)pips.Children[1]).Background!).Color);
            now += TimeSpan.FromMinutes(9);
            typeof(LockdownService).GetMethod("OnCountdownTick", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(lockdown, new object?[] { null, EventArgs.Empty });
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(PossessionRung.ItKnows, director.CurrentRung);
            Assert.Equal(Color.Parse("#FF8A5C"), ((ISolidColorBrush)((Border)pips.Children[4]).Background!).Color);
            Assert.Contains(ConditioningControlPanel.Localization.Loc.Get("lockdown_poss_rung_4"), text.Text);
            settings.LockdownPossessionEnabled = false;
            tab.ShowLockdownState();
            Assert.False(text.IsVisible); Assert.False(pips.IsVisible);
            settings.LockdownPossessionEnabled = true;
            lockdown.Deactivate(); Dispatcher.UIThread.RunJobs();
            Assert.False(text.IsVisible); Assert.False(pips.IsVisible);
            var eventField = typeof(PossessionDirector).GetField("RungChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
            Assert.NotNull(eventField.GetValue(director));
            shell.Show(); shell.Close(); Dispatcher.UIThread.RunJobs();
            Assert.Null(eventField.GetValue(director));
        }
        finally
        {
            lockdown.Deactivate(); shell.Close(); LockdownService.Current = previous;
            (settings.LockdownPossessionEnabled, settings.LockdownPossessionIntensity) = saved;
        }
        return Task.CompletedTask;
    });
}
