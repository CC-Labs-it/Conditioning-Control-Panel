using System;
using System.IO;
using ConditioningControlPanel.Services.Glassroad;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>The Glassroad desktop host (Racing Thoughts 3D): the open race doors, the init frame,
/// the relay's signing rule and its path gate, the kept Escape and the panic registry.</summary>
[Collection(GameSurfacesCollection.Name)]
public class GlassroadHostTests
{
    private const string Base = "https://codebambi-proxy.vercel.app";
    private const string Race = HostNetPathRule.RacePrefix;

    // ---- the relay's path gate ----

    [Theory]
    [InlineData("/v2/race/open")]
    [InlineData("/v2/race/link/claim")]
    [InlineData("/v2/race/link/AbCdEfGhIjKlMnOpQrStUv")]
    [InlineData("/v2/race/access?unified_id=discord%3A1234")]
    public void RaceRoutes_Pass_AndLandOnTheProxy(string path)
    {
        Assert.Equal("/v2/race/", Race);
        Assert.True(HostNetPathRule.TryBuild(Base, path, Race, out var uri));
        Assert.Equal("codebambi-proxy.vercel.app", uri!.Host);
        Assert.StartsWith(Race, uri.AbsolutePath);
    }

    [Theory]
    [InlineData("/v2/race/../user/me")]
    [InlineData("/v2/race/%2e%2e/user/me")]
    [InlineData("/v2/race\\..\\user\\me")]
    [InlineData("/v2/race//evil.example/x")]
    [InlineData("/v2/race/open?next=https://evil.example/")]
    [InlineData("/v2/racex/open")]
    [InlineData("/v2/pbp/lobby")]
    [InlineData("/v2/user/me")]
    public void RacePrefix_RefusesTraversalAndOtherRoutes(string path)
        => Assert.False(HostNetPathRule.TryBuild(Base, path, Race, out _));

    // ---- the door ----

    [Fact]
    public void TheRaceDoor_OpensGlassroadForEveryone_The2dRaceIsRetired()
    {
        var catalogue = Read("Services", "Launcher", "LauncherCatalogue.cs");
        int race = catalogue.IndexOf("G(\"race\"", StringComparison.Ordinal);
        Assert.True(race >= 0);
        var entry = catalogue.Substring(race, catalogue.IndexOf(");", race, StringComparison.Ordinal) - race);
        Assert.Contains("GlassroadHostService.Launch()", entry);
        Assert.Contains("GlassroadHostService.IsActive", entry);
        Assert.DoesNotContain("CaucusHostService", entry);   // no 2D door
        Assert.DoesNotContain("revealed:", entry);           // always revealed, no switch, no purchase
        Assert.DoesNotContain("CCP_GLASSROAD", catalogue);
        // The Play card reaches the same entry.
        Assert.Contains("LaunchPlayLauncherGame(\"race\")", Read("Views", "Tabs", "PlayTabView.Cards.cs"));
    }

    [Fact]
    public void TheBackRoomPortal_OpensGlassroad_AndKeepsTheRoom()
    {
        var room = Read("Services", "BackRoom", "BackRoomHostService.cs");
        int start = room.IndexOf("if ((string?)message[\"game\"] != \"race\"", StringComparison.Ordinal);
        Assert.True(start >= 0);
        var body = room.Substring(start, room.IndexOf("\n    }", start, StringComparison.Ordinal) - start);
        Assert.Contains("GlassroadHostService.Launch()", body);
        Assert.Contains("ok = false, reason = \"elsewhere\"", body);   // releases the page's portal
        Assert.DoesNotContain("RequestClose(\"race\")", body);         // the room stays up
        Assert.DoesNotContain("_openingRace = true", body);
    }

    // ---- the init frame ----

