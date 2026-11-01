using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessRecentAppsTests
{
    [Fact]
    public void SamplingAndHistoryFollowWpfTimingAndSessionGates()
    {
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggersEnabled, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        var engine = new KeywordAudioRuntime();
        try
        {
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            Assert.Equal((null, false), KeywordTriggerHead.Foreground(0));
            var now = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            Assert.True(engine.ShouldResolveForeground(false, now));
            Assert.False(engine.ShouldResolveForeground(false, now.AddMilliseconds(1999)));
            Assert.True(engine.ShouldResolveForeground(true, now.AddMilliseconds(1999)));
            Assert.True(engine.ShouldResolveForeground(false, now.AddSeconds(2)));
            engine.ObserveForeground("before-start", false);
            engine.Start();
            engine.ObserveForeground("self", true);
            engine.ObserveForeground(null, false);
            Assert.Empty(engine.GetRecentForegroundApps());
            for (var i = 0; i < 9; i++) engine.ObserveForeground("app" + i, false);
            engine.ObserveForeground("APP3", false);
            Assert.Equal(new[] { "app3", "app8", "app7", "app6", "app5", "app4", "app2", "app1" }, engine.GetRecentForegroundApps());
            engine.Stop();
            engine.ObserveForeground("after-stop", false);
            Assert.Equal("app3", engine.GetRecentForegroundApps()[0]);
            s.KeywordTriggersEnabled = false;
            engine.Start();
            engine.ObserveForeground("disabled", false);
            Assert.DoesNotContain("disabled", engine.GetRecentForegroundApps());
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => false;
            engine.ObserveForeground("no-access", false);
            Assert.DoesNotContain("no-access", engine.GetRecentForegroundApps());
        }
        finally
        {
            engine.Stop();
            (s.KeywordTriggersEnabled, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
    }

    [Fact]
    public void HistoryEvictsTheOldestAndMovesCaseInsensitiveRepeatsToTheFront()
    {
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggersEnabled, CoreEntitlement.HasPremiumProvider);
        var engine = new KeywordAudioRuntime();
        try
        {
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            engine.Start();
            for (var i = 0; i < 9; i++) engine.ObserveForeground("app" + i, false);
            engine.ObserveForeground("APP3", false);
            Assert.Equal(new[] { "app3", "app8", "app7", "app6", "app5", "app4", "app2", "app1" }, engine.GetRecentForegroundApps());
        }
        finally
        {
            engine.Stop();
            (s.KeywordTriggersEnabled, CoreEntitlement.HasPremiumProvider) = saved;
        }
    }

    [Fact]
    public void HistoryExcludesOwnAndUnknownProcesses()
    {
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggersEnabled, CoreEntitlement.HasPremiumProvider);
        var engine = new KeywordAudioRuntime();
        try
        {
            s.KeywordTriggersEnabled = true;
            CoreEntitlement.HasPremiumProvider = () => true;
            engine.Start();
            engine.ObserveForeground("self", true);
            engine.ObserveForeground(null, false);
            engine.ObserveForeground(" ", false);
            Assert.Empty(engine.GetRecentForegroundApps());
        }
        finally
        {
            engine.Stop();
            (s.KeywordTriggersEnabled, CoreEntitlement.HasPremiumProvider) = saved;
        }
    }

    [Fact]
    public Task RecentChipsRefreshAndAddToThePersistedScopeList() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previous = CoreSettings.ServiceProvider;
        var premium = CoreEntitlement.HasPremiumProvider;
        var free = CoreEntitlement.IsFreeTodayProvider;
        var engine = KeywordTriggerHead.Engine;
        var history = (LinkedList<string>)typeof(KeywordAudioRuntime).GetField("_seenApps", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(engine)!;
        var oldHistory = history.ToArray();
        var wasActive = engine.IsActive;
        var service = new SettingsService();
        try
        {
            history.Clear();
            CoreSettings.ServiceProvider = () => service;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            service.Current.KeywordTriggersEnabled = true;
            service.Current.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            service.Current.KeywordTriggerApps = new() { "Chrome.exe" };
            engine.Start();
            engine.ObserveForeground("chrome", false);
            engine.ObserveForeground("Discord", false);
            var view = new AwarenessTabView();
            var scope = view.FindControl<ComboBox>("CmbAwarenessAppScope")!;
            scope.SelectedItem = scope.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "OnlyListed");
            var panel = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
            var label = view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!;
            Assert.True(label.IsVisible);
            service.SaveImmediate(); // isolate the chip save from the earlier scope change
            var chip = Assert.IsType<Button>(Assert.Single(panel.Children));
            Assert.Equal("+ Discord", chip.Content);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { "Chrome.exe", "Discord" }, service.Current.KeywordTriggerApps);
            Assert.Equal("Chrome.exe, Discord", view.FindControl<TextBox>("TxtAwarenessAppList")!.Text);
            Assert.Empty(panel.Children);
            Assert.False(label.IsVisible);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            Assert.Equal(service.Current.KeywordTriggerApps, new SettingsService().Current.KeywordTriggerApps);
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            box.Text = "Chrome";
            box.RaiseEvent(new RoutedEventArgs(global::Avalonia.Input.InputElement.LostFocusEvent));
            Assert.Equal("+ Discord", Assert.IsType<Button>(Assert.Single(panel.Children)).Content);
        }
        finally
        {
            engine.Stop();
            history.Clear();
            foreach (var app in oldHistory) history.AddLast(app);
            CoreSettings.ServiceProvider = previous;
            CoreEntitlement.HasPremiumProvider = premium;
            CoreEntitlement.IsFreeTodayProvider = free;
            if (wasActive) engine.Start();
        }
        return Task.CompletedTask;
    });
}
