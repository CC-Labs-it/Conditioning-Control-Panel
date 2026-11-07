using System;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Friends;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Lobby;
using ConditioningControlPanel.Views.Tabs;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Fix wave 2 (F4a, contract 4 "others see it"): a Lobby row draws its host's profile frame/avatar. The look comes
/// from the row's own wire (<c>cosmetics</c>, server-clamped) when present, else from the friends state for a friend;
/// strangers on a server that sends nothing draw as before.
/// </summary>
public class LobbyLookTests
{
    private const long Now = 1_000_000_000L;
    private static readonly PlayerLook Lace = new("profile.avatar.emi-pixel", "profile.frame.lace");
    private static readonly PlayerLook Gilt = new(null, "profile.frame.gilt");

    private static Friend F(string id, string name, PlayerLook look, PresenceActivity act = PresenceActivity.Panel) =>
        new(id, name, null, 0, true, new FriendPresence(act, null, DateTimeOffset.UtcNow), false) { Look = look };

    private static PbpLobbyReply Chess(params PbpOpenSeat[] open) => new(open.ToList(), Array.Empty<PbpPlayingGame>(), true);

    [Fact]
    public void A_friend_row_wears_the_friends_look_and_a_stranger_wears_nothing()
    {
        var snap = LobbyMerge.Build(
            Chess(new PbpOpenSeat("p_1", "ann", 600000, 0, Now - 5000), new PbpOpenSeat("p_2", "zed", 600000, 0, Now - 5000)),
            null,
            new[] { new RemoteSeat("u_bo", "bo", 3, "full", Array.Empty<string>(), false) },
            new[] { F("u_ann", "ann", Lace), F("u_bo", "bo", Gilt) }, signedIn: true, Now);
        Assert.Same(Lace, snap.Friends.Single(r => r.Game == LobbyGame.Chess).Look);
        Assert.Same(Gilt, snap.Friends.Single(r => r.Game == LobbyGame.Remote).Look);
        Assert.Same(PlayerLook.None, snap.Open.Single(r => r.HostName == "zed").Look);
    }

    [Fact]
    public void The_wire_look_wins_over_the_friends_state_and_reaches_strangers()
    {
        var table = new OpenTable("ABCD", "cy", 3, null, false, null, true, 45, false, 0) { Look = Gilt };
        var race = new RaceOpenRoom("r_1", "threshold", "ann", 8, 1, 3, false, null, true) { Look = Gilt };
        var snap = LobbyMerge.Build(null, new OpenTablesReply(true, true, new[] { table }, null), null,
            new[] { F("u_ann", "ann", Lace) }, signedIn: true, Now, new RaceOpenReply(new[] { race }, true));
        Assert.Same(Gilt, snap.Open.Single(r => r.Game == LobbyGame.Goon).Look);
        Assert.Same(Gilt, snap.Friends.Single(r => r.Game == LobbyGame.Race).Look);
    }

    [Fact]
    public void A_presence_row_carries_the_friends_look_and_a_shared_name_picks_nobody()
    {
        var snap = LobbyMerge.Build(Chess(new PbpOpenSeat("p_1", "twin", 600000, 0, Now)), null, null,
            new[] { F("u_a", "twin", Lace), F("u_b", "twin", Gilt), F("u_c", "cleo", Lace, PresenceActivity.Goon) },
            signedIn: true, Now);
        Assert.Same(PlayerLook.None, snap.Friends.Single(r => r.HostName == "twin").Look);
        Assert.Same(Lace, snap.Friends.Single(r => r.HostName == "cleo").Look);
    }

    [Fact]
    public void Wires_without_cosmetics_parse_as_before_and_junk_is_none()
    {
        var chess = PbpLobbyApi.Parse("{\"players\":[{\"id\":\"p_1\",\"name\":\"ann\",\"time_control\":{\"initial_ms\":600000,\"increment_ms\":0},\"since_ms\":1}]}");
        Assert.Same(PlayerLook.None, chess.Open.Single().Look);
        var junk = PbpLobbyApi.Parse("{\"players\":[{\"id\":\"p_1\",\"name\":\"ann\",\"time_control\":{\"initial_ms\":600000,\"increment_ms\":0},\"since_ms\":1,\"cosmetics\":[1]}]}");
        Assert.Same(PlayerLook.None, junk.Open.Single().Look);
        if (ShopCosmetics.FrameIds()?.Contains("profile.frame.lace") == true)
        {
            var worn = PbpLobbyApi.Parse("{\"players\":[{\"id\":\"p_1\",\"name\":\"ann\",\"time_control\":{\"initial_ms\":600000,\"increment_ms\":0},\"since_ms\":1,\"cosmetics\":{\"frame_id\":\"profile.frame.lace\"}}]}");
            Assert.Equal("profile.frame.lace", worn.Open.Single().Look.FrameId);
        }
    }

    [Fact]
    public void The_row_view_shows_a_face_only_for_a_worn_look_at_the_one_frame_geometry()
    {
        var gates = LobbyGates.From(true, true, true);
        var bare = new LobbyRowView(new LobbyRow { Game = LobbyGame.Chess, HostName = "zed" }, gates, k => k);
        Assert.False(bare.HasLook);
        Assert.Null(bare.FaceBrush);
        Assert.Null(bare.FrameArt);
        var worn = new LobbyRowView(new LobbyRow { Game = LobbyGame.Chess, HostName = "ann", Look = Gilt }, gates, k => k);
        Assert.True(worn.HasLook);
        Assert.Equal(LobbyRowView.FaceSize / ShopCosmetics.FrameWindowRatio, worn.FrameSize, 6);
        Assert.Equal(-(worn.FrameSize - LobbyRowView.FaceSize) / 2, worn.FrameMargin.Left, 6);
    }

    [Fact]
    public void The_lobby_card_template_draws_the_face_over_the_art()
    {
        var xaml = System.IO.File.ReadAllText(System.IO.Path.Combine(RepoRoot(), "ConditioningControlPanel", "Views", "Tabs", "AvailableSubjectsTabView.xaml"));
        Assert.Contains("Tag=\"lobby-face\"", xaml);
        Assert.Contains("Visibility=\"{Binding HasLook, Converter={StaticResource BoolToVisibility}}\"", xaml);
        Assert.Contains("Source=\"{Binding FrameArt}\"", xaml);
        Assert.Contains("Fill=\"{Binding FaceBrush}\"", xaml);
    }

    private static string RepoRoot()
    {
        var dir = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !System.IO.File.Exists(System.IO.Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
