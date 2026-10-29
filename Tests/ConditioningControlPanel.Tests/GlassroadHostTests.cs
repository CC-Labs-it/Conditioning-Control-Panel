using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Glassroad;
using ConditioningControlPanel.Services.Safety;
using Newtonsoft.Json.Linq;
using Xunit;
using static ConditioningControlPanel.Services.Safety.PanicPolicy;

namespace ConditioningControlPanel.Tests;

/// <summary>The Glassroad desktop host (3D race preview): the hidden door, the init frame, the
/// relay's signing rule and its path gate, the kept Escape and the panic registry.</summary>
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

    [Theory]
    [InlineData("1", true)]
    [InlineData(" 1 ", true)]
    [InlineData(null, false)]
    [InlineData("0", false)]
    [InlineData("true", false)]
    public void TheDoor_OpensOnlyOnOne(string? env, bool open)
        => Assert.Equal(open, GlassroadRules.DoorOpen(env));

    [Fact]
    public void BothRaceDoors_GoThroughTheSwitch()
    {
        var catalogue = Read("Services", "Launcher", "LauncherCatalogue.cs");
        int race = catalogue.IndexOf("G(\"race\"", StringComparison.Ordinal);
        Assert.True(race >= 0);
        var entry = catalogue.Substring(race, catalogue.IndexOf("revealed:", race, StringComparison.Ordinal) - race);
        Assert.Contains("GlassroadRules.DoorOpenNow()", entry);
        Assert.Contains("GlassroadHostService.Launch()", entry);
        Assert.Contains("CaucusHostService.Launch()", entry);
        // The Play card reaches the same entry.
        Assert.Contains("LaunchPlayLauncherGame(\"race\")", Read("Views", "Tabs", "PlayTabView.Cards.cs"));
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
    public void Init_CarriesOnlyKartGrants_AndTheOwnedPushMatches()
    {
        var owned = new[] { "cos.rain.hair.ink-sleek", "cos.kart.body.petal", "cos.kart.body.petal", "cos.kart.umbrella.lace", "backroom.x", "cos.kart." + new string('a', 60) };
        var grants = (JArray)GlassroadRules.InitFrame("u_1", "Kit", 2, false, 1, 1, owned)["identity"]!["grants"]!;
        Assert.Equal(new[] { "cos.kart.body.petal", "cos.kart.umbrella.lace" }, grants.Select(g => (string)g!).ToArray());
        Assert.Empty((JArray)GlassroadRules.InitFrame("u_1", "Kit", 2, false, 1, 1)["identity"]!["grants"]!);
        var push = GlassroadRules.OwnedFrame(owned);
        Assert.Equal("owned", (string?)push["type"]);
        Assert.Equal(grants.ToString(), push["grants"]!.ToString());
        Assert.Empty((JArray)GlassroadRules.OwnedFrame(null)["grants"]!);
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
