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
    private static string SourceRoot([System.Runtime.CompilerServices.CallerFilePath] string path = "") =>
        System.IO.Path.GetFullPath(System.IO.Path.Combine(System.IO.Path.GetDirectoryName(path)!, "../.."));
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
    public Task InputSamplingGatesAccessThrottlesOnlyUnrestrictedAndCapturesRapidSwitches() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var engine = new KeywordAudioRuntime();
        var ring = engine.RecentApps;
        var s = CoreSettings.Current;
        var saved = (s.KeywordTriggersEnabled, s.KeywordTriggerAppScope, s.KeywordTriggerIgnoreOwnFocus,
            s.KeywordTriggerApps, s.KeywordTriggers, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider);
        var now = new DateTime(2035, 1, 1);
        var calls = 0;
        string app = "firefox";
        ring.Source = () => { calls++; return (app, false); };
        engine.Now = () => now;
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => false;
            s.KeywordTriggersEnabled = true; s.KeywordTriggers = new();
            s.KeywordTriggerIgnoreOwnFocus = false;
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            engine.Key(KeywordInputKey.Clear); Assert.Equal(0, calls); // engine off
            engine.Start(); await Task.Delay(2200); Assert.Equal(0, calls); // no idle sampling
            engine.Key(KeywordInputKey.Clear); Assert.Equal(1, calls);
            app = "discord"; now = now.AddMilliseconds(100);
            engine.Character('x'); Assert.Equal(1, calls); // unrestricted throttle
            now = now.AddMilliseconds(1900);
            engine.Key(KeywordInputKey.Clear); Assert.Equal(2, calls); // exact boundary
            s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
            s.KeywordTriggerApps = new(); // even blocked apps are remembered, as WPF :582
            app = "steam"; now = now.AddMilliseconds(10); engine.Character('x');
            app = "zoom"; now = now.AddMilliseconds(10); engine.Key(KeywordInputKey.Clear);
            Assert.Equal(new[] { "zoom", "steam", "discord", "firefox" }, ring.Snapshot());
            s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
            s.KeywordTriggerIgnoreOwnFocus = true;
            app = "teams"; engine.Key(KeywordInputKey.Clear); Assert.Equal(5, calls);
            CoreEntitlement.HasPremiumProvider = () => false;
            app = "private"; now = now.AddSeconds(3);
            engine.Key(KeywordInputKey.Clear); engine.Character('x'); Assert.Equal(5, calls);
            CoreEntitlement.IsFreeTodayProvider = _ => true;
            engine.Key(KeywordInputKey.Clear); Assert.Equal(6, calls);
            s.KeywordTriggersEnabled = false;
            engine.Character('x'); engine.Key(KeywordInputKey.Clear); Assert.Equal(6, calls);
            engine.Stop(); Assert.Equal("private", ring.Snapshot()[0]); // session ring retained
            Assert.Null(typeof(KeywordTriggerHead).GetField("AppSampler", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
        }
        finally
        {
            engine.Stop();
            (s.KeywordTriggersEnabled, s.KeywordTriggerAppScope, s.KeywordTriggerIgnoreOwnFocus,
                s.KeywordTriggerApps, s.KeywordTriggers, CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider) = saved;
        }
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
            Assert.Equal("Hand", chip.Cursor?.ToString());
            Assert.Equal(string.Format(ConditioningControlPanel.Localization.Loc.Get("awareness_add_app_tip"), "firefox"), ToolTip.GetTip(chip));
            foreach (var file in System.IO.Directory.GetFiles(System.IO.Path.Combine(SourceRoot(), "CCP.Core/Localization/Languages"), "*.json"))
            {
                using var json = System.Text.Json.JsonDocument.Parse(System.IO.File.ReadAllText(file));
                var tip = json.RootElement.GetProperty("awareness_add_app_tip").GetString()!;
                Assert.Contains("{0}", tip); Assert.NotEqual("awareness_add_app_tip", tip);
                Assert.Contains("firefox", string.Format(tip, "firefox"));
            }
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
