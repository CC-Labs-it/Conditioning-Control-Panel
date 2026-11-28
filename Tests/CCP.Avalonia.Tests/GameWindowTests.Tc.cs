using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Terms &amp; Conditioning host (WPF TcHostService + TcDoorsTests + TcReloadInitTests, main 21899cde85,
/// 876b22252d, f1b7924f4d, 15d5a5e50a): one never-locked tile, the edition in a fresh init on every ready, the frame
/// gate, the 800 ms close handshake, the watchdog, Escape as its pause, Lockdown on the tier door, and panic.</summary>
public sealed partial class GameWindowTests
{
    private static void RunTc(string edition, Action<MainShellWindow, GameWindow, List<JObject>> body) => Run((shell, launcher) =>
    {
        var (oldTree, oldEdition, oldOpen) = (GameWindow.TcTreePresent, GameWindow.TcReadEdition, GameWindow.TcOpenUrl);
        try
        {
            GameWindow.TcTreePresent = () => true;
            GameWindow.TcReadEdition = () => edition;
            launcher.Hide();
            var w = GameWindow.Launch(GameWindow.TcId)!;
            Assert.NotNull(w);
            Dispatcher.UIThread.RunJobs();
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            body(shell, w, sent);
        }
        finally { (GameWindow.TcTreePresent, GameWindow.TcReadEdition, GameWindow.TcOpenUrl) = (oldTree, oldEdition, oldOpen); }
    });

    [Fact]
    public void Tc_TileIsNeverLocked_AndOpensOnlyWithTheTree() => Run((_, launcher) =>
    {
        var old = GameWindow.TcTreePresent;
        try
        {
            Assert.False(LauncherWindow.Destinations[GameWindow.TcId].Locked());
            Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == GameWindow.TcId);
            launcher.Hide();
            GameWindow.TcTreePresent = () => false;
            var card = LauncherCards.FindAny(GameWindow.TcId)!;
            Assert.False(LauncherWindow.Available(card));   // no tree, no tile (WPF LauncherCatalogue.cs:250)
            Assert.Null(GameWindow.Launch(GameWindow.TcId));
            Assert.False(GameWindow.IsAnyOpen());
            GameWindow.TcTreePresent = () => true;
            Assert.True(LauncherWindow.Available(card));
            var w = GameWindow.Launch(GameWindow.TcId)!;
            Assert.EndsWith("/fuzz/index.html", w.PageUrl!.AbsolutePath);
            Assert.Same(w, GameWindow.Launch(GameWindow.TcId));   // one live window, focused rather than relaunched
            Assert.Equal(PanicPolicy.KeptEscapePage.Tc, w.KeptEscapePage);
        }
        finally { GameWindow.TcTreePresent = old; }
    });

    [Theory]
    [InlineData("full")]
    [InlineData("small")]
    public void Tc_EveryReadyGetsAFreshInit(string edition) => RunTc(edition, (_, w, sent) =>
    {
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");
        w.HandleMessage("{\"type\":\"ready\",\"protocol\":1}");   // the pause card's Main menu reloads the page
        Assert.Equal(2, sent.Count(f => (string?)f["type"] == "init"));
        var init = sent.Last();
        Assert.Equal(TcRules.Protocol, (int)init["protocol"]!);
        var caps = (JObject)init["caps"]!;
        Assert.Equal(edition, (string?)caps["edition"]);
        Assert.Equal("desktop", (string?)caps["platform"]);
        Assert.False((bool)caps["saveSlot"]!);
        Assert.Null(caps["expo"]);
        Assert.Null(caps["ownMedia"]);   // this head cannot answer own-media-list yet (s20-tc-media)
        Assert.False((bool)caps["toys"]!);
        Assert.False((bool)caps["sparkles"]!);
        Assert.True(w.IsReady);
        Assert.True(w.KeptEscapeReady);
        Assert.Equal(edition, w.TcEdition);
    });

    [Fact]
    public void Tc_GateRefusesBeforeTheHostActs() => RunTc("small", (_, w, sent) =>
    {
        var opened = new List<string>();
        GameWindow.TcOpenUrl = u => { opened.Add(u); return true; };
        w.HandleMessage("{\"type\":\"open-url\",\"url\":\"https://evil.example/\"}");
        w.HandleMessage("{\"type\":\"open-url\",\"url\":\"https://cclabs.app:8443/x\"}");
        w.HandleMessage("{\"type\":\"close\"}");   // not a page frame here: the shell's close never applies
        w.HandleMessage("{\"type\":\"exit-done\"}");   // nobody asked: stays open
        Assert.Empty(opened);
        Assert.Contains(w, GameWindow.OpenWindowsForTests());
        w.HandleMessage("{\"type\":\"open-url\",\"url\":\"https://cclabs.app/pricing\"}");
        Assert.Equal(new[] { "https://cclabs.app/pricing" }, opened);
        Assert.Empty(sent);
    });

    [Fact]
    public void Tc_ExitEndsOnExitDone() => RunTc("small", (_, w, _) =>
    {
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"exit\",\"reason\":\"back\"}");
        Assert.True(w.TcCloseClockRunning);
        Assert.False(w.KeptEscapeReady);   // on its way out: it no longer keeps an Escape
        w.HandleMessage("{\"type\":\"exit-done\"}");
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.TcCloseClockRunning);
        Assert.False(w.TcWatchRunning);
    });

    [Fact]
    public void Tc_ExitIsForcedAt800ms() => RunTc("small", (_, w, _) =>
    {
        w.HandleMessage("{\"type\":\"exit\"}");
        var now = DateTime.UtcNow;
        Assert.False(w.CheckTcCloseClock(now + TimeSpan.FromMilliseconds(500)));
        Assert.True(GameWindow.IsAnyOpen());
        Assert.True(w.CheckTcCloseClock(now + TcHostRules.CloseForce + TimeSpan.FromMilliseconds(50)));
        Assert.False(GameWindow.IsAnyOpen());
    });

    [Fact]
    public void Tc_WatchdogClosesASilentPage() => RunTc("small", (_, w, _) =>
    {
        var now = DateTime.UtcNow;
        Assert.Equal("ok", w.CheckTcWatch(now + TimeSpan.FromSeconds(30)));
        w.HandleMessage("{\"type\":\"ready\"}");
        now = DateTime.UtcNow;
        Assert.Equal("ok", w.CheckTcWatch(now + TimeSpan.FromSeconds(60)));
        Assert.Equal("closed", w.CheckTcWatch(now + TcHostRules.SilenceLimit + TimeSpan.FromSeconds(1)));
        Assert.False(GameWindow.IsAnyOpen());
        Assert.Equal("idle", w.CheckTcWatch(now + TimeSpan.FromHours(1)));
    });

    [Fact]
    public void Tc_BootWatchdogClosesAPageThatNeverSpoke() => RunTc("small", (_, w, _) =>
    {
        Assert.Equal("closed", w.CheckTcWatch(DateTime.UtcNow + TcHostRules.BootLimit + TimeSpan.FromSeconds(1)));
        Assert.False(GameWindow.IsAnyOpen());
    });

    // Every stop route (panic key, tray, safe word, leash gate) runs PanicSurfaces.StopAll; WPF GameSurfaces.cs:76.
    [Fact]
    public void Tc_PanicClosesItAndItsClocksInOneTick() => RunTc("full", (shell, w, sent) =>
    {
        w.HandleMessage("{\"type\":\"ready\"}");
        w.HandleMessage("{\"type\":\"exit\"}");
        Assert.True(w.TcWatchRunning && w.TcCloseClockRunning);
        PanicSurfaces.StopAll("tc safe word test", shell);
        Assert.False(GameWindow.IsAnyOpen());
        Assert.False(w.TcWatchRunning);
        Assert.False(w.TcCloseClockRunning);
        var last = sent.Last();
        Assert.Equal("suspend", (string?)last["type"]);
        Assert.Equal(TcHostRules.ReasonPanic, (string?)last["reason"]);
        Assert.True((bool)last["on"]!);
        w.HandleMessage("{\"type\":\"ready\"}");   // a late frame after panic reopens nothing
        Assert.False(w.TcWatchRunning);
        Assert.Single(sent, f => (string?)f["type"] == "init");
    });

    [Fact]
    public void Tc_EscapePausesOnceUp_ASecondEscapeClosesIt() => RunTc("small", (shell, w, sent) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled) = (s.PanicKey, s.PanicKeyEnabled);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled) = ("Escape", true);
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"ready\"}");
            sent.Clear();
            Assert.Same(w, GameWindow.EscapePauseInFront());
            var t = new DateTime(2026, 1, 1, 12, 0, 0);
            shell.HandlePanicKeyPress(t);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);
            shell.HandlePanicKeyPress(t.AddSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });

    [Fact]
    public void Tc_NudgeDuringLockdownOpensNoDoorAndHoldsNothing() => RunTc("small", (_, w, sent) =>
    {
        var old = LockdownService.Current;
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            ld.Activate(TimeSpan.FromMinutes(30));
            w.HandleMessage("{\"type\":\"nudge\",\"where\":\"ending\",\"want\":\"upgrade\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent);   // no suspend(focus): the run is never held behind a door that cannot open
            Assert.True(GameWindow.IsAnyOpen());
        }
        finally { ld.Deactivate(); LockdownService.Current = old; }
    });
}
