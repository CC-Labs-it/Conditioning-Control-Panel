using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class AccountSeedConversionTests
{
    private sealed class Reply(string body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }

    [Fact]
    public async Task ProfileLoadRemembersLowerTierAndAdditiveMemberFieldsWithoutRemovingGrace()
    {
        var s = CoreSettings.Current;
        var oldGet = CoreSecrets.RetrieveProvider;
        var oldSet = CoreSecrets.StoreProvider;
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var answered = AccountSeed.ServerAnsweredThisRun;
        AccountSeed.ServerAnsweredThisRun = false;
        var old = (s.UnifiedId, s.AuthToken, s.OfflineMode, s.PatreonTier, s.ServerTierSeen, s.PatreonMember, s.ServerPaidUntil, s.PostGrants, s.PatreonPremiumValidUntil);
        try
        {
            s.UnifiedId = "conversion-test"; s.AuthToken = "sandbox"; s.OfflineMode = false;
            s.PatreonTier = 1; s.ServerTierSeen = null; s.PatreonMember = false;
            var grace = s.PatreonPremiumValidUntil = DateTime.UtcNow.AddDays(3);
            using var reply = new Reply("{\"success\":true,\"user\":{\"effective_tier\":0,\"patreon_member\":true,\"paid_until\":null,\"post_grants\":{\"voice\":\"2030-01-01T00:00:00Z\"}}}");
            Assert.True(await AccountSeed.LoadProfileAsync(new V2AuthService(() => s, reply)));
            Assert.Equal(0, s.ServerTierSeen);
            Assert.True(AccountSeed.ServerAnsweredThisRun);
            Assert.True(s.PatreonMember);
            Assert.True(s.PostGrants.ContainsKey("voice"));
            Assert.Equal(1, s.PatreonTier);
            Assert.Equal(grace, s.PatreonPremiumValidUntil);
        }
        finally
        {
            (s.UnifiedId, s.AuthToken, s.OfflineMode, s.PatreonTier, s.ServerTierSeen, s.PatreonMember, s.ServerPaidUntil, s.PostGrants, s.PatreonPremiumValidUntil) = old;
            AccountSeed.ServerAnsweredThisRun = answered;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
        }
    }
}
