using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Threading;
using Avalonia.Controls;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Launcher;
using ConditioningControlPanel.Services.PieceByPiece;
using ConditioningControlPanel.Services.Race;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Glassroad host (WPF GlassroadHostService + GlassroadHostTests, feat/glassroad-a): the public 3D door,
/// the v2 init frame at load + first message + ready, the relay's signing rule and its path gate, the kept Escape and
/// panic.</summary>
public sealed partial class GameWindowTests
{
    private const string GrBase = "http://race.test";
    private const double LoadingAged = 121;
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

    /// <summary>Signed in as Prime, no Racing Thoughts track, the relay on a fake transport.</summary>
    private static void RunGlassroad(Action<MainShellWindow, LauncherWindow, GrHttp> body) => RunGlassroad("1", body);

    /// <summary>Signed in, no Racing Thoughts track, the door at <paramref name="door"/>, the relay on a fake transport.</summary>
    private static void RunGlassroad(string? door, Action<MainShellWindow, LauncherWindow, GrHttp> body, bool? lab = null) => Run((shell, launcher) =>
    {
        var (oldDoor, oldOwns, oldHttp, oldBase) = (CoreEntitlement.HasLabProvider, RacingAccess.IsGrantedProvider, GameWindow.PbpHttpClient, GameWindow.PbpServerBase);
        var (oldUid, oldSecret, oldGate) = (CoreAccount.UnifiedUserId, CoreSecrets.RetrieveProvider, MainShellWindow.LeashGateDueProvider);
        var oldLinuxCard = GameWindow.UseLinuxRaceCard;
        var fake = new GrHttp();
        using var http = new System.Net.Http.HttpClient(fake);
        try
        {
            GameWindow.UseLinuxRaceCard = () => false; // exercise the existing WebKit host explicitly
            CoreEntitlement.HasLabProvider = () => lab ?? door == "1"; // an explicit lab wins (Play door tests), else the door string
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
            GameWindow.UseLinuxRaceCard = oldLinuxCard;
            (CoreEntitlement.HasLabProvider, RacingAccess.IsGrantedProvider, GameWindow.PbpHttpClient, GameWindow.PbpServerBase) = (oldDoor, oldOwns, oldHttp, oldBase);
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
    [InlineData("race", "glassroad")]
    [InlineData("RACE", "glassroad")]
    [InlineData("glassroad", "glassroad")]
    [InlineData("breakoutdemo", "breakoutdemo")]
    public void Glassroad_PublicDoorIsAlways3d(string id, string expected)
    {
        Assert.Equal(expected, GameWindow.ResolveDoor(id));
    }

    [Theory]
    [InlineData(-1, true)]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    public void Glassroad_TileLocksBelowPrime(int tier, bool locked)
    {
        CoreEntitlement.HasLabProvider = tier < 0 ? null : () => tier == 2;
        Assert.Equal(locked, LauncherWindow.Destinations["race"].Locked());
        Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "race");
    }

    [Theory]
    [InlineData("race", -1)]
    [InlineData("race", 0)]
    [InlineData("race", 1)]
    [InlineData("race", 2)]
    [InlineData("glassroad", -1)]
    [InlineData("glassroad", 0)]
    [InlineData("glassroad", 1)]
    [InlineData("glassroad", 2)]
    public void Glassroad_PrimeIsRequiredBeforeOpeningOrRefocusing(string door, int tier) => RunGlassroad((_, _, _) =>
    {
        CoreEntitlement.HasLabProvider = tier < 0 ? null : () => tier == 2;
        CoreEntitlement.HasPremiumProvider = () => tier >= 1;
        CoreEntitlement.IsFreeTodayProvider = _ => true; // the race has no daily-free bypass
        var denied = new List<TierVerdict>();
        CoreEntitlement.ShowDeniedHandler = denied.Add;
        if (tier != 2)
        {
            Assert.Null(GameWindow.Launch(door));
            Assert.False(GameWindow.IsAnyOpen());
        }
        else
        {
            var w = GameWindow.Launch(door);
            Assert.NotNull(w);
            Assert.Equal(GameWindow.GlassroadId, w.Spec.Id);
            Assert.Same(w, GameWindow.Launch(door));
            Assert.Single(GameWindow.OpenWindowsForTests());
            Assert.Empty(denied);
            CoreEntitlement.HasLabProvider = () => false;
            Assert.Null(GameWindow.Launch(door)); // an existing window must not bypass the gate
            Assert.Same(w, Assert.Single(GameWindow.OpenWindowsForTests()));
        }
        var refusal = Assert.Single(denied);
        Assert.Equal(ConditioningControlPanel.Models.PatreonTier.Level2, refusal.Required);
        Assert.Equal(Loc.Get("launcher_game_race_title"), refusal.Feature);
    });

    /// <summary>WPF LauncherHost.LaunchGame (:335-375): a launch the gate refused reports false, so the
    /// companion's open action does not claim it opened the race (restack b128, from #4065).</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Glassroad_LauncherLaunchGame_ReportsWhetherTheRaceOpened(bool lab) => RunGlassroad("1", (shell, _, _) =>
    {
        var denied = new List<TierVerdict>();
        var oldDenied = CoreEntitlement.ShowDeniedHandler;
        CoreEntitlement.ShowDeniedHandler = denied.Add;
        try
        {
            CoreEntitlement.HasLabProvider = () => lab;
            Assert.Equal(lab, LauncherWindow.LaunchGame(shell, "race"));
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(lab, GameWindow.IsAnyOpen());
            Assert.Equal(lab ? 0 : 1, denied.Count);
        }
        finally { CoreEntitlement.ShowDeniedHandler = oldDenied; }
    }, lab: lab);

    [Fact]
    public void Glassroad_RaceTileIsVisibleWithoutPurchase()
    {
        RacingAccess.IsGrantedProvider = _ => false;
        Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "race");
        Assert.True(LauncherWindow.CanStartGame("race"));
    }

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

