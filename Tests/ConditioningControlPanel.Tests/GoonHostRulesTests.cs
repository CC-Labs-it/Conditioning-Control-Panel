using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The peer pool's online switch and niche-name rules. Both implementations live
/// in CCP.Core, with broader coverage in CCP.Core.Tests.</summary>
public class GoonHostRulesTests
{
    [Theory]
    [InlineData(null, true)]    // never set: joining a Scrolller game is the opt-in
    [InlineData(true, true)]
    [InlineData(false, false)]  // switched off for the Goon Game: declined, own pictures stand in
    public void The_peer_pool_follows_the_players_own_online_switch(bool? online, bool fetch)
        => Assert.Equal(fetch, GoonHostRules.PeerFetchAllowed(online));

    [Fact]
    public void The_peer_pool_takes_only_clean_niche_names()
    {
        var subs = GoonOnlineMediaRules.CleanSubs(new[] { "Good_one", "https://evil.example/x", "a", "good_ONE", "../etc" });
        Assert.Equal(new[] { "Good_one" }, subs);
    }
}