    [Fact]
    public void Init_SignedIn_CarriesTheIdentityAndNoSong()
    {
        var f = GlassroadRules.InitFrame("u_1", "Kit", 7, false, 0.75, 0.5);
        Assert.Equal("init", (string?)f["type"]);
        Assert.Equal(2, (int)f["v"]!);
        Assert.Equal("desktop", (string?)f["caps"]!["platform"]);
        Assert.True((bool)f["caps"]!["net"]!);
        Assert.Empty((JArray)f["caps"]!["songs"]!);
        Assert.Equal("u_1", (string?)f["identity"]!["unifiedId"]);
        Assert.Equal("Kit", (string?)f["identity"]!["displayName"]);
        Assert.Equal(2, (int)f["identity"]!["tier"]!);   // clamped
        Assert.Equal(600, (int)f["gentle"]!["fadeMs"]!);
        Assert.Equal("Player", (string?)GlassroadRules.InitFrame("u_1", " ", 0, false, 1, 1)["identity"]!["displayName"]);
    }

    [Fact]
    public void Init_SignedOut_HasNullIdentityAndStillNet_GentleCarried()
    {
        var f = GlassroadRules.InitFrame(null, "Kit", 2, true, 1.5, 0.25);
        Assert.Equal(JTokenType.Null, f["identity"]!.Type);
        Assert.True((bool)f["caps"]!["net"]!);
        Assert.True((bool)f["gentle"]!["on"]!);
        Assert.Equal(1500, (int)f["gentle"]!["fadeMs"]!);
        Assert.Equal(GentleVisuals.RiseFloorMs, (int)f["gentle"]!["riseMs"]!);
        Assert.Equal(0.25, (double)f["gentle"]!["speed"]!);
    }

    // ---- signing ----

    [Fact]
    public void Post_GetsTheHostsUnifiedId_GetGetsItInTheQuery()
    {
        Assert.True(GlassroadRules.TrySign("POST", "/v2/race/link/claim", "{\"token\":\"abc\"}", "u_1", out var p, out var b));
        Assert.Equal("/v2/race/link/claim", p);
        Assert.Equal("{\"token\":\"abc\",\"unified_id\":\"u_1\"}", b);
        Assert.True(GlassroadRules.TrySign("POST", "/v2/race/link", "", "u_1", out _, out var empty));
        Assert.Equal("{\"unified_id\":\"u_1\"}", empty);
        Assert.True(GlassroadRules.TrySign("GET", "/v2/race/open", "", "discord:12", out var g, out _));
        Assert.Equal("/v2/race/open?unified_id=discord%3A12", g);
        Assert.True(GlassroadRules.TrySign("GET", "/v2/race/board/t?x=1", "", "u_1", out var q, out _));
        Assert.Equal("/v2/race/board/t?x=1&unified_id=u_1", q);
        Assert.True(HostNetPathRule.TryBuild(Base, g, Race, out _));
    }

    [Theory]
    [InlineData("POST", "/v2/race/open", "{\"unified_id\":\"u_other\"}")]   // the page names someone
    [InlineData("POST", "/v2/race/open", "{\"Unified_Id\":\"u_other\"}")]
    [InlineData("GET", "/v2/race/open?unified_id=u_other", "")]
    [InlineData("POST", "/v2/race/result", "{}")]                           // room-signed, never from a page
    [InlineData("POST", "/v2/race/result/x", "{}")]
    [InlineData("POST", "/v2/race/open", "[1,2]")]                          // not an object
    [InlineData("POST", "/v2/race/open", "{nope")]
    [InlineData("GET", "/v2/pbp/lobby", "")]
    [InlineData("GET", "", "")]
    public void Refusals(string method, string path, string body)
        => Assert.False(GlassroadRules.TrySign(method, path, body, "u_1", out _, out _));

    [Fact]
    public void OversizedBody_IsRefused_SignedOutSendsBare()
    {
        Assert.False(GlassroadRules.TrySign("POST", "/v2/race/open",
            "{\"x\":\"" + new string('a', GlassroadRules.MaxBodyBytes) + "\"}", "u_1", out _, out _));
        Assert.True(GlassroadRules.TrySign("GET", "/v2/race/link/AbCdEfGhIjKlMnOpQrStUv", "", null, out var p, out _));
        Assert.Equal("/v2/race/link/AbCdEfGhIjKlMnOpQrStUv", p);
    }

