using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class SkillPurchaseTests
{
    private sealed class Wire(Func<int, string> reply) : HttpMessageHandler
    {
        public int Calls;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls++;
            Assert.Equal("/v2/user/purchase-skill", request.RequestUri!.AbsolutePath);
            var body = JObject.Parse(await request.Content!.ReadAsStringAsync(ct));
            Assert.Equal("test-account", (string?)body["unified_id"]);
            Assert.Equal("pink_hours", (string?)body["skill_id"]);
            return new(HttpStatusCode.OK) { Content = new StringContent(reply(Calls)) };
        }
    }

    [Fact]
    public async Task SuccessAdoptsServerWalletAndUnionsSkills()
    {
        var s = new AppSettings { UnifiedId = "test-account", SkillPoints = 100 };
        s.UnlockedSkills.Add("eternal_doll");
        using var wire = new Wire(_ => "{\"success\":true,\"skill_points\":97,\"unlocked_skills\":[\"pink_hours\"],\"lifetime_points_spent\":100}");
        var result = await new V2AuthService(() => s, wire).PurchaseSkillAsync("pink_hours", () => Task.FromResult(false));
        Assert.True(result.Success);
        Assert.Equal(97, s.SkillPoints);
        Assert.Contains("pink_hours", s.UnlockedSkills);
        Assert.Contains("eternal_doll", s.UnlockedSkills);
        Assert.Equal(100, result.LifetimeSpent);
    }

    [Theory]
    [InlineData(true, 2, 0)]
    [InlineData(false, 1, 100)]
    public async Task BalanceRefusalSyncsOnceAndOnlyAdoptsAfterSuccessfulSync(bool synced, int calls, int balance)
    {
        var s = new AppSettings { UnifiedId = "test-account", SkillPoints = 100 };
        using var wire = new Wire(_ => "{\"success\":false,\"skill_points\":0,\"error\":\"balance\"}");
        int syncs = 0;
        var result = await new V2AuthService(() => s, wire).PurchaseSkillAsync("pink_hours", () => { syncs++; return Task.FromResult(synced); });
        Assert.False(result.Success);
        Assert.Equal(calls, wire.Calls);
        Assert.Equal(1, syncs);
        Assert.Equal(balance, s.SkillPoints);
        Assert.Empty(s.UnlockedSkills);
    }

    [Fact]
    public async Task SignedOutNeverSendsPurchase()
    {
        var s = new AppSettings { SkillPoints = 100 };
        using var wire = new Wire(_ => throw new Exception("must not send"));
        Assert.False((await new V2AuthService(() => s, wire).PurchaseSkillAsync("pink_hours", () => Task.FromResult(false))).Success);
        Assert.Equal(0, wire.Calls);
    }
}
