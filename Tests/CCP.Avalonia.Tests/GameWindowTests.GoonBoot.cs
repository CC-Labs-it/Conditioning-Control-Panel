using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Localization;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

// Navigation sequence from port/7.2 9966844ef8 (GameWindowTests.GoonBoot.cs); the inbox and
// media-walk assertions stay with that commit's inbox lane.
public sealed partial class GameWindowTests
{
    [Fact]
    public void GoonBoot_EarlyReadyWaitsForConfirmedPage() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        Assert.True(w.Web.InjectCarrier);   // WebKitGTK has no chrome.webview without it (bridge.js:49-52)
        w.Load(GameWindow.Server()); w.Show();
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Web.NewScrolllerEngine()(true);
        void Start(Uri url)
        {
            var e = new WebViewNavigationStartingEventArgs { Request = url };
            w.Web.OnNavigationStarted(null, e);
            Assert.False(e.Cancel);
        }
        var page = w.PageUrl!;
        var wrong = new Uri(page, "/other.html");
        Start(wrong);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        w.Web.OnNavigationCompleted(wrong);
        Assert.Empty(sent);
        Start(page);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Start(wrong); // a later navigation must discard the old document's ready
        w.Web.OnNavigationCompleted(wrong);
        Assert.Empty(sent);
        Start(page);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        w.Web.OnNavigationCompleted(page, success: false);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Empty(sent);
        Start(page);
        w.Web.OnWebMessage("\"{\\\"type\\\":\\\"ready\\\"}\"");
        Assert.Empty(sent);
        Assert.False(w.IsReady);
        w.Web.OnNavigationCompleted(page);
        Assert.Equal(new[] { "init", "manifest", "fullscreen" }, sent.Select(x => (string?)x["type"]));
        Assert.True(w.IsReady);
        w.Close();
    });

    [Fact]
    public void GoonBoot_ConfirmedReadySendsInit() => Run((_, _) =>
    {
        var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
        w.Load(GameWindow.Server()); w.Show();
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Web.OnNavigationCompleted(w.PageUrl!);
        w.Web.OnWebMessage("{\"type\":\"ready\"}");
        Assert.Equal(new[] { "init", "manifest", "fullscreen" }, sent.Select(x => (string?)x["type"]));
        w.Close();
    });

    [Fact]
    public void Goon_OpenPrimeReachesThePremiumGate() => Run((_, _) =>
    {
        var (oldPremium, oldDenied) = (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler);
        var denied = new List<string>();
        try
        {
            CoreEntitlement.HasPremiumProvider = () => false;
            CoreEntitlement.ShowDeniedHandler = v => denied.Add(v.Feature);
            var w = new GameWindow(new GameWindow.Game("goon", "launcher_game_goon_title", "goon/index.html"));
            w.Load(GameWindow.Server()); w.Show();
            w.HandleMessage("{\"type\":\"open-prime\"}");
            Assert.Equal(new[] { Loc.Get("launcher_game_goon_title") }, denied);
            w.Close();
        }
        finally { (CoreEntitlement.HasPremiumProvider, CoreEntitlement.ShowDeniedHandler) = (oldPremium, oldDenied); }
    });
}
