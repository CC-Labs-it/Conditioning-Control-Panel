using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The 401 path (WPF ProfileSyncService.HandleUnauthorizedAsync) and its two V2 callers here, rename and
/// delete (ProfileSyncService.cs:4159/4207): restore-session, then a provider re-validate, each on its own
/// cooldown; a failed recovery keeps the token; delete retries ONCE after a real recovery, rename never.
/// Adapted from parity/trunk bb302d777. The auth token lives in CoreSecrets, so this runs in that collection.
/// </summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class AuthRecoveryTests : IDisposable
{
    private sealed class Fake : HttpMessageHandler
    {
        public readonly Queue<(HttpStatusCode, string)> Answers = new();
        public readonly List<(string Path, string? Token)> Calls = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Calls.Add((request.RequestUri!.AbsolutePath, request.Headers.TryGetValues("X-Auth-Token", out var v) ? v.First() : null));
            var (code, body) = Answers.Dequeue();
            return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
        }
    }

    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly AppSettings _settings;
    private readonly Fake _http = new();
    private DateTime _now = new(2026, 10, 10, 12, 0, 0, DateTimeKind.Utc);

    public AuthRecoveryTests()
    {
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        _settings = new() { UnifiedId = "u_abc123", AuthToken = "tok" };
        AuthRecovery.ResetForTest();
        AuthRecovery.SettingsForTest = () => _settings;
        AuthRecovery.HandlerForTest = _http;   // the same wire for the V2 call and the recovery
        AuthRecovery.Now = () => _now;
        AuthRecovery.ProviderRevalidate = null;
        AuthRecovery.Recovered = null;
    }

    public void Dispose()
    {
        AuthRecovery.ResetForTest();
        AuthRecovery.ProviderRevalidate = null;
        AuthRecovery.Recovered = null;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private V2AuthService Client() => new(() => _settings, _http);
    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{}") };

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SkillPurchaseRetriesOnlyAfterGenuineRecovery(bool recovered)
    {
        _settings.SkillPoints = 100;
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        _http.Answers.Enqueue(recovered
            ? (HttpStatusCode.OK, "{\"auth_token\":\"fresh\"}")
            : (HttpStatusCode.Unauthorized, "{}"));
        if (recovered) _http.Answers.Enqueue((HttpStatusCode.OK,
            "{\"success\":true,\"skill_points\":98,\"unlocked_skills\":[\"pink_hours\"]}"));
        var result = await Client().PurchaseSkillAsync("pink_hours", () => Task.FromResult(false));
        Assert.Equal(recovered, result.Success);
        Assert.Equal(recovered ? 3 : 2, _http.Calls.Count);
        Assert.Equal(recovered ? 98 : 100, _settings.SkillPoints);
        if (recovered) Assert.Equal(("/v2/user/purchase-skill", "fresh"), _http.Calls.Last());
    }

    [Fact]
    public async Task AnythingButA401IsNotRecovered()
    {
        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Forbidden)));
        Assert.Empty(_http.Calls);
    }

    [Fact]
    public async Task RestoreSessionConfirmsTheTokenAndAdoptsARotatedOne()
    {
        var recovered = 0;
        AuthRecovery.Recovered = () => recovered++;
        _http.Answers.Enqueue((HttpStatusCode.OK, "{\"auth_token\":\"fresh\"}"));

        Assert.True(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal(("/v2/auth/restore-session", "tok"), _http.Calls.Single());
        Assert.Equal("fresh", _settings.AuthToken);
        Assert.Equal(1, recovered);
    }

    [Fact]
    public async Task AFailedRecoveryKeepsTheTokenAndWaitsOutItsCooldowns()
    {
        var providerAsks = 0;
        AuthRecovery.ProviderRevalidate = () => { providerAsks++; return Task.FromResult(true); };   // validates, mints nothing
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));

        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal("tok", _settings.AuthToken);
        Assert.Equal(1, providerAsks);

        // A burst inside the cooldowns asks nobody.
        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Single(_http.Calls);
        Assert.Equal(1, providerAsks);

        // Past 30 s restore-session may ask again; the provider waits out its 2 minutes.
        _now = _now.AddSeconds(31);
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        Assert.False(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal(2, _http.Calls.Count);
        Assert.Equal(1, providerAsks);
    }

    [Fact]   // WPF JObject.Parse threw on it, so the provider re-validate got its turn (P28)
    public async Task AnUnparseable200FromRestoreSession_FallsThroughToTheProvider()
    {
        AuthRecovery.ProviderRevalidate = () => { _settings.AuthToken = "minted"; return Task.FromResult(true); };
        _http.Answers.Enqueue((HttpStatusCode.OK, ""));
        Assert.True(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal("minted", _settings.AuthToken);
    }

    [Fact]
    public async Task AProviderThatMintsAFreshTokenRecovers()
    {
        AuthRecovery.ProviderRevalidate = () => { _settings.AuthToken = "minted"; return Task.FromResult(true); };
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        Assert.True(await AuthRecovery.HandleUnauthorizedAsync(Status(HttpStatusCode.Unauthorized)));
        Assert.Equal("minted", _settings.AuthToken);
    }

    [Fact]
    public async Task Delete401_RecoversThenRetriesOnceWithTheRecoveredToken()
    {
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{\"error\":\"bad token\"}"));
        _http.Answers.Enqueue((HttpStatusCode.OK, "{\"auth_token\":\"fresh\"}"));
        _http.Answers.Enqueue((HttpStatusCode.OK, "{\"success\":true}"));

        var (success, error) = await Client().DeleteAccountAsync("u_abc123");

        Assert.True(success, error);
        Assert.Equal(new[] { ("/v2/user/delete-account", (string?)"tok"), ("/v2/auth/restore-session", "tok"),
                             ("/v2/user/delete-account", "fresh") }, _http.Calls);
    }

    [Fact]
    public async Task Delete401_WithoutARecovery_DoesNotRetryAndShowsTheServersText()
    {
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{\"error\":\"bad token\"}"));
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));   // restore-session refuses; no provider

        var (success, error) = await Client().DeleteAccountAsync("u_abc123");

        Assert.False(success);
        Assert.Equal("bad token", error);
        Assert.Equal(2, _http.Calls.Count);
        Assert.Equal("tok", _settings.AuthToken);
    }

    [Fact]
    public async Task Rename401_RecoversTheSessionButDoesNotRetry()
    {
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{\"error\":\"bad token\"}"));
        _http.Answers.Enqueue((HttpStatusCode.OK, "{\"auth_token\":\"fresh\"}"));

        var (success, error, _) = await Client().ChangeDisplayNameAsync("u_abc123", "Bambi");

        Assert.False(success);
        Assert.Equal("bad token", error);
        Assert.Equal(new[] { "/v2/user/change-display-name", "/v2/auth/restore-session" }, _http.Calls.Select(c => c.Path));
        Assert.Equal("fresh", _settings.AuthToken);   // the next rename goes out with the recovered token
    }
}
