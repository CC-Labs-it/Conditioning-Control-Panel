using ConditioningControlPanel.Services.GoonGame;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The opponent's niche names this host fetches. The host and join gates and the
/// online switch moved to CCP.Core (GoonHostRules); their tests live in CCP.Core.Tests.</summary>
public class GoonHostRulesTests
{
    [Fact]
    public void The_peer_pool_takes_only_clean_niche_names()
    {
        var subs = GoonOnlineMediaRules.CleanSubs(new[] { "Good_one", "https://evil.example/x", "a", "good_ONE", "../etc" });
        Assert.Equal(new[] { "Good_one" }, subs);
    }
}
