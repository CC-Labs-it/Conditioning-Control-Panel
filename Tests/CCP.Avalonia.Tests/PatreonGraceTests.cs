using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
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
    private sealed class PendingPatreonProfile(string profile) : HttpMessageHandler
    {
        internal readonly TaskCompletionSource Requested = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal readonly TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requested.SetResult();
            await Release.Task.WaitAsync(ct);
            return new(HttpStatusCode.OK) { Content = new StringContent(profile) };
        }
    }

    private static string ActivePatreonProfile(DateTime at, int tier)
    {
        var profile = JObject.Parse(L40Profile("u1"));
        profile["user"]!["patreon_is_active"] = true;
        profile["user"]!["patreon_tier"] = tier;
        profile["user"]!["effective_tier"] = tier;
        profile["user"]!["patreon_confirmed_at"] = at.ToString("O");
        return profile.ToString();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Pending_profile_cannot_renew_after_credentials_are_cleared(bool logout) => WithFreshInstall(async () =>
    {
        var profile = ActivePatreonProfile(DateTime.UtcNow.AddHours(-1), 2);
        SignIn("u1", profile);
        var s = CoreSettings.Current;
        s.PatreonTier = 2;
        s.PatreonPremiumValidUntil = s.PatreonLabValidUntil = null;
        using var wire = new PendingPatreonProfile(profile);
        var pending = AccountSeed.LoadProfileAsync(new V2AuthService(() => s, wire));
        try
        {
            await wire.Requested.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (logout) await AccountSeed.Logout();
            else s.AuthToken = null;
        }
        finally { wire.Release.TrySetResult(); }
        await pending;
        Assert.Null(s.PatreonPremiumValidUntil);
        Assert.Null(s.PatreonLabValidUntil);
    });

    [Theory]
    [InlineData(0, 1, 1, false)]
    [InlineData(0, 2, 2, false)]
    [InlineData(1, 2, 2, false)]
    [InlineData(2, 2, 0, false)]
    [InlineData(0, 1, 1, true)]
    public void Renewed_access_notifies_once_at_the_effective_tier(int held, int tier, int raised, bool invite) =>
        WithFreshInstall(async () =>
    {
        SignIn("u1", ActivePatreonProfile(DateTime.UtcNow.AddHours(-1), tier));
        var s = CoreSettings.Current;
        s.PatreonTier = tier;
        s.PatreonPremiumValidUntil = held >= 1 ? DateTime.UtcNow.AddDays(7) : null;
        s.PatreonLabValidUntil = held >= 2 ? DateTime.UtcNow.AddDays(7) : null;
        s.InviteGrantUntil = invite ? DateTime.UtcNow.AddDays(7) : null;
        var events = new List<PatreonTier>();
        void Changed(object? sender, PatreonTier value)
        {
            Assert.True(CoreAccount.HasPremiumAccess);
            if (value == PatreonTier.Level2) Assert.True(CoreAccount.HasLabAccess);
            events.Add(value);
        }
        AccountSeed.Patreon!.TierChanged += Changed;
        try
        {
            Assert.True(await AccountSeed.LoadProfileAsync());
            Assert.Equal(raised == 0 ? Array.Empty<PatreonTier>() : new[] { (PatreonTier)raised }, events);
            Assert.True(await AccountSeed.LoadProfileAsync());
            Assert.Equal(raised == 0 ? 0 : 1, events.Count);
        }
        finally { AccountSeed.Patreon.TierChanged -= Changed; }
    });

}
