using ConditioningControlPanel.Services.Friends;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The words a landing throws (WPF FriendsLanding.Sink.SentBeat / Poke), shared by both heads.</summary>
public class FriendsLandingWordsTests
{
    [Theory]
    [InlineData(SendKind.Invite, "friends_land_knock_sent", "knock sent")]
    [InlineData(SendKind.Watch, "friends_land_watch_sent", "watch sent")]
    [InlineData(SendKind.Poke, "friends_land_poke_sent", "sent")]
    public void ASendLeavesWpfsWord(SendKind kind, string key, string english)
        => Assert.Equal((key, english), LandingRules.SentWord(kind));

    [Theory]
    [InlineData(true, false, true)]
    [InlineData(false, false, false)]   // bell off: the Inbox row and nothing else
    [InlineData(true, true, false)]     // a game has the screen: the toast only
    [InlineData(false, true, false)]
    public void APokeThrowsItsWordOnlyWithTheBellOnAndNoGame(bool noticesOn, bool overGame, bool throws)
        => Assert.Equal(throws, LandingRules.PokeThrowsWord(noticesOn, overGame));
}