    [Fact]
    public void TheHost_SignsThenJudgesTheSignedPath()
    {
        var host = Read("Services", "Glassroad", "GlassroadHostService.cs");
        int sign = host.IndexOf("GlassroadRules.TrySign(", StringComparison.Ordinal);
        int judge = host.IndexOf("HostNetPathRule.TryBuild(ProxyBaseUrl, signedPath, GlassroadRules.AllowedPathPrefix", StringComparison.Ordinal);
        Assert.True(sign >= 0 && judge > sign, "the signed path must be the one judged");
        Assert.Contains("new ChaosWebViewHost(", host);   // ChaosWebViewHost attaches ScrolllerWebGate
        Assert.Contains("UserDataFolderName = \"browser_data_glassroad\"", host);
    }

    // ---- Escape and panic ----

    [Fact]
    public void GlassroadKeptTheEscape_ThePageIsHandedIt()
    {
        Assert.Equal(KeptEscapePage.Glassroad, KeptEscapeGoesTo(true, false, false, false, false, false, false,
            glassroadInFront: true, glassroadReady: true));
        Assert.Equal(KeptEscapePage.None, KeptEscapeGoesTo(true, false, false, false, false, false, false,
            glassroadInFront: true, glassroadReady: false));
        Assert.Equal(KeptEscapePage.None, KeptEscapeGoesTo(false, false, false, false, false, false, false,
            glassroadInFront: true, glassroadReady: true));
    }

    [Fact]
    public void TheEscapePause_KnowsGlassroad()
    {
        var source = Read("MainWindow", "MainWindow.xaml.cs");
        int start = source.IndexOf("private bool TryRacePauseOnEscape()", StringComparison.Ordinal);
        int end = source.IndexOf("private void HandlePanicKeyPress()", start, StringComparison.Ordinal);
        var body = end > start ? source[start..end] : source[start..];
        int gate = body.IndexOf("PanicPolicy.KeptEscapeGoesTo(claim", StringComparison.Ordinal);
        Assert.Contains("GlassroadHostService.IsInFront", body);
        Assert.Contains("GlassroadHostService.IsReady", body);
        Assert.Contains("|| intakeInFront || glassroadInFront", body);
        Assert.True(body.IndexOf("GlassroadHostService.PostKeptEscape()", StringComparison.Ordinal) > gate);
    }

    [Fact]
    public void Panic_ClosesGlassroad()
        => Assert.Contains("glassroad", GameSurfaces.Ids());

    // ---- Sparkles by place (B1 W4): race-result -> claim ----

    private static JObject Result(string mode, int place = 2, int of = 8, int laps = 3, int fullLaps = 3,
        double timeS = 612.4, bool finished = true, string? medal = null, string? roomId = null) => new()
    {
        ["type"] = "race-result", ["v"] = 1, ["track"] = "threshold", ["laps"] = laps, ["place"] = place, ["of"] = of,
        ["timeS"] = timeS, ["bestLapS"] = 200.1, ["finished"] = finished, ["items"] = mode != "trial", ["xp"] = null,
        ["mode"] = mode, ["fullLaps"] = fullLaps, ["roomId"] = roomId, ["medal"] = medal,
    };

    [Fact]
    public void Solo_ClaimsItsPlace_WithLapsAndDuration()
    {
        var c = GlassroadRules.SparklesClaimFor(Result("race", place: 3, of: 8, laps: 1))!;
        Assert.Equal("rt3d", GlassroadRules.SparklesGame);
        Assert.Equal("solo", c.Mode);
        Assert.Equal(new JObject { ["kind"] = "place", ["place"] = 3, ["of"] = 8 }.ToString(), c.Result.ToJson().ToString());
        Assert.Equal(1, (int)c.Detail["laps"]!);
        Assert.Equal("threshold", (string?)c.Detail["track"]);
        Assert.Equal(612400, c.Duration.TotalMilliseconds, 0);
        Assert.Null(c.Proof);
    }

