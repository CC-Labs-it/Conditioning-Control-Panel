using System;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Billboard;
using ConditioningControlPanel.Services.Billboard.Providers;
using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Core.Tests;

public class LiveCardsTests
{
    private static readonly LobbyRow Chess = new() { Game = LobbyGame.Chess, State = LobbyRowState.Open, Key = "p_x", HostName = "A" };
    private static BillboardCardSpec? Card(LobbySnapshot snapshot) => LiveCards.Decide(snapshot, k => k);

    [Fact]
    public void Signed_out_and_empty_snapshots_are_silent()
    {
        Assert.Null(Card(LobbySnapshot.Empty));
        Assert.Null(Card(new(new[] { Chess }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), false)));
        Assert.Null(Card(LobbySnapshot.Empty with { SignedIn = true }));
    }

    [Fact]
    public void Friend_wins_and_a_single_game_joins_but_multiple_tables_open_lobby()
    {
        var friend = Chess with { Game = LobbyGame.Goon, Key = "ABCD", Friend = true };
        var snap = new LobbySnapshot(new[] { Chess }, Array.Empty<LobbyRow>(), new[] { friend }, true);
        Assert.Equal("live.friend", Card(snap)!.Id);
        Assert.Equal("join:goon:ABCD", Card(snap)!.Action.Target);
        var single = Card(snap with { Friends = Array.Empty<LobbyRow>() })!;
        Assert.Equal((BillboardActionKind.Callback, "join:chess:p_x"), (single.Action.Kind, single.Action.Target));
        var many = Card(snap with { Friends = Array.Empty<LobbyRow>(), Open = new[] { Chess, Chess } })!;
        Assert.Equal((BillboardActionKind.Tab, "availablesubjects"), (many.Action.Kind, many.Action.Target));
    }

    [Fact]
    public void Remote_claims_and_playing_friends_use_the_lobby()
    {
        var snap = new LobbySnapshot(new[] { Chess with { Game = LobbyGame.Remote } }, Array.Empty<LobbyRow>(), Array.Empty<LobbyRow>(), true);
        Assert.Equal(BillboardActionKind.Tab, Card(snap)!.Action.Kind);
        var playing = Chess with { State = LobbyRowState.Playing, Key = null, Friend = true };
        var card = Card(snap with { Open = Array.Empty<LobbyRow>(), Friends = new[] { playing } })!;
        Assert.Equal("live.playing", card.Id);
        Assert.Equal("availablesubjects", card.Action.Target);
    }

    [Theory]
    [InlineData("join:chess:p_x", true)]
    [InlineData("join:goon:ABCD", true)]
    [InlineData("join:remote:id", false)]
    [InlineData("join:chess:", false)]
    [InlineData("other:goon:ABCD", false)]
    [InlineData(null, false)]
    public void Only_chess_and_goon_callbacks_are_accepted(string? target, bool valid)
    {
        Assert.Equal(valid, LiveCards.TryParseTarget(target, out var game, out var key));
        if (valid) Assert.Equal(target, LiveCards.Target(game, key));
    }
}
