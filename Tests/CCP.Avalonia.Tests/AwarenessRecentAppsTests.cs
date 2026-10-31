using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
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
    public void RingKeepsEightDistinctNewestFirstAndIgnoresOwnUnknownAndFailedLookups()
    {
        var ring = new RecentForegroundApps();
        for (var i = 0; i < 10; i++) { var name = "app" + i; ring.Source = () => (name, false); ring.Sample(); }
        Assert.Equal(Enumerable.Range(2, 8).Reverse().Select(i => "app" + i), ring.Snapshot());
        ring.Source = () => ("APP3", false); ring.Sample();
        Assert.Equal("app3", ring.Snapshot()[0]);
        Assert.Equal(new[] { "app3", "app9", "app8", "app7", "app6", "app5", "app4", "app2" }, ring.Snapshot());
        var before = ring.Snapshot();
        ring.Source = () => ("ccp", true); ring.Sample();
        ring.Source = () => (null, false); ring.Sample();
        ring.Source = () => (" ", false); ring.Sample();
        ring.Source = () => throw new InvalidOperationException(); ring.Sample();
        Assert.Equal(before, ring.Snapshot());
    }

    [Fact]
    public Task SamplingRunsOnlyWithEngineAndRetainsSessionSuggestionsAfterStop() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var ring = KeywordTriggerHead.RecentApps;
        var source = ring.Source;
        try
        {
            ring.Clear();
            var calls = 0;
            ring.Source = () => { calls++; return ("firefox", false); };
            var timer = (global::Avalonia.Threading.DispatcherTimer)typeof(KeywordTriggerHead)
                .GetField("AppSampler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!.GetValue(null)!;
            Assert.Equal(TimeSpan.FromSeconds(2), timer.Interval);
            var enabled = CoreSettings.Current.KeywordTriggersEnabled;
            try
            {
                CoreSettings.Current.KeywordTriggersEnabled = true;
                KeywordTriggerHead.Sync(); Assert.True(timer.IsEnabled);
                await Task.Delay(TimeSpan.FromMilliseconds(2200));
                Assert.Equal(1, calls); // actual timer tick must reach the injected process source
                calls = 0;
                CoreSettings.Current.KeywordTriggersEnabled = false;
                KeywordTriggerHead.Sync(); Assert.False(timer.IsEnabled);
            }
            finally { CoreSettings.Current.KeywordTriggersEnabled = enabled; }
            KeywordTriggerHead.Stop(); KeywordTriggerHead.SampleForegroundApp(); Assert.Equal(0, calls);
            KeywordTriggerHead.Engine.Start(); KeywordTriggerHead.SampleForegroundApp(); Assert.Equal(1, calls);
            KeywordTriggerHead.Stop(); KeywordTriggerHead.SampleForegroundApp(); Assert.Equal(1, calls);
            Assert.Equal(new[] { "firefox" }, ring.Snapshot());
        }
        finally { KeywordTriggerHead.Stop(); ring.Clear(); ring.Source = source; }
    });

    [Fact]
    public void SuggestionsRefreshOnShowModeCommitAndClickSavesCanonicalList() => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current == null)
            AppBuilder.Configure<ConditioningControlPanel.Avalonia.App>().UseSkia().WithInterFont().With(AppFonts.Options())
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var ring = KeywordTriggerHead.RecentApps;
        var source = ring.Source;
        var provider = CoreSettings.ServiceProvider;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        var s = service.Current;
        try
        {
            ring.Clear();
            ring.Source = () => ("firefox", false); ring.Sample();
            ring.Source = () => ("discord", false); ring.Sample();
            s.KeywordTriggerApps = new() { " DISCORD.exe " };
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            var view = new AwarenessTabView();
            var panel = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
            var label = view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!;
            Assert.True(label.IsVisible);
            var chip = Assert.IsType<Button>(Assert.Single(panel.Children));
            Assert.Equal("+ firefox", chip.Content);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(new[] { " DISCORD.exe ", "firefox" }, s.KeywordTriggerApps);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            Assert.Equal(s.KeywordTriggerApps, new SettingsService().Current.KeywordTriggerApps);
            Assert.Empty(panel.Children); Assert.False(label.IsVisible);
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            Assert.Equal(" DISCORD.exe , firefox", box.Text);
            box.Text = "firefox";
            box.RaiseEvent(new RoutedEventArgs(InputElement.LostFocusEvent));
            Assert.Equal("+ discord", Assert.IsType<Button>(Assert.Single(panel.Children)).Content);
            ring.Source = () => ("steam", false); ring.Sample();
            var mode = view.FindControl<ComboBox>("CmbAwarenessAppScope")!;
            mode.SelectedItem = mode.Items.OfType<ComboBoxItem>().Single(i => (string?)i.Tag == "ExceptListed");
            Assert.Equal(2, panel.Children.Count);
            ring.Source = () => ("zoom", false); ring.Sample();
            view.IsVisible = false; view.IsVisible = true;
            Assert.Equal(3, panel.Children.Count);
        }
        finally { SettingsService.DisarmAllPendingSaves(); CoreSettings.ServiceProvider = provider; ring.Clear(); ring.Source = source; }
    });
}
