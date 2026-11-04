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
    private readonly string _oldLanguage = ConditioningControlPanel.Localization.LocalizationManager.Instance.CurrentLanguage;
    public SkillPurchaseTests() { ConditioningControlPanel.Localization.LocalizationManager.Instance.Initialize("en"); CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n); CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v; }
    public void Dispose() { CoreSecrets.RetrieveProvider = _oldGet; CoreSecrets.StoreProvider = _oldSet; ConditioningControlPanel.Localization.LocalizationManager.Instance.SetLanguage(_oldLanguage); }

    private sealed class Fake : HttpMessageHandler
    {
        public int Calls;
        public bool Throw;
        public Func<int, (HttpStatusCode status, string response)>? Reply;
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
            var (status, response) = Reply?.Invoke(Calls) ?? (Status, Response);
            return new HttpResponseMessage(status) { Content = new StringContent(response) };
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
    public async Task UnauthorizedRetriesOnceOnlyAfterRecoveryForTheSameAccount()
    {
        var s = Settings();
        var fake = new Fake { Reply = n => n == 1 ? (HttpStatusCode.Unauthorized, "{}") : (HttpStatusCode.OK, "{\"success\":true,\"skill_points\":98,\"unlocked_skills\":[\"pink_hours\"]}") };
        var purchase = new SkillPurchase(fake) { Recover = _ => { s.AuthToken = "new"; return Task.FromResult(true); } };
        Assert.True((await purchase.BuyAsync(s, "pink_hours")).success);
        Assert.Equal(2, fake.Calls); Assert.Equal("new", fake.Token);
        fake.Calls = 0; purchase.Recover = _ => Task.FromResult(false);
        Assert.False((await purchase.BuyAsync(s, "pink_hours")).success); Assert.Equal(1, fake.Calls);
        fake.Calls = 0; purchase.Recover = _ => { s.UnifiedId = "different"; return Task.FromResult(true); };
        Assert.False((await purchase.BuyAsync(s, "pink_hours")).success); Assert.Equal(1, fake.Calls);
    }

    [Fact]
    public async Task MergedResponseDelegatesAndStopsWithoutRetry()
    {
        var old = V2AuthService.MergedRecovery;
        try
        {
            int handled = 0;
            V2AuthService.MergedRecovery = (response, body) => { handled++; return Task.FromResult(true); };
            var fake = new Fake { Status = HttpStatusCode.Conflict, Response = "{}" };
            var result = await new SkillPurchase(fake).BuyAsync(Settings(), "pink_hours");
            Assert.False(result.success); Assert.Equal(1, handled); Assert.Equal(1, fake.Calls);
            Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("account_merged_retry_hint"), result.error);
        }
        finally { V2AuthService.MergedRecovery = old; }
    }

    [Fact]
    public async Task FallbackErrorsUseLocalizationKeys()
    {
        var s = Settings(); var fake = new Fake { Status = HttpStatusCode.BadRequest, Response = "broken" };
        var purchase = new SkillPurchase(fake);
        Assert.Equal(ConditioningControlPanel.Localization.Loc.GetF("skill_purchase_server_error", 400), (await purchase.BuyAsync(s, "pink_hours")).error);
        fake.Status = HttpStatusCode.OK; fake.Response = "{}";
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("skill_purchase_failed"), (await purchase.BuyAsync(s, "pink_hours")).error);
        fake.Response = "{\"success\":true}";
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("skill_purchase_invalid_response"), (await purchase.BuyAsync(s, "pink_hours")).error);
        fake.Throw = true;
        Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("skill_purchase_connection_failed"), (await purchase.BuyAsync(s, "pink_hours")).error);
    }

    [Theory]
    [InlineData("en")][InlineData("de")][InlineData("es")][InlineData("fr")][InlineData("it")]
    [InlineData("ja")][InlineData("ko")][InlineData("pt-BR")][InlineData("ru")][InlineData("zh-CN")]
    public void PurchaseFallbacksExistInEveryLanguage(string language)
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Localization", "Languages", language + ".json");
        var strings = Newtonsoft.Json.Linq.JObject.Parse(System.IO.File.ReadAllText(path));
        foreach (var key in new[] { "skill_purchase_server_error", "skill_purchase_failed", "skill_purchase_invalid_response", "skill_purchase_connection_failed" })
            Assert.False(string.IsNullOrWhiteSpace((string?)strings[key]));
        Assert.Contains("{0}", (string)strings["skill_purchase_server_error"]!);
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
