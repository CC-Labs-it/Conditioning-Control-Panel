using System;
using System.Collections.Generic;
using Avalonia.Threading;
using Avalonia.Controls;
using CCP.Avalonia.Testing;
using Avalonia.Interactivity;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    // WPF PlayTabView.Cards.cs:45-46 -> BreakoutHostService.cs:14-17.
    [Fact]
    public void Breakout_PlayCardDemandsLabBeforeOpening_ThenSendsFullAccess() => Run((shell, launcher) =>
    {
        launcher.Hide();
        bool lab = false;
        int denials = 0;
        CoreEntitlement.HasLabProvider = () => lab;
        CoreEntitlement.ShowDeniedHandler = _ => denials++;
        shell.ShowTab("play");
        var button = shell.Named<Control>("PlayTab")!.FindControl<Button>("BtnPlayBreakout")!;
        Assert.True(button.IsEnabled);
        Assert.Null(ToolTip.GetTip(button));
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(1, denials);
        Assert.False(GameWindow.IsAnyOpen());
        lab = true;
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(GameWindow.IsAnyOpen());
        var window = GameWindow.Launch("breakout")!;
        Assert.NotNull(window);
        Assert.Same(window, GameWindow.Launch("breakout"));
        Assert.Contains("desktop=1", window.PageUrl!.Query);
        Assert.DoesNotContain("demo=1", window.PageUrl.Query);
        var sent = new List<JObject>();
        window.Posted += json => sent.Add(JObject.Parse(json));
        window.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal(8, (int)sent[0]["breakout"]!["storyLimit"]!);
        Assert.True((bool)sent[0]["breakout"]!["endless"]!);
        Assert.False((bool)sent[0]["breakout"]!["demo"]!);
        Assert.True((bool)sent[0]["breakoutStandalone"]!);
        lab = false;
        Assert.Null(GameWindow.Launch("breakout"));
        Assert.Equal(2, denials);
    });

    // WPF BreakoutAccess.cs:12-16, BackRoomHostService.cs:736-763.
    [Fact]
    public void Breakout_AccessLossDowngradesTheLivePage_DemoNeverExpands() => Run((_, launcher) =>
    {
        launcher.Hide();
        bool lab = true;
        CoreEntitlement.HasLabProvider = () => lab;
        var window = GameWindow.Launch("breakout")!;
        var sent = new List<JObject>();
        window.Posted += json => sent.Add(JObject.Parse(json));
        window.PushBreakoutAccess();
        Assert.Empty(sent);
        lab = false;
        var watch = (DispatcherTimer)typeof(GameWindow).GetField("_breakoutAccessWatch",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(window)!;
        Assert.True(watch.IsEnabled);
        Assert.Equal(TimeSpan.FromSeconds(2), watch.Interval);
        window.PushBreakoutAccess();
        Assert.Equal("settings", (string?)sent[0]["type"]);
        Assert.Equal(3, (int)sent[0]["breakout"]!["storyLimit"]!);
        Assert.False((bool)sent[0]["breakout"]!["endless"]!);
        Assert.True((bool)sent[0]["breakout"]!["demo"]!);
        window.PushBreakoutAccess();
        Assert.Single(sent);
        lab = true;
        window.PushBreakoutAccess();
        Assert.Equal(8, (int)sent[1]["breakout"]!["storyLimit"]!);
        var demo = JObject.FromObject(GameWindow.BreakoutEntitlementFor("breakoutdemo")!);
        Assert.Equal(3, (int)demo["storyLimit"]!);
        Assert.False((bool)demo["endless"]!);
        Assert.True((bool)demo["demo"]!);
        window.Close();
        Assert.False(watch.IsEnabled);
        lab = false;
        window.PushBreakoutAccess();
        Assert.Equal(2, sent.Count);
    });
}
