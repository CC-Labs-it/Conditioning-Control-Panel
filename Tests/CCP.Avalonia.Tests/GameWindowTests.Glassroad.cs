using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Glassroad host (WPF GlassroadHostService + GlassroadHostTests, feat/glassroad-a): the hidden door,
/// the v2 init frame at load + first message + ready, the relay's signing rule and its path gate, the kept Escape and
/// panic.</summary>
public sealed partial class GameWindowTests
{
    private const string GrBase = "http://race.test";
    private const string GrRace = GlassroadHostRules.AllowedPathPrefix;

    private sealed class GrHttp : System.Net.Http.HttpMessageHandler
    {
        public readonly List<(string Method, string Url, string Body, string? Token)> Seen = new();
        protected override async System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            var body = request.Content == null ? "" : await request.Content.ReadAsStringAsync(ct);
            Seen.Add((request.Method.Method, request.RequestUri!.ToString(), body,
                request.Headers.TryGetValues("X-Auth-Token", out var t) ? t.Single() : null));
            return new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new System.Net.Http.StringContent("{\"ok\":1}") };
        }
    }

    /// <summary>Signed in, no Racing Thoughts track, the door at <paramref name="door"/>, the relay on a fake transport.</summary>
    private static void RunGlassroad(string? door, Action<MainShellWindow, LauncherWindow, GrHttp> body, bool lab = true) => Run((shell, launcher) =>
    {
        var (oldDoor, oldOwns, oldHttp, oldBase) = (GameWindow.GlassroadDoorEnv, RacingAccess.IsGrantedProvider, GameWindow.PbpHttpClient, GameWindow.PbpServerBase);
        var (oldUid, oldSecret, oldGate) = (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, MainShellWindow.LeashGateDueProvider);
        var oldLab = CoreEntitlement.HasLabProvider;
        var fake = new GrHttp();
        using var http = new System.Net.Http.HttpClient(fake);
        try
        {
            CoreEntitlement.HasLabProvider = () => lab;
            GameWindow.GlassroadDoorEnv = () => door;
            RacingAccess.IsGrantedProvider = _ => false;
            GameWindow.PbpHttpClient = () => http;
            GameWindow.PbpServerBase = () => GrBase;
            CoreAccount.IsLoggedInProvider = () => true;
            CoreAccount.UnifiedUserId = "discord:12";
            CoreSecrets.RetrieveProvider = n => n == CoreSecrets.AuthToken ? "tok" : null;
            MainShellWindow.LeashGateDueProvider = () => false;
            launcher.Hide();
            body(shell, launcher, fake);
        }
        finally
        {
            CoreEntitlement.HasLabProvider = oldLab;
            (GameWindow.GlassroadDoorEnv, RacingAccess.IsGrantedProvider, GameWindow.PbpHttpClient, GameWindow.PbpServerBase) = (oldDoor, oldOwns, oldHttp, oldBase);
            (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, MainShellWindow.LeashGateDueProvider) = (oldUid, oldSecret, oldGate);
        }
    });

    [Theory]
    [InlineData(false, null)]
    [InlineData(false, "1")]
    [InlineData(true, null)]
    [InlineData(true, "1")]
    public void PlayRaceOpensGlassroadAndRequiresLab(bool lab, string? preview)
        => RunGlassroad(preview, (shell, _, _) =>
        {
            CoreEntitlement.HasPremiumProvider = () => true; // Tier 1 alone must still be refused.
            var denied = new List<TierVerdict>();
            var oldDenied = CoreEntitlement.ShowDeniedHandler;
            CoreEntitlement.ShowDeniedHandler = v => denied.Add(v);
            try
            {
                shell.ShowTab("play");
                shell.Named<global::Avalonia.Controls.Control>("PlayTab")!
                    .FindControl<global::Avalonia.Controls.Button>("BtnPlayRacingThoughts")!
                    .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
                Dispatcher.UIThread.RunJobs();
                if (!lab)
                {
                    Assert.Empty(GameWindow.OpenWindowsForTests());
                    Assert.False(Assert.Single(denied).Allowed);
                    return;
                }
                Assert.Empty(denied);
                var w = Assert.Single(GameWindow.OpenWindowsForTests());
                Assert.Equal(GameWindow.GlassroadId, w.Spec.Id);
                Assert.EndsWith("/glassroad/index.html", w.PageUrl!.AbsolutePath);
                PanicSurfaces.All.Single(p => p.Id == "games").Stop(shell);
                Assert.False(GameWindow.IsAnyOpen());
            }
            finally { CoreEntitlement.ShowDeniedHandler = oldDenied; }
        }, lab);

    // ---- pure rules ----

    [Theory]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    public void Glassroad_TheDoor_OpensOnlyOnOne(string? env, bool open) => Assert.Equal(open, GlassroadHostRules.DoorOpen(env));

    [Theory]
    [InlineData("/v2/race/open", true)]
    [InlineData("/v2/race/link/AbCdEfGhIjKlMnOpQrStUv", true)]
    [InlineData("/v2/race/access?unified_id=discord%3A1234", true)]
    [InlineData("/v2/race/../user/me", false)]
    [InlineData("/v2/race//evil.example/x", false)]   // the shared shapes: WPF HostNetPathRuleTests
    [InlineData("/v2/racex/open", false)]
    [InlineData("/v2/pbp/lobby", false)]
    public void Glassroad_PathGate_OnlyRaceRoutesLandOnTheProxy(string path, bool pass)
    {
        Assert.Equal(pass, HostNetPathRule.TryBuild(GrBase, path, GrRace, out var uri));
        if (pass) Assert.Equal("race.test", uri!.Host);
    }

    [Fact]
    public void Glassroad_Init_SignedInAndSignedOut()
    {
        var f = GlassroadHostRules.InitFrame("u_1", "Kit", 7, false, 0.75, 0.5);
        Assert.Equal("init", (string?)f["type"]);
        Assert.Equal(2, (int)f["v"]!);
        Assert.Equal("desktop", (string?)f["caps"]!["platform"]);
        Assert.True((bool)f["caps"]!["net"]!);
        Assert.Empty((JArray)f["caps"]!["songs"]!);
        Assert.Equal("u_1", (string?)f["identity"]!["unifiedId"]);
        Assert.Equal(2, (int)f["identity"]!["tier"]!);   // clamped
        Assert.Equal(600, (int)f["gentle"]!["fadeMs"]!);
        Assert.Equal("Player", (string?)GlassroadHostRules.InitFrame("u_1", " ", 0, false, 1, 1)["identity"]!["displayName"]);
        var o = GlassroadHostRules.InitFrame(null, "Kit", 2, true, 1.5, 0.25);
        Assert.Equal(JTokenType.Null, o["identity"]!.Type);
        Assert.True((bool)o["caps"]!["net"]!);
        Assert.Equal(1500, (int)o["gentle"]!["fadeMs"]!);
        Assert.Equal(GentleVisuals.RiseFloorMs, (int)o["gentle"]!["riseMs"]!);
        Assert.Equal(0.25, (double)o["gentle"]!["speed"]!);
    }

    [Fact]
    public void Glassroad_Signing_PostBodyAndGetQuery()
    {
        Assert.True(GlassroadHostRules.TrySign("POST", "/v2/race/link/claim", "{\"token\":\"abc\"}", "u_1", out var p, out var b));
        Assert.Equal("/v2/race/link/claim", p);
        Assert.Equal("{\"token\":\"abc\",\"unified_id\":\"u_1\"}", b);
        Assert.True(GlassroadHostRules.TrySign("POST", "/v2/race/link", "", "u_1", out _, out var empty));
        Assert.Equal("{\"unified_id\":\"u_1\"}", empty);
        Assert.True(GlassroadHostRules.TrySign("GET", "/v2/race/open", "", "discord:12", out var g, out _));
        Assert.Equal("/v2/race/open?unified_id=discord%3A12", g);
        Assert.True(GlassroadHostRules.TrySign("GET", "/v2/race/board/t?x=1", "", "u_1", out var q, out _));
        Assert.Equal("/v2/race/board/t?x=1&unified_id=u_1", q);
        Assert.True(GlassroadHostRules.TrySign("GET", "/v2/race/link/AbCdEfGhIjKlMnOpQrStUv", "", null, out var bare, out _));
        Assert.Equal("/v2/race/link/AbCdEfGhIjKlMnOpQrStUv", bare);   // signed out: sent bare
        Assert.False(GlassroadHostRules.TrySign("POST", "/v2/race/open",
            "{\"x\":\"" + new string('a', GlassroadHostRules.MaxBodyBytes) + "\"}", "u_1", out _, out _));
    }

    [Theory]
    [InlineData("POST", "/v2/race/open", "{\"unified_id\":\"u_other\"}")]
    [InlineData("POST", "/v2/race/open", "{\"Unified_Id\":\"u_other\"}")]
    [InlineData("GET", "/v2/race/open?unified_id=u_other", "")]
    [InlineData("POST", "/v2/race/result", "{}")]
    [InlineData("POST", "/v2/race/result/x", "{}")]
    [InlineData("POST", "/v2/race/open", "[1,2]")]
    [InlineData("POST", "/v2/race/open", "{nope")]
    [InlineData("GET", "/v2/pbp/lobby", "")]
    [InlineData("GET", "", "")]
    public void Glassroad_Signing_Refusals(string method, string path, string body)
        => Assert.False(GlassroadHostRules.TrySign(method, path, body, "u_1", out _, out _));

    // ---- the window ----

    /// <summary>Door shut: the page cannot be opened and the race tile stays hidden without a track. Door open: the
    /// Racing Thoughts tile shows, Play opens Glassroad (not dtrh/race.html), and panic closes it.</summary>
    [Fact]
    public void Glassroad_DoorHidesIt_TheRaceTileOpensIt_PanicClosesIt()
    {
        RunGlassroad(null, (_, _, _) =>
        {
            Assert.Equal("race", GameWindow.ResolveDoor("race"));
            Assert.Null(GameWindow.Launch(GameWindow.GlassroadId));
            Assert.DoesNotContain(LauncherWindow.VisibleCards, c => c.Id == "race");
            Assert.False(GameWindow.IsAnyOpen());
        }, lab: false);
        RunGlassroad("1", (shell, launcher, _) =>
        {
            Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "race");
            launcher.Play(LauncherCards.Find("race")!);
            Dispatcher.UIThread.RunJobs();
            var w = Assert.Single(GameWindow.OpenWindowsForTests());
            Assert.Equal(GameWindow.GlassroadId, w.Spec.Id);
            Assert.EndsWith("/glassroad/index.html", w.PageUrl!.AbsolutePath);
            Assert.Same(w, GameWindow.Launch("race"));                 // one live window, focused
            PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        });
    }

    /// <summary>Init goes out at load, on the first message and at ready; the relay signs as the host's player, sends
    /// only /v2/race/ (judged signed), never the room-signed result; exit and menu-back close; a silent page is pinged
    /// then closed.</summary>
    [Fact]
    public void Glassroad_InitFramesNetRelayExitAndWatchdog() => RunGlassroad("1", (_, _, fake) =>
    {
        var w = GameWindow.Launch(GameWindow.GlassroadId)!;
        Dispatcher.UIThread.RunJobs();
        var sent = new List<JObject>();
        w.Posted += json => sent.Add(JObject.Parse(json));
        w.Web.OnNavigationCompleted(w.PageUrl);
        var init = Assert.Single(sent);
        Assert.Equal("init", (string?)init["type"]);
        Assert.Equal(2, (int)init["v"]!);
        Assert.Equal("discord:12", (string?)init["identity"]!["unifiedId"]);
        w.Web.OnWebMessage("{\"type\":\"heartbeat\"}");               // first message, still booting: init again
        Assert.Equal(2, sent.Count(o => (string?)o["type"] == "init"));
        w.Web.OnWebMessage("{\"type\":\"heartbeat\"}");               // only once
        Assert.Equal(2, sent.Count(o => (string?)o["type"] == "init"));
        w.Web.OnWebMessage("\"{\\\"type\\\":\\\"ready\\\",\\\"protocol\\\":2}\"");
        Assert.True(w.IsReady);
        Assert.Equal(3, sent.Count(o => (string?)o["type"] == "init"));

        w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"a\",\"method\":\"GET\",\"path\":\"/v2/pbp/lobby\"}");
        w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"b\",\"method\":\"POST\",\"path\":\"/v2/race/result\",\"body\":{}}");
        w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"c\",\"method\":\"GET\",\"path\":\"/v2/race/../user/me\"}");
        w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"d\",\"method\":\"DELETE\",\"path\":\"/v2/race/link\"}");
        foreach (var id in new[] { "a", "b", "c", "d" })
        {
            var r = sent.Single(o => (string?)o["id"] == id);
            Assert.Equal("net-reply", (string?)r["type"]);
            Assert.Equal(0, (int)r["status"]!);
        }
        Assert.Empty(fake.Seen);

        w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"e\",\"method\":\"POST\",\"path\":\"/v2/race/link/claim\",\"body\":{\"token\":\"t1\"}}");
        Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();
        w.Web.OnWebMessage("{\"type\":\"net\",\"id\":\"f\",\"method\":\"GET\",\"path\":\"/v2/race/access\",\"body\":null}");
        Assert.True(w.GlassroadPending.Wait(TimeSpan.FromSeconds(10)));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(("POST", GrBase + "/v2/race/link/claim", "{\"token\":\"t1\",\"unified_id\":\"discord:12\"}", (string?)"tok"), fake.Seen[0]);
        Assert.Equal(("GET", GrBase + "/v2/race/access?unified_id=discord%3A12", "", (string?)"tok"), fake.Seen[1]);
        Assert.Equal(200, (int)sent.Single(o => (string?)o["id"] == "e")["status"]!);
        Assert.Equal("{\"ok\":1}", (string?)sent.Single(o => (string?)o["id"] == "f")["body"]);

        var now = DateTime.UtcNow;
        Assert.Equal("ok", w.CheckGlassroadWatch(now));
        Assert.Equal("ping", w.CheckGlassroadWatch(now.AddSeconds(25)));
        Assert.Equal("ping", (string?)sent.Last()["type"]);
        Assert.Equal("closed", w.CheckGlassroadWatch(now.AddSeconds(30)));
        Dispatcher.UIThread.RunJobs();
        Assert.False(GameWindow.IsAnyOpen());

        foreach (var leave in new[] { "exit", "menu-back" })
        {
            var again = GameWindow.Launch(GameWindow.GlassroadId)!;
            Dispatcher.UIThread.RunJobs();
            again.Web.OnNavigationCompleted(again.PageUrl);
            again.Web.OnWebMessage("{\"type\":\"" + leave + "\"}");
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
    });

    /// <summary>Booting: Escape is a full panic that closes it. Up: the first Escape is its pause (kept-escape),
    /// a second inside 2 s is a full panic that closes it.</summary>
    [Fact]
    public void Glassroad_EscapePausesOnceUp_ASecondEscapeClosesIt() => RunGlassroad("1", (shell, _, _) =>
    {
        var s = CoreSettings.Current;
        var (oldKey, oldEnabled) = (s.PanicKey, s.PanicKeyEnabled);
        try
        {
            (s.PanicKey, s.PanicKeyEnabled) = ("Escape", true);
            var t0 = new DateTime(2026, 1, 1, 12, 0, 0);
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            Assert.Same(w, GameWindow.EscapePauseInFront());
            shell.HandlePanicKeyPress(t0);
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());

            w = GameWindow.Launch(GameWindow.GlassroadId)!;
            w.Activate();
            Dispatcher.UIThread.RunJobs();
            w.HandleMessage("{\"type\":\"ready\"}");
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            var t1 = t0.AddSeconds(10);
            shell.HandlePanicKeyPress(t1);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GameWindow.IsAnyOpen());
            Assert.Equal("kept-escape", (string?)Assert.Single(sent)["type"]);
            shell.HandlePanicKeyPress(t1.AddSeconds(1));
            Dispatcher.UIThread.RunJobs();
            Assert.False(GameWindow.IsAnyOpen());
        }
        finally { (s.PanicKey, s.PanicKeyEnabled) = (oldKey, oldEnabled); }
    });
}