    /// <summary>WPF 7.2.2 GlassroadHostTests (#1543): a page building a course is waited for, up to 90 s.</summary>
    [Fact]
    public void Glassroad_Watch_ABusyPageIsWaitedFor()
    {
        static TimeSpan S(double s) => TimeSpan.FromSeconds(s);
        Assert.True(GlassroadHostRules.IsBusy(S(10)));
        Assert.False(GlassroadHostRules.IsBusy(null));
        Assert.False(GlassroadHostRules.IsBusy(GlassroadHostRules.LoadingWindow));
        Assert.Equal(PbpHostRules.WatchStep.Idle, GlassroadHostRules.Watch(false, S(40), S(40), false, busy: false));
        Assert.Equal(PbpHostRules.WatchStep.Close, GlassroadHostRules.Watch(false, S(46), S(46), false, busy: false));
        Assert.Equal(PbpHostRules.WatchStep.Idle, GlassroadHostRules.Watch(false, S(60), S(60), false, busy: true));
        Assert.Equal(PbpHostRules.WatchStep.Close, GlassroadHostRules.Watch(false, S(91), S(91), false, busy: true));
        Assert.Equal(PbpHostRules.WatchStep.Ok, GlassroadHostRules.Watch(true, S(1), S(19), false, busy: false));
        Assert.Equal(PbpHostRules.WatchStep.Ping, GlassroadHostRules.Watch(true, S(25), S(25), false, busy: false));
        Assert.Equal(PbpHostRules.WatchStep.Close, GlassroadHostRules.Watch(true, S(30), S(30), true, busy: false));
        Assert.Equal(PbpHostRules.WatchStep.Ok, GlassroadHostRules.Watch(true, S(60), S(60), true, busy: true));
        Assert.Equal(PbpHostRules.WatchStep.Close, GlassroadHostRules.Watch(true, S(95), S(95), true, busy: true));
    }

