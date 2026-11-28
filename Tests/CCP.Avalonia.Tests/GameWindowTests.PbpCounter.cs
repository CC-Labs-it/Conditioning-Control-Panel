using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Games;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class GameWindowTests
{
    [Fact]
    public void PieceByPiece_ClosingBoardStopsCounterWatch() => RunBoard(() =>
    {
        var board = GameWindow.Launch(GameWindow.PbpId)!;
        board.HandleMessage("{\"type\":\"open-prize-counter\"}");
        var counter = GameWindow.OpenForTests(GameWindow.PrizeCounterId)!;
        var sent = new List<JObject>();
        board.Posted += json => sent.Add(JObject.Parse(json));
        board.Close();
        counter.CloseFor("back");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(sent);
    });

    [Theory]
    [InlineData("back", true)]
    [InlineData("panic", false)]
    public void PieceByPiece_CounterDoorReturnsWardrobeAndFocus(string reason, bool focus) => RunBoard(() =>
    {
        var board = GameWindow.Launch(GameWindow.PbpId)!;
        board.Web.Focusable = true;
        board.PbpOsAnimations = () => Task.FromResult(true);
        var sent = new List<JObject>();
        board.Posted += json => sent.Add(JObject.Parse(json));
        board.HandleMessage("{\"type\":\"ready\"}");
        board.HandleMessage("{\"type\":\"open-prize-counter\"}");
        var counter = GameWindow.OpenForTests(GameWindow.PrizeCounterId)!;
        Assert.NotNull(counter);
        var counterSent = new List<JObject>();
        counter.Posted += json => counterSent.Add(JObject.Parse(json));
        counter.HandleMessage("{\"type\":\"ready\"}");
        var init = counterSent.Single(x => (string?)x["type"] == "init");
        Assert.Equal("chess", (string?)init["tab"]);
        Assert.Equal("piecebypiece", (string?)init["from"]);
        board.HandleMessage("{\"type\":\"open-prize-counter\"}");
        Assert.Same(counter, GameWindow.OpenForTests(GameWindow.PrizeCounterId));
        var other = new Button();
        var panel = new StackPanel();
        board.Content = null;
        panel.Children.Add(board.Web);
        panel.Children.Add(other);
        board.Content = panel;
        Assert.True(other.Focus());
        sent.Clear();
        counter.CloseFor(reason);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[] { "owned", "pbp:look" }, sent.Select(x => (string?)x["type"]).Where(t => t is "owned" or "pbp:look"));
        Assert.Equal(focus, board.Web.IsFocused);
    });
}
