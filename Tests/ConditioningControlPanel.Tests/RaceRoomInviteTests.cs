using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Glassroad;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Racing Thoughts 3D rooms on the friends wire (7.2 lane A3): the host's room id on a
/// <c>race</c> invite, the room's fixed refusal reasons, and the pre-race room's invite list.</summary>
public class RaceRoomInviteTests
{
    private const string Room = "abc123def456";

    private static InboxItem? Parse(string extra) => FriendsApi.ParseItem(Newtonsoft.Json.JsonConvert.DeserializeObject<JObject>(
        "{\"id\":\"i1\",\"kind\":\"invite\",\"from\":\"u_1\",\"from_name\":\"Ann\",\"at\":\"2026-10-29T10:00:00Z\"," + extra + "}",
        new Newtonsoft.Json.JsonSerializerSettings { DateParseHandling = Newtonsoft.Json.DateParseHandling.None })!);

    [Fact]
    public void A_race_invite_carries_its_room()
    {
        var item = Parse("\"destination\":\"race\",\"room\":\"" + Room + "\"");
        Assert.NotNull(item);
        Assert.Equal(Room, item!.Room);
        Assert.Null(item.Code);
    }

    [Fact]
    public void A_race_invite_names_its_course_only_when_this_build_can()
    {
        var known = Parse("\"destination\":\"race\",\"room\":\"" + Room + "\",\"track\":\"keep\"");
        Assert.Equal("keep", known!.Track);
        Assert.Equal("Racing Thoughts, The Keep", LandingRules.WithCourse("Racing Thoughts", known.Track));

        var unknown = Parse("\"destination\":\"race\",\"room\":\"" + Room + "\",\"track\":\"<b>x</b>\"");
        Assert.Null(unknown!.Track);
        Assert.Equal("Racing Thoughts", LandingRules.WithCourse("Racing Thoughts", unknown.Track));

        var bare = Parse("\"destination\":\"race\",\"track\":\"keep\"");
        Assert.Null(bare!.Track);
    }

    [Theory]
    [InlineData("\"destination\":\"race\"")]
    [InlineData("\"destination\":\"race\",\"room\":null")]
    [InlineData("\"destination\":\"race\",\"room\":\"ABC123DEF456\"")]
    [InlineData("\"destination\":\"race\",\"room\":\"abc123def456&x=1\"")]
    [InlineData("\"destination\":\"backroom\",\"room\":\"abc123def456\"")]
    public void A_room_the_grammar_does_not_admit_leaves_a_bare_invite(string extra)
    {
        var item = Parse(extra);
        Assert.NotNull(item);
        Assert.Null(item!.Room);
    }

    [Fact]
    public void Room_ids_are_twelve_lowercase_letters_or_digits()
    {
        Assert.True(InviteDestination.IsRoomId(Room));
        Assert.False(InviteDestination.IsRoomId(null));
        Assert.False(InviteDestination.IsRoomId("abc123def45"));
        Assert.False(InviteDestination.IsRoomId("abc123def4567"));
        Assert.False(InviteDestination.IsRoomId("abc123def45-"));
        Assert.False(InviteDestination.IsRoomId("abc123def45é"));
    }

    [Fact]
    public void A_refused_send_keeps_only_a_known_room_reason()
    {
        var r = FriendsApi.SendOutcomeFromReply(JObject.Parse("""{ "ok": true, "status": "refused", "reason": "needs_prime" }"""));
        Assert.Equal(SendResult.Refused, r.Result);
        Assert.Equal("needs_prime", r.Reason);
        Assert.Null(FriendsApi.SendOutcomeFromReply(JObject.Parse("""{ "ok": true, "status": "refused", "reason": "say hi" }""")).Reason);
        Assert.Null(FriendsApi.SendOutcomeFromReply(JObject.Parse("""{ "ok": true, "status": "sent", "reason": "room_full" }""")).Reason);
        Assert.Null(FriendsApi.SendOutcomeFromReply(JObject.Parse("""{ "ok": true, "status": "refused" }""")).Reason);
    }