    /// <summary>B2 (owner 2026-10-29): the Linux race host says <c>caps.scrolller: false</c> outright, so the page
    /// offers My files / Off (boot.js reads <c>init.caps.scrolller ? feed : null</c>); Windows keeps the WPF caps.</summary>
    [Fact]
    public void Glassroad_Init_LinuxHostOffersNoRemotePictures()
    {
        var old = GlassroadHostRules.LinuxHostOverride;
        try
        {
            GlassroadHostRules.LinuxHostOverride = true;
            Assert.False(GlassroadHostRules.RemotePicturesOffered);
            var caps = GlassroadHostRules.InitFrame("u_1", "Kit", 2, false, 0.75, 0.5, picturesOn: true)["caps"]!;
            Assert.Equal(JTokenType.Boolean, caps["scrolller"]!.Type);
            Assert.False((bool)caps["scrolller"]!);
            GlassroadHostRules.LinuxHostOverride = false;
            Assert.True(GlassroadHostRules.RemotePicturesOffered);
            caps = GlassroadHostRules.InitFrame("u_1", "Kit", 2, false, 0.75, 0.5, picturesOn: true)["caps"]!;
            Assert.True((bool)caps["scrolller"]!["consent"]!);
            Assert.Equal("device", (string?)caps["scrolller"]!["origin"]);
            Assert.False((bool)GlassroadHostRules.InitFrame(null, null, 0, false, 1, 1)["caps"]!["scrolller"]!["consent"]!);
        }
        finally { GlassroadHostRules.LinuxHostOverride = old; }
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

    [Fact]
    public void Glassroad_OwnershipAndToys_KeepShopClosed()
    {
        var grants = new[] { "cos.kart.body.petal", "cos.other", "cos.kart.body.petal", "cos.kart." + new string('x', 65) };
        var init = GlassroadHostRules.InitFrame("u", "Kit", 2, false, 1, 1, grants: grants, toysConnected: true);
        Assert.Equal(new[] { "cos.kart.body.petal" }, init["identity"]!["grants"]!.Values<string>());
        Assert.True((bool)init["caps"]!["toysConnected"]!);
        Assert.False((bool)init["caps"]!["prizeCounter"]!);
        Assert.Equal("owned", (string?)GlassroadHostRules.OwnedFrame(grants)["type"]);
        Assert.Equal(64, GlassroadHostRules.KartGrants(Enumerable.Range(0, 100).Select(i => "cos.kart." + i)).Count);
        Assert.Equal(JTokenType.Null, GlassroadHostRules.InitFrame(null, "Kit", 0, false, 1, 1, grants: grants)["identity"]!.Type);
    }

    [Fact]
    public void Glassroad_InviteNamesKnownCourseOnly()
    {
        var item = new ConditioningControlPanel.Services.Friends.InboxItem("a",
            ConditioningControlPanel.Services.Friends.SendKind.Invite, "u", "Kit", null, null,
            ConditioningControlPanel.Services.Friends.InviteDestination.Race, null, null,
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddMinutes(1)) { Track = "keep" };
        Assert.Contains("The Keep", ConditioningControlPanel.Avalonia.Platform.FriendsLanding.KnockLine(item));
        item = item with { Track = "unknown" };
        Assert.DoesNotContain("unknown", ConditioningControlPanel.Avalonia.Platform.FriendsLanding.KnockLine(item));
    }

    [Fact]
    public void Glassroad_OneDayEventPerWindow_PanicStopsRaceBed() => RunGlassroad((_, _, _) =>
    {
        var oldNote = GameWindow.GlassroadNoteEvent;
        var oldHaptics = CoreHaptics.Service;
        var oldPremium = CoreEntitlement.HasPremiumProvider;
        using var haptics = new HapticService(new ConditioningControlPanel.Models.HapticSettings { Enabled = true });
        var notes = new List<string>();
        try
        {
            GameWindow.GlassroadNoteEvent = notes.Add;
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreHaptics.Service = haptics;
            GameWindow.Launch(GameWindow.GlassroadId);
            GameWindow.Launch(GameWindow.GlassroadId);
            Assert.Equal(new[] { "e_race3d" }, notes);
            haptics.SetLayer(ConditioningControlPanel.Services.Haptics.Core.HapticLayer.Race, 0.8);
            Assert.Equal(0.8, haptics.GetLayer(ConditioningControlPanel.Services.Haptics.Core.HapticLayer.Race));
            GameWindow.CloseAllForPanic();
            Assert.Equal(0, haptics.GetLayer(ConditioningControlPanel.Services.Haptics.Core.HapticLayer.Race));
            Assert.Null(GameWindow.OpenForTests(GameWindow.GlassroadId));
            GameWindow.Launch(GameWindow.GlassroadId);
            Assert.Equal(2, notes.Count);
        }
        finally
        {
            GameWindow.CloseAllForPanic();
            GameWindow.GlassroadNoteEvent = oldNote;
            CoreHaptics.Service = oldHaptics;
            CoreEntitlement.HasPremiumProvider = oldPremium;
        }
    });

    private sealed class GrProfileHttp : System.Net.Http.HttpMessageHandler
    {
        public string Account = "discord:12", Grant = "cos.kart.body.petal";
        public long Revision = 4;
        public bool ProfileGrants = true;
        protected override System.Threading.Tasks.Task<System.Net.Http.HttpResponseMessage> SendAsync(
            System.Net.Http.HttpRequestMessage request, System.Threading.CancellationToken ct)
        {
            var prizes = new JObject { ["revision"] = Revision, ["grants"] = new JArray(Grant) };
            var body = request.RequestUri!.AbsolutePath switch
            {
                "/v2/user/profile" => new JObject { ["user"] = new JObject { ["unified_id"] = Account, ["prizes"] = ProfileGrants ? prizes : null } },
                "/v2/user/sync" => new JObject { ["success"] = true, ["prizes"] = prizes },
                _ => throw new InvalidOperationException("Unexpected request: " + request.RequestUri.AbsolutePath),
            };
            return System.Threading.Tasks.Task.FromResult(new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
                { Content = new System.Net.Http.StringContent(body.ToString()) });
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Glassroad_ExistingServerGrantsLoadWithoutPurchase_UpdateAndClear(bool profileGrants) => RunGlassroad((_, _, _) =>
    {
        var s = CoreSettings.Current;
        var oldSync = ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync;
        var oldAccount = CoreAccount.UnifiedUserId;
        using var wire = new GrProfileHttp { ProfileGrants = profileGrants };
        var v2 = new V2AuthService(() => s, wire);
        // The production construction: NewSync seeds the sync push with PrizeOwnership.Feed.
        var sync = ConditioningControlPanel.Avalonia.Platform.AccountSeed.NewSync(() => Array.Empty<string>(), () => false, wire);
        try
        {
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync = profileGrants ? null : sync;
            s.UnifiedId = CoreAccount.UnifiedUserId = wire.Account;
            s.AuthToken = "tok";
            s.OfflineMode = false;
            ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.ResetHeld();
            Assert.True(ConditioningControlPanel.Avalonia.Platform.AccountSeed.LoadProfileAsync(v2).GetAwaiter().GetResult());
            sync.StopHeartbeat();
            Dispatcher.UIThread.RunJobs();   // PrizesFeed applies on the UI queue
            var w = GameWindow.Launch(GameWindow.GlassroadId)!;
            var sent = new List<JObject>();
            w.Posted += json => sent.Add(JObject.Parse(json));
            w.PostGlassroadInit();
            Assert.Contains(wire.Grant, sent.Last()["identity"]!["grants"]!.Values<string>());
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync = sync;
            // Same profile revision, then a newer sync snapshot from a different device.
            sync.MarkLoaded(null);
            Assert.True(ConditioningControlPanel.Avalonia.Platform.AccountSeed.LoadProfileAsync(v2).GetAwaiter().GetResult());
            sync.StopHeartbeat();
            wire.Grant = "cos.kart.body.trinket";
            wire.Revision++;
            sync.UtcNow = () => DateTime.UtcNow.AddMinutes(2);
            Assert.True(sync.PushAsync("ownership refresh").GetAwaiter().GetResult());
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(wire.Grant, sent.Last(o => (string?)o["type"] == "owned")["grants"]!.Values<string>());
            foreach (var (answer, revision) in new[] { ("wrong", 999L), (wire.Account, 1L) })
                ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Feed.Apply(wire.Account, answer,
                    new ConditioningControlPanel.Models.PrizesBlock { Revision = revision, Grants = new() { "cos.kart.body.old" } }, "stale");
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new[] { wire.Grant }, ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Grants);
            s.UnifiedId = CoreAccount.UnifiedUserId = "other";
            ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Feed.NoteAccount("other");
            ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Feed.Apply(wire.Account, wire.Account,
                new ConditioningControlPanel.Models.PrizesBlock { Revision = 999, Grants = new() { wire.Grant } }, "late");
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Grants);
            s.UnifiedId = CoreAccount.UnifiedUserId = wire.Account;
            ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Feed.NoteAccount(wire.Account);
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Grants);
            Assert.True(ConditioningControlPanel.Avalonia.Platform.AccountSeed.LoadProfileAsync(v2).GetAwaiter().GetResult());
            Dispatcher.UIThread.RunJobs();
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Logout(accountDeleted: true).GetAwaiter().GetResult();
            Dispatcher.UIThread.RunJobs();
            Assert.Empty(sent.Last(o => (string?)o["type"] == "owned")["grants"]!);
            s.UnifiedId = wire.Account;
            s.AuthToken = "tok";
            Assert.Empty(ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.Grants);
        }
        finally
        {
            sync.StopHeartbeat();
            GameWindow.CloseAllForPanic();
            ConditioningControlPanel.Avalonia.Platform.PrizeOwnership.ResetHeld();
            ConditioningControlPanel.Avalonia.Platform.AccountSeed.Sync = oldSync;
            CoreAccount.UnifiedUserId = oldAccount;
        }
    });

