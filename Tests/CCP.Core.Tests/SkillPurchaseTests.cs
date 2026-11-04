using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

[Collection(CoreSecretsStatics.Name)]
public sealed class SkillPurchaseTests : IDisposable
{
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    public SkillPurchaseTests() { CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n); CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v; }
    public void Dispose() { CoreSecrets.RetrieveProvider = _oldGet; CoreSecrets.StoreProvider = _oldSet; }

    private sealed class Fake : HttpMessageHandler
    {
        public int Calls;
        public bool Throw;
        public string? Body, Token, Url;
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Response = "";
        public TaskCompletionSource<bool>? Wait;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            if (Throw) throw new HttpRequestException();
            Url = request.RequestUri!.AbsoluteUri;
            Token = request.Headers.TryGetValues("X-Auth-Token", out var values) ? System.Linq.Enumerable.Single(values) : null;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            if (Wait != null) await Wait.Task;
            return new HttpResponseMessage(Status) { Content = new StringContent(Response) };
        }
    }
    private static AppSettings Settings() => new() { UnifiedId = "u", AuthToken = "token", SkillPoints = 100, NightTimeUsageCount = 10 };

    [Theory]
    [InlineData("pink_hours")]
    [InlineData("night_shift")]
    public async Task SendsPurchasableNormalAndSecretSkillsAndReturnsServerValues(string id)
    {
        var s = Settings(); var fake = new Fake { Response = "{\"success\":true,\"skill_points\":88,\"unlocked_skills\":[\"" + id + "\"],\"lifetime_points_spent\":12}" };
        var result = await new SkillPurchase(fake).BuyAsync(s, id);
        Assert.True(result.success); Assert.Equal(88, result.points); Assert.Contains(id, result.skills!); Assert.Equal(12, result.lifetimeSpent);
        Assert.Equal("https://codebambi-proxy.vercel.app/v2/user/purchase-skill", fake.Url);
        Assert.Equal("token", fake.Token);
        Assert.Equal("{\"unified_id\":\"u\",\"skill_id\":\"" + id + "\",\"skill_points\":100}", fake.Body);
        Assert.Equal(100, s.SkillPoints); Assert.Empty(s.UnlockedSkills);
    }

    [Fact]
    public async Task RefusesInvalidAndSignedOutBeforeSending()
    {
        var s = Settings(); var fake = new Fake(); var purchase = new SkillPurchase(fake);
        Assert.False((await purchase.BuyAsync(s, "not_a_skill")).success);
        s.NightTimeUsageCount = 0;
        Assert.False((await purchase.BuyAsync(s, "night_shift")).success);
        s.UnifiedId = null;
        Assert.False((await purchase.BuyAsync(s, "pink_hours")).success);
        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task RejectsErrorsAndIncompleteSuccessWithoutChangingSettings()
    {
        var s = Settings(); var fake = new Fake { Status = HttpStatusCode.BadRequest, Response = "{\"error\":\"denied\",\"skill_points\":0}" }; var purchase = new SkillPurchase(fake);
        Assert.Equal("denied", (await purchase.BuyAsync(s, "pink_hours")).error);
        fake.Status = HttpStatusCode.OK; fake.Response = "{\"success\":false,\"error\":\"rejected\"}";
        Assert.Equal("rejected", (await purchase.BuyAsync(s, "pink_hours")).error);
        fake.Response = "{\"success\":true,\"skill_points\":0,\"unlocked_skills\":[]}";
        Assert.False((await purchase.BuyAsync(s, "pink_hours")).success);
        Assert.Equal(100, s.SkillPoints); Assert.Empty(s.UnlockedSkills);
    }

    [Fact]
    public async Task NetworkFailureDoesNotMutateSettingsAndReopensGate()
    {
        var s = Settings(); var fake = new Fake { Throw = true }; var purchase = new SkillPurchase(fake);
        var result = await purchase.BuyAsync(s, "pink_hours");
        Assert.False(result.success); Assert.Contains("Connection failed", result.error);
        Assert.Null(result.points); Assert.Empty(s.UnlockedSkills); Assert.Equal(100, s.SkillPoints);
        fake.Throw = false; fake.Response = "{\"success\":true,\"skill_points\":98,\"unlocked_skills\":[\"pink_hours\"]}";
        Assert.True((await purchase.BuyAsync(s, "pink_hours")).success);
    }

    [Fact]
    public async Task OnlyOneRequestAtATimeAndGateReopens()
    {
        var s = Settings(); var fake = new Fake { Wait = new TaskCompletionSource<bool>(), Response = "{\"success\":true,\"skill_points\":98,\"unlocked_skills\":[\"pink_hours\"]}" }; var purchase = new SkillPurchase(fake);
        var first = purchase.BuyAsync(s, "pink_hours");
        Assert.False((await purchase.BuyAsync(s, "pink_hours")).success);
        Assert.Equal(1, fake.Calls);
        fake.Wait.SetResult(true); Assert.True((await first).success);
        Assert.True((await purchase.BuyAsync(s, "pink_hours")).success); Assert.Equal(2, fake.Calls);
    }
}
