using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Prize Counter on the shared GameWindow (WPF PrizeCounterHostService).</summary>
public sealed partial class GameWindowTests
{
    private sealed class ShopWire : HttpMessageHandler
    {
        internal readonly List<string> Urls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            lock (Urls) Urls.Add(r.RequestUri!.ToString() + " " + string.Join(",", r.Headers.GetValues("X-Auth-Token")));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(
                "{\"ok\":true,\"tabs\":[{\"id\":\"rain-clothes\"},{\"id\":\"kart\",\"shelves\":[{\"rows\":[{\"id\":\"rain.x\"},{\"id\":\"kart.y\"}]}]}]}") });
        }
    }

    private static void RunCounter(Action<ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow, ShopWire, List<JObject>> body) => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var (oldHttp, oldBase, oldSignIn, oldSecret, oldProbe) = (GameWindow.CounterHttp, GameWindow.CounterServerBase, GameWindow.CounterSignIn, CoreSecrets.RetrieveProvider, UiMotionPreference.OsProbe);
        var (oldId, oldKey, oldEnabled) = (s.UnifiedId, s.PanicKey, s.PanicKeyEnabled);
        var wire = new ShopWire();
        launcher.Hide();
        try
        {
            GameWindow.CounterHttp = () => new HttpClient(wire);   // no network in tests
            GameWindow.CounterServerBase = () => "http://shop.test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;   // P02: never the keyring
            GameWindow.CounterSignIn = _ => throw new InvalidOperationException("no dialog in this test");
            body(shell, wire, new List<JObject>());
        }
        finally
        {
            (GameWindow.CounterHttp, GameWindow.CounterServerBase, GameWindow.CounterSignIn, CoreSecrets.RetrieveProvider, UiMotionPreference.OsProbe) = (oldHttp, oldBase, oldSignIn, oldSecret, oldProbe);
            UiMotionPreference.CacheStamp = long.MinValue;   // drop a cached probe from this test
            (s.UnifiedId, s.PanicKey, s.PanicKeyEnabled) = (oldId, oldKey, oldEnabled);
        }
    });

    private static JObject AwaitReply(List<JObject> sent, string reqId)
    {
        for (var until = DateTime.UtcNow.AddSeconds(10); DateTime.UtcNow < until; Thread.Sleep(10), Dispatcher.UIThread.RunJobs())
            if (sent.FirstOrDefault(o => (string?)o["type"] == "reply" && (string?)o["reqId"] == reqId) is { } r) return (JObject)r["body"]!;
        throw new TimeoutException("no reply for " + reqId);
    }

    private static GameWindow OpenCounter(List<JObject> sent, Action<string>? onClosed = null, string? tab = null)
    {
        var w = GameWindow.LaunchPrizeCounter("glassroad", tab, onClosed)!;
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Activate();
        Dispatcher.UIThread.RunJobs();   // IsActive lands on the next frame
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        return w;
    }

    /// <summary>WPF LauncherWindow WalletChip_Click (:250): the signed-in wallet opens the counter over the launcher
    /// (door "launcher"), the launcher stays up, a second click brings the same window forward, and panic closes it.</summary>
    [Fact]
    public void PrizeCounter_LauncherWalletOpensIt() => Run((shell, launcher) =>
    {
        var s = CoreSettings.Current;
        var (oldHttp, oldBase, oldSecret, oldIn, oldId, oldSp) = (GameWindow.CounterHttp, GameWindow.CounterServerBase, CoreSecrets.RetrieveProvider, CoreAccount.IsLoggedInProvider, s.UnifiedId, s.SkillPoints);
        try
        {
            GameWindow.CounterHttp = () => new HttpClient(new ShopWire());   // no network in tests
            GameWindow.CounterServerBase = () => "http://shop.test";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;   // P02: never the keyring
            CoreAccount.IsLoggedInProvider = () => true;
            (s.UnifiedId, s.SkillPoints) = ("acct-1", 123);
            launcher.RefreshAccount();
            var chip = launcher.FindControl<global::Avalonia.Controls.Button>("SpChip")!;
            Assert.True(chip.IsVisible);
            chip.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            var w = GameWindow.LaunchPrizeCounter("", null)!;   // the open window, brought forward; a blank door keeps the source
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
            Assert.Equal("launcher", (string?)sent.Single(o => (string?)o["type"] == "init")["from"]);
            Assert.True(launcher.IsVisible);
            chip.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
            Assert.Same(w, GameWindow.LaunchPrizeCounter("", null));
            GameWindow.CloseAllForPanic();
            Assert.False(w.IsVisible);
            Assert.True(launcher.IsVisible);
        }
        finally
        {
            (GameWindow.CounterHttp, GameWindow.CounterServerBase, CoreSecrets.RetrieveProvider, CoreAccount.IsLoggedInProvider) = (oldHttp, oldBase, oldSecret, oldIn);
            (s.UnifiedId, s.SkillPoints) = (oldId, oldSp);
        }
    });

    /// <summary>WPF BuildInit / OnPageMessage: init never carries the token; a state read goes out with the door and
    /// comes back with Rain hidden (RainV2Gate off); a Rain buy is refused and never relayed.</summary>
    [Fact]
    public void PrizeCounter_InitRelayAndRainGate() => RunCounter((_, wire, sent) =>
    {
        CoreSettings.Current.UnifiedId = "acct-1";
        var w = OpenCounter(sent, tab: "kart");
        var init = sent.Single(o => (string?)o["type"] == "init");
        Assert.Equal("acct-1", (string?)init["identity"]!["unifiedId"]);
        Assert.Equal(("kart", "glassroad"), ((string?)init["tab"], (string?)init["from"]));
        Assert.Equal("Prize Counter", (string?)init["lexicon"]!["shop_title"]);
        Assert.Empty((JArray)init["wears"]!); Assert.False((bool)init["reducedMotion"]!);
        Assert.DoesNotContain("tok", init.ToString());

        w.HandleMessage("{\"type\":\"request\",\"reqId\":\"r1\",\"op\":\"shop-state\"}");
        var state = AwaitReply(sent, "r1");
        Assert.Equal("http://shop.test/v2/shop/state?unified_id=acct-1 tok", Assert.Single(wire.Urls));
        var tab = Assert.Single((JArray)state["tabs"]!);                   // the Rain tab gone, its row a hole
        Assert.Equal(("kart", JTokenType.Null), ((string?)tab["id"], tab["shelves"]![0]!["rows"]![0]!.Type));

        w.HandleMessage("{\"type\":\"request\",\"reqId\":\"r2\",\"op\":\"shop-buy\",\"idem\":\"i1\",\"body\":{\"itemId\":\"rain.x\"}}");
        Assert.Equal("unavailable", (string?)AwaitReply(sent, "r2")["reason"]);
        Assert.Single(wire.Urls);

        Assert.Same(w, GameWindow.LaunchPrizeCounter("", "chess"));   // another tab reloads; a blank door keeps the source (WPF :320)
        UiMotionPreference.OsProbe = () => false; UiMotionPreference.CacheStamp = long.MinValue;   // OS animations off (WPF :535)
        sent.Clear(); w.HandleMessage("{\"type\":\"ready\"}");
        var again = sent.Single(o => (string?)o["type"] == "init");
        Assert.Equal(("chess", "glassroad", true), ((string?)again["tab"], (string?)again["from"], (bool)again["reducedMotion"]!));
    });

    /// <summary>WPF SignInFromPage: signed out answers signed_out; signin opens the sign-in, then pins and reloads.</summary>
    [Fact]
    public void PrizeCounter_SignInFromThePage_PinsTheAccountAndReloads() => RunCounter((_, wire, sent) =>
    {
        CoreSettings.Current.UnifiedId = null;
        var w = OpenCounter(sent);
        w.HandleMessage("{\"type\":\"request\",\"reqId\":\"r1\",\"op\":\"shop-state\"}");
        Assert.Equal("signed_out", (string?)AwaitReply(sent, "r1")["reason"]);
        Assert.Empty(wire.Urls);

        int dialogs = 0;
        GameWindow.CounterSignIn = _ => { dialogs++; CoreSettings.Current.UnifiedId = "acct-2"; return Task.CompletedTask; };
        w.HandleMessage("{\"type\":\"signin\"}"); Dispatcher.UIThread.RunJobs();
        Assert.Equal((1, false), (dialogs, w.IsReady));                      // the page loads again
        sent.Clear();
        w.HandleMessage("{\"type\":\"ready\"}");
        Assert.Equal("acct-2", (string?)sent.Single(o => (string?)o["type"] == "init")["identity"]!["unifiedId"]);
        w.HandleMessage("{\"type\":\"request\",\"reqId\":\"r2\",\"op\":\"shop-state\"}");
        Assert.True((bool)AwaitReply(sent, "r2")["ok"]!);
        Assert.Equal("http://shop.test/v2/shop/state?unified_id=acct-2 tok", Assert.Single(wire.Urls));
    });

    /// <summary>WPF CloseFor / NotifyOpeners: each opener hears back, panic or app-exit once; a throwing one starves none.</summary>
    [Fact]
    public void PrizeCounter_OpenersHearBackPanicAndAppExit() => RunCounter((shell, _, sent) =>
    {
        var heard = new List<string>();
        OpenCounter(sent, r => heard.Add("a:" + r));
        GameWindow.LaunchPrizeCounter("piecebypiece", null, _ => throw new InvalidOperationException("bad opener"));
        GameWindow.LaunchPrizeCounter("piecebypiece", null, r => heard.Add("b:" + r));
        GameWindow.OpenForTests(GameWindow.PrizeCounterId)!.HandleMessage("{\"type\":\"close\",\"reason\":\"back\"}"); Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "a:back", "b:back" }, heard);
        heard.Clear();
        OpenCounter(sent, r => heard.Add(r));
        GameWindow.CloseAllForPanic(); Dispatcher.UIThread.RunJobs();
        OpenCounter(sent, r => heard.Add(r));
        GameWindow.CloseCounterForExit(); Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "panic", "app-exit" }, heard);
        Assert.False(GameWindow.IsAnyOpen());
    });

    /// <summary>WPF TryRacePauseOnEscape: a first Escape is the counter's back, a second inside 2 s a full panic.</summary>
    [Fact]
    public void PrizeCounter_EscapeIsItsBack_ASecondEscapeIsAPanic() => RunCounter((shell, _, sent) =>
    {
        var s = CoreSettings.Current;
        (s.PanicKey, s.PanicKeyEnabled) = ("Escape", true);
        var heard = new List<string>();
        var w = OpenCounter(sent, r => heard.Add(r));
        Assert.Same(w, GameWindow.EscapePauseInFront());
        sent.Clear();
        var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
        shell.HandlePanicKeyPress(t0); Dispatcher.UIThread.RunJobs();
        Assert.Equal(("kept-escape", true), ((string?)Assert.Single(sent)["type"], GameWindow.IsAnyOpen()));
        shell.HandlePanicKeyPress(t0.AddSeconds(1)); Dispatcher.UIThread.RunJobs();
        Assert.Equal((false, "panic"), (GameWindow.IsAnyOpen(), Assert.Single(heard)));
    });
}