    [Fact]
    public void Every_reason_words_its_own_line_and_the_rest_reuse_the_drawer()
    {
        foreach (var reason in InviteDestination.RaceReasons)
            Assert.Equal("friends_race_" + reason,
                GlassroadRules.InviteNoteKey(new SendOutcome(SendResult.Refused, null) { Reason = reason }));
        Assert.Equal("friends_result_sent", GlassroadRules.InviteNoteKey(new SendOutcome(SendResult.Sent, "x")));
        Assert.Equal("friends_result_offline", GlassroadRules.InviteNoteKey(new SendOutcome(SendResult.Offline, null)));
        Assert.Equal("friends_result_refused", GlassroadRules.InviteNoteKey(new SendOutcome(SendResult.Refused, null)));
        Assert.Equal("They need Prime", GlassroadRules.Sentence("they need Prime"));
        Assert.Equal("", GlassroadRules.Sentence(""));
    }

    [Fact]
    public void Every_reason_line_is_in_all_ten_languages_and_keeps_the_voice()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Localization", "Languages");
        var files = Directory.GetFiles(dir, "*.json");
        Assert.Equal(10, files.Length);
        foreach (var file in files)
        {
            var raw = File.ReadAllBytes(file);
            Assert.False(raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF, file + " has a BOM");
            var o = JObject.Parse(System.Text.Encoding.UTF8.GetString(raw));
            foreach (var reason in InviteDestination.RaceReasons)
            {
                var line = (string?)o["friends_race_" + reason];
                Assert.False(string.IsNullOrWhiteSpace(line), Path.GetFileName(file) + " lacks " + reason);
                Assert.DoesNotContain('!', line!);
                Assert.DoesNotContain('—', line!);
                Assert.DoesNotContain('–', line!);
            }
        }
    }

    [Fact]
    public void The_invite_list_is_online_named_friends_only()
    {
        var friends = new[]
        {
            new Friend("u_a", "Ann", null, 2, true, FriendPresence.None, false),
            new Friend("u_b", "Bo", null, 0, false, FriendPresence.None, false),
            new Friend("u_c", " ", null, 2, true, FriendPresence.None, false),
        }.Concat(Enumerable.Range(0, 60).Select(i => new Friend("u_x" + i, "X" + i, null, 2, true, FriendPresence.None, false)));
        var rows = GlassroadRules.FriendRows(friends);
        Assert.Equal(50, rows.Count);
        Assert.Equal("u_a", (string?)rows[0]["uid"]);
        Assert.Equal("Ann", (string?)rows[0]["name"]);
        Assert.Equal(new[] { "uid", "name" }, ((JObject)rows[0]).Properties().Select(p => p.Name));
        Assert.DoesNotContain(rows, r => (string?)r["uid"] is "u_b" or "u_c");
        Assert.Empty(GlassroadRules.FriendRows(null));
    }

    [Fact]
    public void The_desktop_init_offers_the_friends_invite()
        => Assert.True((bool)GlassroadRules.InitFrame("u_1", "Kit", 2, false, 1, 1)["caps"]!["friendInvite"]!);

    [Fact]
    public async Task The_service_refuses_a_malformed_room_before_the_wire()
    {
        var api = new RoomApi();
        var svc = new FriendsService(api, () => "u_me", () => true, () => DateTimeOffset.UtcNow, () => false, _ => { });
        Assert.Equal(SendResult.Refused, (await svc.InviteToRaceAsync("u_a", "ABC123DEF456")).Result);
        Assert.Null(api.Room);
        var ok = await svc.InviteToRaceAsync("u_a", Room);
        Assert.Equal(SendResult.Refused, ok.Result);
        Assert.Equal("room_started", ok.Reason);
        Assert.Equal(("u_a", Room), (api.To, api.Room));
    }

    private sealed class RoomApi : IFriendsApi
    {
        public string? To, Room;
        public Task<FriendsSnapshot?> StateAsync(CancellationToken ct = default) => Task.FromResult<FriendsSnapshot?>(FriendsSnapshot.Empty);
        public Task<FriendsPollReply?> PollAsync(PresenceActivity? activity, int? lockDay, bool shared, CancellationToken ct = default)
            => Task.FromResult<FriendsPollReply?>(null);
        public Task<AddResult> RequestAsync(string code, CancellationToken ct = default) => Task.FromResult(AddResult.TryLater);
        public Task<SendResult> SendAsync(string to, SendKind kind, string? poke, string? destination, string? code, WatchRef? watch,
            CancellationToken ct = default) => Task.FromResult(SendResult.TryLater);
        public Task<bool> ActAsync(string op, string id, JObject? extra = null, CancellationToken ct = default) => Task.FromResult(false);
        public Task<SendOutcome> SendRaceInviteAsync(string to, string room, CancellationToken ct = default)
        {
            To = to; Room = room;
            return Task.FromResult(new SendOutcome(SendResult.Refused, null) { Reason = "room_started" });
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }
}
