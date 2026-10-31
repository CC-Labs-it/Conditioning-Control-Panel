using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.Lobby;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class LobbyRaceInviteTests
{
    [Theory]
    [InlineData("threshold", "Threshold")]
    [InlineData("mainframe", "Mainframe")]
    [InlineData("keep", "The Keep")]
    [InlineData("boudoir", "Boudoir")]
    [InlineData("goonworks", "GOONWORKS")]
    [InlineData("unknown", "")]
    public void InviteParsesOnlyKnownCoursesAndWordsThem(string track, string course)
    {
        var item = FriendsApi.ParseItem(new JObject {
            ["id"] = "invite", ["from"] = "friend", ["kind"] = "invite", ["destination"] = "race",
            ["at"] = "2026-10-31T12:00:00Z", ["room"] = "abcdefghijkl", ["track"] = track })!;
        Assert.NotNull(item);
        Assert.Equal("abcdefghijkl", item.Room);
        Assert.Equal(course.Length == 0 ? null : track, item.Track);
        Assert.Equal(course.Length == 0 ? "Race" : "Race, " + course, LandingRules.WithCourse("Race", item.Track));
        var row = new LobbyRow { Game = LobbyGame.Race, Track = track };
        Assert.Contains(course, LobbySummary.For(row, key => key));
    }

    [Fact]
    public void MalformedRoomDropsCourse()
    {
        var item = FriendsApi.ParseItem(new JObject { ["id"] = "i", ["from"] = "f", ["kind"] = "invite",
            ["destination"] = "race", ["room"] = "bad", ["at"] = "2026-10-31T12:00:00Z", ["track"] = "keep" })!;
        Assert.Null(item.Room);
        Assert.Null(item.Track);
    }

    [Fact]
    public void NonRaceInviteCannotCarryACourse()
    {
        var item = FriendsApi.ParseItem(new JObject { ["id"] = "i", ["from"] = "f", ["kind"] = "invite",
            ["destination"] = "backroom", ["at"] = "2026-10-31T12:00:00Z", ["track"] = "keep" })!;
        Assert.Null(item.Track);
    }
}