    // ---- the window ----

    [Fact]
    public void RacingThoughts_DoorRules_FailClosedWithoutATierProvider()
    {
        var (oldLab, oldPremium, oldFree, oldDenied) = (CoreEntitlement.HasLabProvider,
            CoreEntitlement.HasPremiumProvider, CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler);
        try
        {
            CoreEntitlement.HasPremiumProvider = () => true;
            CoreEntitlement.IsFreeTodayProvider = _ => true;
            int refusals = 0;
            CoreEntitlement.ShowDeniedHandler = _ => refusals++;
            foreach (var lab in new Func<bool>?[] { null, () => false, () => throw new InvalidOperationException() })
            {
                CoreEntitlement.HasLabProvider = lab;
                Assert.True(GameWindow.GlassroadLocked());
                Assert.False(GameWindow.GlassroadGate());
            }
            Assert.Equal(3, refusals);
            CoreEntitlement.HasLabProvider = () => true;
            Assert.False(GameWindow.GlassroadLocked());
            Assert.True(GameWindow.GlassroadGate());
            Assert.Equal(GameWindow.GlassroadId, GameWindow.ResolveDoor("RACE"));
            Assert.Equal("loom", GameWindow.ResolveDoor("loom"));
            Assert.Equal("dtrh/race.html", GameWindow.Games["raceclassic"].Page);
            Assert.Equal("glassroad/index.html", GameWindow.Games["race"].Page);
            Assert.Equal("glassroad/index.html", GameWindow.Games["race"].Page);
        }
        finally
        {
            (CoreEntitlement.HasLabProvider, CoreEntitlement.HasPremiumProvider,
                CoreEntitlement.IsFreeTodayProvider, CoreEntitlement.ShowDeniedHandler) = (oldLab, oldPremium, oldFree, oldDenied);
        }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void RacingThoughts_PrimeOnlyIncludingSolo_AndPanic(bool prime, bool solo)
    {
        RunGlassroad(prime ? "1" : null, (shell, launcher, _) =>
        {
            CoreAccount.IsLoggedInProvider = () => !solo;
            Assert.Contains(LauncherWindow.VisibleCards, c => c.Id == "race");
            Assert.Equal(!prime, LauncherWindow.Destinations["race"].Locked());
            Assert.Equal(GameWindow.GlassroadId, GameWindow.ResolveDoor("RACE"));
            Assert.Equal("dtrh/race.html", GameWindow.Games["raceclassic"].Page);
            Assert.Equal("glassroad/index.html", GameWindow.Games["race"].Page);
            if (prime && !solo)
            {
                int gates = 0;
                MainShellWindow.LeashGateDueProvider = () => true;
                var oldPresent = MainShellWindow.PresentLeashGateProvider;
                try
                {
                    MainShellWindow.PresentLeashGateProvider = _ => gates++;
                    launcher.Play(LauncherCards.Find("race")!);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(1, gates);
                    Assert.False(GameWindow.IsAnyOpen());
                    MainShellWindow.LeashGateDueProvider = () => false;
                    launcher.Play(LauncherCards.Find("race")!);
                    Dispatcher.UIThread.RunJobs();
                    Assert.Equal(GameWindow.GlassroadId, Assert.Single(GameWindow.OpenWindowsForTests()).Spec.Id);
                    PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
                    Assert.False(GameWindow.IsAnyOpen());
                }
                finally { MainShellWindow.PresentLeashGateProvider = oldPresent; }
            }
            foreach (var id in new[] { "race", GameWindow.GlassroadId })
            {
                var w = GameWindow.Launch(id);
                if (!prime) { Assert.Null(w); continue; }
                Assert.NotNull(w);
                Assert.Equal(GameWindow.GlassroadId, w.Spec.Id);
                Assert.EndsWith("/glassroad/index.html", w.PageUrl!.AbsolutePath);
                Assert.Same(w, GameWindow.Launch("race"));
                var old = CoreEntitlement.HasLabProvider;
                try
                {
                    CoreEntitlement.HasLabProvider = () => false;
                    Assert.Null(GameWindow.Launch(id)); // gate also runs before refocusing
                }
                finally { CoreEntitlement.HasLabProvider = old; }
                PanicSurfaces.All.Single(x => x.Id == "games").Stop(shell);
                Dispatcher.UIThread.RunJobs();
                Assert.False(GameWindow.IsAnyOpen());
            }
            Assert.False(GameWindow.IsAnyOpen());
        });
    }

    /// <summary>Init goes out at load, on the first message and at ready; the relay signs as the host's player, sends
    /// only /v2/race/ (judged signed), never the room-signed result; exit and menu-back close; a silent page is pinged
    /// then closed.</summary>
    [Fact]
    public void Glassroad_InitFramesNetRelayExitAndWatchdog() => RunGlassroad((_, _, fake) =>
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
        // #1543: a `loading` frame marks the page busy, so a silent, pinged page is waited for instead of closed.
        w.Web.OnWebMessage("{\"type\":\"loading\",\"stage\":\"boudoir\",\"p\":0.5}");
        Assert.Equal("ping", w.CheckGlassroadWatch(now.AddSeconds(25)));
        Assert.Equal("ok", w.CheckGlassroadWatch(now.AddSeconds(60)));
        Assert.True(GameWindow.IsAnyOpen());
        w.Web.OnWebMessage("{\"type\":\"heartbeat\"}");
        now = DateTime.UtcNow.AddSeconds(LoadingAged);   // the loading frame has aged out: back to ping, then close
        Assert.Equal("ping", w.CheckGlassroadWatch(now));
        Assert.Equal("ping", (string?)sent.Last()["type"]);
        Assert.Equal("closed", w.CheckGlassroadWatch(now.AddSeconds(5)));
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
    public void Glassroad_EscapePausesOnceUp_ASecondEscapeClosesIt() => RunGlassroad((shell, _, _) =>
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
