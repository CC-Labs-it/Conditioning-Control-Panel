using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AwarenessSeenAppsTests
{
    [Fact]
    public void EverywhereSamplesEveryTwoSecondsWhileScopedInputAlwaysResolves()
    {
        var s = CoreSettings.Current;
        s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
        s.KeywordTriggerIgnoreOwnFocus = false;
        var engine = new KeywordAudioRuntime();
        var now = new DateTime(2026, 10, 30);
        Assert.True(engine.NeedsForeground(now));
        Assert.False(engine.NeedsForeground(now.AddMilliseconds(1999)));
        Assert.True(engine.NeedsForeground(now.AddSeconds(2)));
        s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
        Assert.True(engine.NeedsForeground(now.AddMilliseconds(2001)));
        s.KeywordTriggerAppScope = AwarenessAppScope.Everywhere;
        s.KeywordTriggerIgnoreOwnFocus = true;
        Assert.True(engine.NeedsForeground(now.AddMilliseconds(2002)));
    }

    [Fact]
    public void RunningSourceRemembersEightDistinctAppsBeforeScopeFilteringButNeverOwnOrUnknown()
    {
        CoreEntitlement.HasPremiumProvider = () => true;
        var s = CoreSettings.Current;
        s.KeywordTriggersEnabled = true;
        s.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
        s.KeywordTriggerApps = new(); // all input suppressed, but suggestions must still fill
        var engine = new KeywordAudioRuntime();
        engine.Key(KeywordInputKey.None, "inactive");
        Assert.Empty(engine.GetRecentForegroundApps());
        engine.Start();
        for (var i = 0; i < 10; i++) engine.Key(KeywordInputKey.None, "app" + i);
        engine.Character('x', "APP5");
        engine.Key(KeywordInputKey.None, "ccp", true);
        engine.Key(KeywordInputKey.None, null);
        engine.Key(KeywordInputKey.None, " ");
        Assert.Equal(new[] { "app5", "app9", "app8", "app7", "app6", "app4", "app3", "app2" }, engine.GetRecentForegroundApps());
        engine.Stop();
        engine.Key(KeywordInputKey.None, "stopped");
        Assert.Equal(8, engine.GetRecentForegroundApps().Length);
        Assert.DoesNotContain("stopped", engine.GetRecentForegroundApps());
    }

    [Theory]
    [InlineData("en", "Add 'discord' to the list")]
    [InlineData("de", "'discord' zur Liste hinzufügen")]
    [InlineData("es", "Añadir 'discord' a la lista")]
    [InlineData("fr", "Ajouter 'discord' à la liste")]
    [InlineData("it", "Aggiungi 'discord' all'elenco")]
    [InlineData("pt-BR", "Adicionar 'discord' à lista")]
    [InlineData("ru", "Добавить 'discord' в список")]
    [InlineData("ja", "'discord' をリストに追加")]
    [InlineData("ko", "'discord'을 목록에 추가")]
    [InlineData("zh-CN", "将“discord”添加到列表")]
    public void ChipAddsAndSavesWithoutRearmingKeyboardAndUsesLocalizedTooltip(string language, string tooltip) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var previousLanguage = LocalizationManager.Instance.CurrentLanguage;
        var service = new SettingsService();
        CoreSettings.ServiceProvider = () => service;
        CoreEntitlement.HasPremiumProvider = () => true;
        service.Current.KeywordTriggersEnabled = true;
        service.Current.KeywordTriggerAppScope = AwarenessAppScope.OnlyListed;
        service.Current.KeywordTriggerApps = new() { "CHROME.exe" };
        var engine = KeywordTriggerHead.Engine;
        var ring = (System.Collections.Generic.LinkedList<string>)typeof(KeywordAudioRuntime)
            .GetField("_seenApps", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(engine)!;
        var before = ring.ToArray();
        ring.Clear();
        engine.Start();
        try
        {
            engine.Key(KeywordInputKey.None, "chrome");
            engine.Key(KeywordInputKey.None, "discord");
            LocalizationManager.Instance.SetLanguage(language);
            var view = new AwarenessTabView();
            var master = view.FindControl<CheckBox>("ChkAwarenessMaster")!;
            var keyboard = view.FindControl<CheckBox>("ChkAwarenessKeyboard")!;
            Assert.True(master.IsChecked);
            keyboard.IsChecked = false; // real user path: master stays on, keyboard source detaches
            Assert.False(keyboard.IsChecked);
            Assert.True(master.IsChecked);
            var chips = view.FindControl<WrapPanel>("AwarenessSeenAppsPanel")!;
            var label = view.FindControl<TextBlock>("TxtAwarenessSeenAppsLabel")!;
            Assert.True(label.IsVisible);
            var chip = Assert.Single(chips.Children.OfType<Button>());
            Assert.Equal("+ discord", chip.Content);
            Assert.Equal(tooltip, ToolTip.GetTip(chip));
            service.SaveImmediate();
            Assert.False(service.HasPendingSave);
            chip.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(keyboard.IsChecked);
            Assert.True(master.IsChecked);
            Assert.True(service.Current.KeywordTriggersEnabled);
            Assert.Equal(new[] { "CHROME.exe", "discord" }, service.Current.KeywordTriggerApps);
            Assert.True(service.HasPendingSave);
            service.SaveImmediate();
            Assert.Equal(service.Current.KeywordTriggerApps, new SettingsService().Current.KeywordTriggerApps);
            Assert.Empty(chips.Children);
            Assert.False(label.IsVisible);
            var box = view.FindControl<TextBox>("TxtAwarenessAppList")!;
            Assert.Equal("CHROME.exe, discord", box.Text);
            box.Text = "chrome";
            box.RaiseEvent(new RoutedEventArgs(global::Avalonia.Input.InputElement.LostFocusEvent));
            Assert.Equal("+ discord", Assert.Single(chips.Children.OfType<Button>()).Content);
        }
        finally
        {
            engine.Stop(); ring.Clear(); foreach (var app in before) ring.AddLast(app);
            LocalizationManager.Instance.SetLanguage(previousLanguage);
        }
    });
}
