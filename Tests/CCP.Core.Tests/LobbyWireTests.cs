using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class LobbyWireTests
{
    [Fact]
    public void Poll_backs_off_on_errors_and_snaps_back()
    {
        var t = LobbyPollPlan.Base;
        t = LobbyPollPlan.Next(t, ok: false);
        Assert.Equal(TimeSpan.FromSeconds(20), t);
        t = LobbyPollPlan.Next(t, false);
        t = LobbyPollPlan.Next(t, false);
        t = LobbyPollPlan.Next(t, false);
        Assert.Equal(LobbyPollPlan.Max, t);
        Assert.Equal(LobbyPollPlan.Base, LobbyPollPlan.Next(t, true));
    }

    [Fact]
    public void Pbp_lobby_parse_drops_self_and_idless_rows()
    {
        var json = "{\"players\":[{\"id\":\"p_a\",\"display_name\":\"ann\",\"since_ms\":5,\"time_control\":{\"initial_ms\":300000,\"increment_ms\":3000}}," +
                   "{\"id\":\"p_me\",\"display_name\":\"me\",\"self\":true},{\"display_name\":\"noid\"}]," +
                   "\"playing\":[{\"white\":{\"display_name\":\"w\"},\"black\":\"b\",\"started_ms\":9,\"moves\":4}],\"matched\":null}";
        var r = PbpLobbyApi.Parse(json);
        Assert.True(r.Ok);
        var seat = Assert.Single(r.Open);
        Assert.Equal("p_a", seat.Id);
        Assert.Equal(300000, seat.InitialMs);
        Assert.Equal(3000, seat.IncrementMs);
        var game = Assert.Single(r.Playing);
        Assert.Equal("w", game.White);
        Assert.Equal("b", game.Black);
        Assert.Equal(4, game.Moves);
    }

    [Fact]
    public void Pbp_lobby_cosmetics_use_the_head_registry_and_drop_unknown_frames()
    {
        var previous = PlayerLook.KnownIdsProvider;
        const string known = "{\"players\":[{\"id\":\"p\",\"cosmetics\":{\"frame_id\":\"profile.frame.lace\"}}]}";
        const string unknown = "{\"players\":[{\"id\":\"p\",\"cosmetics\":{\"frame_id\":\"unshipped\"}}]}";
        try
        {
            PlayerLook.KnownIdsProvider = null;
            Assert.Null(Assert.Single(PbpLobbyApi.Parse(known).Open).Look.FrameId);
            PlayerLook.KnownIdsProvider = () => (new HashSet<string>(), new HashSet<string> { "profile.frame.lace" });
            Assert.Equal("profile.frame.lace", Assert.Single(PbpLobbyApi.Parse(known).Open).Look.FrameId);
            Assert.Same(PlayerLook.None, Assert.Single(PbpLobbyApi.Parse(unknown).Open).Look);
        }
        finally { PlayerLook.KnownIdsProvider = previous; }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("<html>404</html>")]
    [InlineData("{\"ok\":true}")]
    public void Pbp_lobby_parse_of_anything_else_is_empty(string? body)
    {
        var r = PbpLobbyApi.Parse(body);
        Assert.False(r.Ok);
        Assert.Empty(r.Open);
    }
}