    [Theory]
    [InlineData("glass", "gold")]
    [InlineData("gold", "gold")]
    [InlineData("silver", "silver")]
    [InlineData("bronze", "bronze")]
    [InlineData(null, "none")]
    public void Trial_ClaimsItsMedal_GlassIsGold(string? medal, string expected)
    {
        var c = GlassroadRules.SparklesClaimFor(Result("trial", place: 1, of: 1, medal: medal))!;
        Assert.Equal("trial", c.Mode);
        Assert.Equal("medal", c.Result.Kind);
        Assert.Equal(expected, c.Result.Medal);
    }

    [Fact]
    public void Trial_ShortOfFullLength_ClaimsNothing()
        => Assert.Null(GlassroadRules.SparklesClaimFor(Result("trial", place: 1, of: 1, laps: 1, medal: "gold")));

    [Fact]
    public void Trial_LongerThanFullLength_ClaimsNothing()
        => Assert.Null(GlassroadRules.SparklesClaimFor(Result("trial", place: 1, of: 1, laps: 5, medal: "gold")));

    [Fact]
    public void Race_LongerThanFullLength_ClaimsFullLength()
        => Assert.Equal(3, (int)GlassroadRules.SparklesClaimFor(Result("race", laps: 5))!.Detail["laps"]!);

    [Fact]
    public void Online_CarriesTheRoomAsProof()
    {
        var c = GlassroadRules.SparklesClaimFor(Result("online", place: 1, of: 6, roomId: "abc123def456"))!;
        Assert.Equal("online", c.Mode);
        Assert.Equal("abc123def456", (string?)c.Proof!["roomId"]);
        Assert.Equal("place", c.Result.Kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("../evil")]
    public void Online_WithoutAGoodRoom_ClaimsNothing(string? roomId)
        => Assert.Null(GlassroadRules.SparklesClaimFor(Result("online", roomId: roomId)));

    [Fact]
    public void Ghost_IsADuel_OnlyAtFullLength()
    {
        var win = Result("ghost", place: 1, of: 1);
        win["ghost"] = "win";
        var c = GlassroadRules.SparklesClaimFor(win)!;
        Assert.Equal("ghost", c.Mode);
        Assert.Equal("win", c.Result.Outcome);
        win["laps"] = 1;
        Assert.Null(GlassroadRules.SparklesClaimFor(win));
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("ghost", place: 1, of: 1)));   // no outcome
    }

    [Fact]
    public void Unfinished_Flagged_OrMalformed_ClaimsNothing()
    {
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("race", finished: false)));
        var flagged = Result("race");
        flagged["flagged"] = true;
        Assert.Null(GlassroadRules.SparklesClaimFor(flagged));
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("race", place: 9, of: 8)));
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("race", place: 0)));
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("race", laps: 0)));
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("race", timeS: 0)));
        Assert.Null(GlassroadRules.SparklesClaimFor(Result("bogus")));
        Assert.Null(GlassroadRules.SparklesClaimFor(new JObject { ["type"] = "run-ended", ["laps"] = 3 }));
        Assert.Null(GlassroadRules.SparklesClaimFor(null));
        var noFinished = Result("race");
        noFinished.Remove("finished");
        Assert.Null(GlassroadRules.SparklesClaimFor(noFinished));
    }

    [Fact]
    public void ThePage_SendsTheClaimFields_AndTheHostClaims()
    {
        var boot = Read("Resources", "web", "glassroad", "boot.js");
        var send = boot.IndexOf("bridge.send({ type: 'race-result'", StringComparison.Ordinal);
        Assert.True(send > 0);
        var frame = boot.Substring(send, 700);
        foreach (var field in new[] { "mode:", "fullLaps:", "roomId:", "medal:" }) Assert.Contains(field, frame);
        var host = Read("Services", "Glassroad", "GlassroadHostService.cs");
        Assert.Contains("case \"race-result\":", host);
        Assert.Contains("SparklesGameEnd.Claim(GlassroadRules.SparklesGame", host);
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", Path.Combine(parts)));

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
