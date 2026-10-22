using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Controls.Friends;
using ConditioningControlPanel.Services.Friends;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Friends Everywhere D2: the poll says it is the desktop, rows mark where a friend is
/// online (desktop / phone / web), and web invites to race and breakout land.</summary>
public class FriendsPlatformsTests
{
    private sealed class Handler : HttpMessageHandler
    {
        private readonly string _reply;
        public string? LastBody;
        public Handler(string reply) => _reply = reply;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_reply, Encoding.UTF8, "application/json") };
        }
    }

    private static (FriendsApi Api, Handler H) Make(string reply)
    {
        var h = new Handler(reply);
        return (new FriendsApi(new HttpClient(h), () => ("u_me", "tok"), "http://127.0.0.1:1"), h);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EveryPollSaysItIsTheDesktop(bool shared)
    {
        var (api, h) = Make("{\"ok\":true,\"online\":[],\"inbox\":[]}");
        await api.PollAsync(PresenceActivity.Panel, null, shared);
        var sent = JObject.Parse(h.LastBody!);
        Assert.Equal("desktop", (string?)sent["platform"]);
        Assert.Equal(shared, (bool)sent["shared"]!);
    }

    private static Friend ParseOne(string friendJson)
    {
        var snap = FriendsApi.ParseState(JObject.Parse("{\"ok\":true,\"code\":\"CCP-ABCDE\",\"friends\":[" + friendJson + "]}"));
        return Assert.Single(snap.Friends);
    }

    [Fact]
    public void PlatformsComeBackInTheOneOrderWithJunkDropped()
    {
        var f = ParseOne("{\"id\":\"u_1\",\"name\":\"Ann\",\"online\":true,\"activity\":\"panel\"," +
            "\"platforms\":[\"web\",\"PHONE\",\"fridge\",\"web\",7,\"desktop\"]}");
        Assert.Equal(new[] { "desktop", "phone", "web" }, f.Platforms);
    }

    [Theory]
    [InlineData("{\"id\":\"u_1\",\"name\":\"Ann\",\"online\":true,\"activity\":\"panel\"}")]
    [InlineData("{\"id\":\"u_1\",\"name\":\"Ann\",\"online\":true,\"platforms\":\"phone\"}")]
    [InlineData("{\"id\":\"u_1\",\"name\":\"Ann\",\"online\":false,\"platforms\":[\"phone\"]}")]
    public void MissingOddOrOfflinePlatformsReadAsNone(string json)
        => Assert.Empty(ParseOne(json).Platforms);

    [Fact]
    public void AFriendBuiltInCodeHasNoPlatforms()
        => Assert.Empty(new Friend("u", "n", null, 0, true, FriendPresence.None, false).Platforms);

    [Fact]
    public void TheSameStateParsedTwiceIsTheSameSnapshot()
    {
        // Record equality compares Platforms by reference: equal sets must share one instance, or
        // every `state` reply would redraw the drawer, the rail chip and the landing for nothing.
        const string json = "{\"ok\":true,\"code\":\"CCP-ABCDE\",\"friends\":[" +
            "{\"id\":\"u_1\",\"name\":\"Ann\",\"online\":true,\"activity\":\"panel\",\"platforms\":[\"desktop\",\"phone\"]}," +
            "{\"id\":\"u_2\",\"name\":\"Bo\",\"online\":true,\"activity\":\"panel\",\"platforms\":[]}]}";
        var a = FriendsApi.ParseState(JObject.Parse(json));
        var b = FriendsApi.ParseState(JObject.Parse(json));
        Assert.Equal(new[] { "desktop", "phone" }, a.Friends[0].Platforms);
        Assert.Empty(a.Friends[1].Platforms);
        Assert.True(FriendsSnapshotEquality.Same(a, b));
        Assert.Same(FriendPlatform.Normalise(new[] { "phone", "desktop" }), FriendPlatform.Normalise(new[] { "desktop", "phone" }));
    }

    private static Friend Online(params string[] platforms) =>
        new Friend("u_1", "Ann", null, 0, true, new FriendPresence(PresenceActivity.Panel, null, DateTimeOffset.UtcNow), false)
        { Platforms = platforms };

    [Fact]
    public void RowMarksRunDesktopPhoneWebWithTheirTips()
    {
        var marks = FriendsDrawerRules.PlatformMarks(Online("web", "desktop", "phone"));
        Assert.Equal(new[] { "desktop", "phone", "web" }, marks.Select(m => m.Platform));
        Assert.Equal(new[] { "friends_platform_desktop", "friends_platform_phone", "friends_platform_web" }, marks.Select(m => m.TipKey));
        Assert.Equal(3, marks.Select(m => m.Glyph).Distinct().Count());
        Assert.All(marks, m => Assert.True(m.Glyph.Length == 1 && m.Glyph[0] >= '' && m.Glyph[0] <= ''));
    }

    [Fact]
    public void AnOfflineFriendOrAnOldServerDrawsNoMarks()
    {
        Assert.Empty(FriendsDrawerRules.PlatformMarks(Online()));
        Assert.Empty(FriendsDrawerRules.PlatformMarks(Online("phone") with { Online = false }));
    }

    [Fact]
    public void MarksUseTheFallbackFontChain()
    {
        Assert.StartsWith("Segoe MDL2 Assets,", FriendsDrawerRules.GlyphFont);
        Assert.Contains("Segoe UI Symbol", FriendsDrawerRules.GlyphFont);
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Controls", "Friends", "FriendsDrawer.Platforms.cs"));
        Assert.Contains("FriendsDrawerRules.GlyphFont", src);
        Assert.DoesNotContain("new FontFamily(\"Segoe MDL2 Assets\")", src);
    }

    [Theory]
    [InlineData("race")]
    [InlineData("breakout")]
    public void WebInvitesToRaceAndBreakoutLand(string dest)
    {
        var item = FriendsApi.ParseItem(Wire(
            "{\"id\":\"i1\",\"kind\":\"invite\",\"from\":\"u_1\",\"from_name\":\"Ann\",\"destination\":\"" + dest +
            "\",\"at\":\"2026-10-20T10:00:00Z\",\"expires_at\":\"2026-10-20T10:05:00Z\"}"));
        Assert.NotNull(item);
        Assert.Equal(dest, item!.Destination);
        Assert.Null(item.Code);
        Assert.False(InviteDestination.IsSendable(dest));

        var art = LandingRules.KnockArt(item);
        Assert.Equal("features/" + dest + ".png", art);
        Assert.True(File.Exists(Path.Combine(RepoRoot(), "Assets", art.Replace('/', Path.DirectorySeparatorChar))), art);

        var line = FriendsFeedRules.Describe(new FriendEvent(FriendEventKind.InviteReceived, "u_1", "Ann", DateTime.UtcNow, "k", dest));
        Assert.Equal("friends_invite_" + dest, line.Detail);
    }

    [Fact]
    public void ARaceInviteWithAJunkCodeIsRefused()
        => Assert.Null(FriendsApi.ParseItem(Wire(
            "{\"id\":\"i1\",\"kind\":\"invite\",\"from\":\"u_1\",\"destination\":\"race\",\"code\":\"say hi\",\"at\":\"2026-10-20T10:00:00Z\"}")));

    [Fact]
    public void TheWebRaceIsTheOneHost()
        => Assert.Equal("https://app.cclabs.app/race", InviteDestination.RaceWebUrl);

    [Fact]
    public void NewKeysAreInAllTenLanguages()
    {
        var keys = new[]
        {
            "friends_platform_desktop", "friends_platform_phone", "friends_platform_web",
            "friends_invite_race", "friends_invite_breakout", "friends_land_dest_race", "friends_land_dest_breakout",
        };
        var dir = SourceRoots.LanguagesDirectory;
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var bytes = File.ReadAllBytes(file);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, file + " has a BOM");
            var json = JObject.Parse(Encoding.UTF8.GetString(bytes));
            foreach (var k in keys)
            {
                var v = (string?)json[k];
                Assert.False(string.IsNullOrWhiteSpace(v), $"{Path.GetFileName(file)} is missing {k}");
                Assert.DoesNotContain('�', v!);
                Assert.DoesNotContain("Ã", v!);
                Assert.DoesNotContain("!", v!);
            }
        }
    }

    private static JObject Wire(string json) =>
        Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(json,
            new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })!;

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !Directory.Exists(Path.Combine(d.FullName, "ConditioningControlPanel", "Localization")))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
