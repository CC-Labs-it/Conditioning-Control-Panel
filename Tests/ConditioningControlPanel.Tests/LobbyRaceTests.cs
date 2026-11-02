using System;
using System.Linq;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Racing Thoughts 3D (Glassroad) open races in the Lobby: the /v2/race/open parse,
/// the merge, the summary line and the web join link.</summary>
public class LobbyRaceTests
{
    private const long Now = 1_000_000_000L;

    private const string TwoRows =
        "{\"rows\":[" +
        "{\"roomId\":\"abc123def456\",\"track\":\"keep\",\"host\":{\"name\":\"ann\"},\"seats\":6,\"filled\":2,\"laps\":3,\"staked\":true,\"startsIn\":20,\"friend\":true}," +
        "{\"roomId\":\"zzzz00001111\",\"track\":\"goonworks\",\"host\":{\"name\":\"bo\"},\"seats\":4,\"filled\":1,\"laps\":2,\"staked\":false,\"startsIn\":null,\"friend\":false}" +
        "],\"serverMs\":123}";

    [Fact]
    public void Parse_reads_rows()
    {
        var r = RaceOpenApi.Parse(TwoRows);
        Assert.True(r.Ok);
        Assert.Null(r.Error);
        Assert.Equal(2, r.Rooms.Count);
        var a = r.Rooms[0];
        Assert.Equal("abc123def456", a.RoomId);
        Assert.Equal("keep", a.Track);
        Assert.Equal("ann", a.HostName);
        Assert.Equal(6, a.Seats);
        Assert.Equal(2, a.Filled);
        Assert.Equal(3, a.Laps);
        Assert.True(a.Staked);
        Assert.Equal(20, a.StartsInSec);
        Assert.True(a.Friend);
        Assert.Null(r.Rooms[1].StartsInSec);
        Assert.False(r.Rooms[1].Friend);
    }

    [Theory]
    [InlineData("ABC123DEF456")]
    [InlineData("abc123def45")]
    [InlineData("abc123def4567")]
    [InlineData("abc123def45/")]
    [InlineData("abc123def456\\n")]
    public void Parse_drops_rows_with_a_bad_room_id(string id)
    {
        var json = "{\"rows\":[{\"roomId\":\"" + id + "\",\"track\":\"keep\",\"host\":{\"name\":\"x\"}}]}";
        var r = RaceOpenApi.Parse(json);
        Assert.True(r.Ok);
        Assert.Empty(r.Rooms);
    }

    [Fact]
    public void Tier_refusal_is_an_answer_with_no_rows()
    {
        var r = RaceOpenApi.Parse("{\"error\":\"tier\"}");
        Assert.True(r.Ok);
        Assert.True(r.TierLocked);
        Assert.Empty(r.Rooms);
    }

    [Theory]
    [InlineData("{\"error\":\"auth\"}")]
    [InlineData("{\"error\":\"rate\"}")]
    [InlineData("{\"error\":\"preview\"}")]
    [InlineData("not json")]
    [InlineData("{\"rows\":5}")]
    [InlineData("")]
    public void Faults_read_as_not_ok(string json)
    {
        var r = RaceOpenApi.Parse(json);
        Assert.False(r.Ok);
        Assert.False(r.TierLocked);
        Assert.Empty(r.Rooms);
    }

    [Fact]
    public void Missing_host_name_reads_as_someone()
    {
        var r = RaceOpenApi.Parse("{\"rows\":[{\"roomId\":\"abc123def456\",\"track\":\"boudoir\"}]}");
        Assert.Equal("someone", Assert.Single(r.Rooms).HostName);
    }

    [Theory]
    [InlineData("threshold", "Threshold")]
    [InlineData("mainframe", "Mainframe")]
    [InlineData("keep", "The Keep")]
    [InlineData("boudoir", "Boudoir")]
    [InlineData("goonworks", "GOONWORKS")]
    [InlineData("moon", "")]
    [InlineData(null, "")]
    public void Track_names(string? id, string name) => Assert.Equal(name, RaceOpenApi.TrackName(id));

    [Fact]
    public void Merge_puts_friend_races_in_friends_and_the_rest_in_open()
    {
        var snap = LobbyMerge.Build(null, null, null, null, true, Now, RaceOpenApi.Parse(TwoRows));
        var friend = Assert.Single(snap.Friends);
        Assert.Equal(LobbyGame.Race, friend.Game);
        Assert.Equal("abc123def456", friend.Key);
        Assert.True(friend.CanJoin);
        var open = Assert.Single(snap.Open);
        Assert.Equal("zzzz00001111", open.Key);
        Assert.Equal("goonworks", open.Track);
        Assert.Equal(2, snap.OpenCount);
    }

    [Fact]
    public void Merge_signed_out_or_tier_locked_shows_no_races()
    {
        Assert.True(LobbyMerge.Build(null, null, null, null, false, Now, RaceOpenApi.Parse(TwoRows)).IsEmpty);
        Assert.True(LobbyMerge.Build(null, null, null, null, true, Now, RaceOpenApi.Parse("{\"error\":\"tier\"}")).IsEmpty);
    }

    [Fact]
    public void Summary_line()
    {
        var row = LobbyMerge.Build(null, null, null, null, true, Now, RaceOpenApi.Parse(TwoRows)).Friends[0];
        var line = LobbySummary.For(row, k => k switch
        {
            "lobby_race_seats" => "{0}/{1} in",
            "lobby_race_laps" => "{0} laps",
            "lobby_race_staked" => "staked",
            "lobby_race_starts" => "starts in {0}s",
            _ => k,
        });
        Assert.Equal("The Keep · 2/6 in · 3 laps · staked · starts in 20s", line);
    }

    [Fact]
    public void Join_link_builds_only_for_a_room_id()
    {
        Assert.Equal("https://ccp.game/glassroad/index.html?join=abc123def456", RaceJoinLink.For("abc123def456")!.AbsoluteUri);
        Assert.Null(RaceJoinLink.For(null));
        Assert.Null(RaceJoinLink.For(""));
        Assert.Null(RaceJoinLink.For("abc123def456&x=1"));
        Assert.Null(RaceJoinLink.For("abc123def456\n"));
        Assert.Null(RaceJoinLink.For("ABC123DEF456"));
    }

    [Fact]
    public void Gates_let_any_account_join_a_listed_race_and_never_host_one()
    {
        var gates = LobbyGates.From(signedIn: true, goonHosting: false, remoteHosting: false);
        Assert.True(gates.CanJoin(LobbyGame.Race));
        Assert.False(gates.CanHost(LobbyGame.Race));
        Assert.False(LobbyGates.From(false, false, false).CanJoin(LobbyGame.Race));
    }

    [Fact]
    public void Row_view_labels_a_race()
    {
        var row = LobbyMerge.Build(null, null, null, null, true, Now, RaceOpenApi.Parse(TwoRows)).Open[0];
        var view = new LobbyRowView(row, LobbyGates.From(true, false, false), k => k);
        Assert.Equal("lobby_game_race", view.GameLabel);
        Assert.Equal("bo", view.Title);
        Assert.True(view.HasButton);
        Assert.False(view.Locked);
        Assert.Equal("features/race.png", LobbyRowView.ArtPath(LobbyGame.Race));
    }
}
