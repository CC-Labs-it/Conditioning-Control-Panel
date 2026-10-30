using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public void Profile_load_renews_fresh_Patreon_grace_without_a_tier_rise() => WithFreshInstall(async () =>
    {
        var at = DateTime.UtcNow.AddHours(-1);
        var profile = JObject.Parse(L40Profile("u1"));
        profile["user"]!["patreon_is_active"] = true;
        profile["user"]!["patreon_tier"] = 2;
        profile["user"]!["effective_tier"] = 2;
        profile["user"]!["patreon_confirmed_at"] = at.ToString("O");
        SignIn("u1", profile.ToString());
        var s = CoreSettings.Current;
        s.PatreonTier = 2;
        s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = at.AddDays(-1);
        Assert.True(await AccountSeed.LoadProfileAsync());
        Assert.Equal(at.AddDays(14), s.PatreonPremiumValidUntil);
        Assert.Equal(at.AddDays(14), s.PatreonLabValidUntil);
    });
}
