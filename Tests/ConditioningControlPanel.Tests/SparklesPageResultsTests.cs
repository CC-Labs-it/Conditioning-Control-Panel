using ConditioningControlPanel.Services.Sparkles;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>B1 W3: chess, Goon and Breakout pages tell the host how a game ended; these readers
/// decide what is claimed (and what is not) from frames the page may get wrong.</summary>
public class SparklesPageResultsTests
{
    [Theory]
    [InlineData(0, "gold")]
    [InlineData(1, "silver")]
    [InlineData(2, "bronze")]
    [InlineData(3, "none")]
    [InlineData(-4, "gold")]
    public void BreakoutBallsLostMapToMedals(int lost, string medal) =>
        Assert.Equal(medal, SparklesGameEnd.BreakoutMedal(lost));

    [Fact]
    public void ChessVsComputerClaimsSoloWithPlies()
    {
        var c = SparklesPageResults.Chess(JObject.Parse(
            "{\"type\":\"pbp:game-over\",\"mode\":\"solo\",\"outcome\":\"win\",\"plies\":44,\"durationMs\":600000,\"reason\":\"checkmate\"}"));
        Assert.NotNull(c);
        Assert.Equal(("chess", "solo", "win"), (c!.Game, c.Mode, c.Result.Outcome));
        Assert.Equal(44, (int)c.Detail!["plies"]!);
        Assert.Equal(600, c.Duration.TotalSeconds);
        Assert.Null(c.Proof);
    }

    [Fact]
    public void ChessOnlineClaimsWithTheMatchIdAsProof()
    {
        var c = SparklesPageResults.Chess(JObject.Parse(
            "{\"mode\":\"online\",\"outcome\":\"draw\",\"plies\":30,\"durationMs\":1,\"reason\":\"agreement\",\"matchId\":\"m_abc123\"}"));
        Assert.Equal("online", c!.Mode);
        Assert.Equal("m_abc123", (string?)c.Proof!["matchId"]);
    }

    [Theory]
    [InlineData("{\"mode\":\"hotseat\",\"outcome\":\"win\"}")]
    [InlineData("{\"mode\":\"watch\",\"outcome\":\"win\"}")]
    [InlineData("{\"mode\":\"solo\",\"outcome\":null}")]
    [InlineData("{\"mode\":\"online\",\"outcome\":\"win\",\"reason\":\"abandon\",\"matchId\":\"m1\"}")]
    [InlineData("{\"mode\":\"online\",\"outcome\":\"win\",\"matchId\":null}")]
    [InlineData("{\"mode\":\"online\",\"outcome\":\"win\",\"matchId\":{\"x\":1}}")]
    [InlineData("{\"mode\":{\"a\":1},\"outcome\":[1]}")]
    public void ChessClaimsNothingForTheseFrames(string json) =>
        Assert.Null(SparklesPageResults.Chess(JObject.Parse(json)));

    [Fact]
    public void ChessReadsJunkFieldsSafely()
    {
        var c = SparklesPageResults.Chess(JObject.Parse(
            "{\"mode\":\"solo\",\"outcome\":\"loss\",\"plies\":\"many\",\"durationMs\":-5}"));
        Assert.Equal(0, (int)c!.Detail!["plies"]!);
        Assert.Equal(0, c.Duration.TotalMilliseconds);
    }

    private static JObject Goon(string result, bool practice = false, string code = "ABCD12") => JObject.Parse(
        "{\"type\":\"match-result\",\"practice\":" + (practice ? "true" : "false") + ",\"code\":\"" + code + "\",\"result\":" + result + "}");

    [Fact]
    public void GoonDuelClaimsOnlineWithTheRoomCode()
    {
        var c = SparklesPageResults.Goon(Goon("{\"endReason\":1,\"winnerIsHost\":false,\"localIsHost\":false,\"survivedMs\":240000}"));
        Assert.Equal(("goon", "online", "win"), (c!.Game, c.Mode, c.Result.Outcome));
        Assert.Equal("ABCD12", (string?)c.Proof!["goonMatchId"]);
        Assert.Equal(240, c.Duration.TotalSeconds);
        var lost = SparklesPageResults.Goon(Goon("{\"endReason\":1,\"winnerIsHost\":true,\"localIsHost\":false}"));
        Assert.Equal("loss", lost!.Result.Outcome);
        var draw = SparklesPageResults.Goon(Goon("{\"endReason\":3,\"winnerIsHost\":null,\"localIsHost\":true}"));
        Assert.Equal("draw", draw!.Result.Outcome);
    }

    [Fact]
    public void GoonPracticeClaimsPracticeWithNoProof()
    {
        var c = SparklesPageResults.Goon(Goon("{\"endReason\":1,\"winnerIsHost\":true,\"localIsHost\":true}", practice: true, code: ""));
        Assert.Equal(("practice", "win"), (c!.Mode, c.Result.Outcome));
        Assert.Null(c.Proof);
    }

    [Theory]
    [InlineData("{\"endReason\":0,\"winnerIsHost\":true,\"localIsHost\":true}", false)]   // mercy
    [InlineData("{\"endReason\":2,\"winnerIsHost\":true,\"localIsHost\":true}", false)]   // abandon
    [InlineData("{\"endReason\":1,\"winnerIsHost\":true,\"localIsHost\":true,\"countsForLedger\":false}", false)]
    [InlineData("{\"endReason\":1,\"winnerIsHost\":null,\"localIsHost\":true}", false)]
    [InlineData("{\"endReason\":1,\"winnerIsHost\":true,\"localIsHost\":true,\"disputed\":true}", true)]
    public void GoonClaimsNothingForTheseResults(string result, bool practice) =>
        Assert.Null(SparklesPageResults.Goon(Goon(result, practice)));

    [Theory]
    [InlineData("")]
    [InlineData("abcd")]
    [InlineData("ABC")]
    public void AGoonDuelWithoutARoomCodeClaimsNothing(string code) =>
        Assert.Null(SparklesPageResults.Goon(Goon("{\"endReason\":3,\"localIsHost\":true}", code: code)));

    [Fact]
    public void BreakoutWallClaimsAMedalPerWall()
    {
        var c = SparklesPageResults.BreakoutWall(JObject.Parse(
            "{\"type\":\"breakout-wall\",\"walls\":3,\"ballsLost\":1,\"wallMs\":42000}"));
        Assert.Equal(("breakout", "solo", "silver"), (c!.Game, c.Mode, c.Result.Medal));
        Assert.Equal(1, (int)c.Detail!["ballsLost"]!);
        Assert.Equal(42, c.Duration.TotalSeconds);
    }

    [Theory]
    [InlineData("{\"ballsLost\":0,\"skipped\":true}")]
    [InlineData("{\"ballsLost\":\"0\"}")]
    [InlineData("{\"walls\":2}")]
    public void BreakoutClaimsNothingForTheseWalls(string json) =>
        Assert.Null(SparklesPageResults.BreakoutWall(JObject.Parse(json)));
}
