using ConditioningControlPanel.Services.Lobby;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LobbyPageRulesTests
{
    [Theory]
    [InlineData(-1, false)] [InlineData(0, false)] [InlineData(899.99, true)]
    [InlineData(900, false)] [InlineData(901, false)]
    public void NarrowBoundaryMatchesWpf(double width, bool expected) => Assert.Equal(expected, LobbyPageRules.IsNarrowWidth(width));

    [Theory]
    [InlineData(-1, 0)] [InlineData(1, 1)] [InlineData(10, 2)]
    public void ColumnSelectionStaysInsideThreeColumns(int value, int expected) => Assert.Equal(expected, LobbyPageRules.Column(value));

    [Fact]
    public void PlayingChessShowsBothPlayersAndRowIdentityKeepsGameAndState()
    {
        var row = new LobbyRow { Game = LobbyGame.Chess, State = LobbyRowState.Playing, HostName = "White", OpponentName = "Black" };
        Assert.Equal("White vs Black", LobbyPageRules.Title(row, _ => "{0} vs {1}"));
        Assert.Equal("Chess|White|Black|Playing", LobbyPageRules.Id(row));
        Assert.Equal("White", LobbyPageRules.Title(row with { State = LobbyRowState.Open }, _ => "{0} vs {1}"));
        Assert.Equal("lobby_game_remote", LobbyPageRules.LabelKey(LobbyGame.Remote));
    }
}
