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

    // WPF ProfileSyncService.cs:4668: a backup status 401 recovers for next time, never retries.
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BackupStatusRecoversWithoutRetryAndSharesCooldowns(bool restoreWorks)
    {
        var restarted = 0;
        var providers = 0;
        AuthRecovery.Recovered = () => restarted++;
        AuthRecovery.ProviderRevalidate = () =>
        {
            providers++;
            _settings.AuthToken = "provider-token";
            return Task.FromResult(true);
        };
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        _http.Answers.Enqueue((restoreWorks ? HttpStatusCode.OK : HttpStatusCode.Unauthorized,
            restoreWorks ? "{\"auth_token\":\"restored-token\"}" : "{}"));
        var client = new CloudSettingsBackup(_http);
        Assert.Null(await client.GetInfoAsync(_settings));
        Assert.Equal(new[] { "/v2/user/settings-backup", "/v2/auth/restore-session" },
            _http.Calls.Select(c => c.Path));
        Assert.Equal(restoreWorks ? "restored-token" : "provider-token", _settings.AuthToken);
        Assert.Equal(restoreWorks ? 0 : 1, providers);
        Assert.Equal(1, restarted);
        // A second status failure uses the recovered token but cannot repeat the restore.
        // In the restore-success case the independent provider cooldown is still available.
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        Assert.Null(await client.GetInfoAsync(_settings));
        Assert.Equal(3, _http.Calls.Count);
        Assert.Equal(restoreWorks ? "restored-token" : "provider-token", _http.Calls[2].Token);
        Assert.Equal(1, providers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BackupAndRestoreRecoverWithoutRetry(bool upload)
    {
        if (upload) _http.Answers.Enqueue((HttpStatusCode.OK, "{\"backup\":null}"));
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        _http.Answers.Enqueue((HttpStatusCode.OK, "{\"auth_token\":\"fresh\"}"));
        var client = new CloudSettingsBackup(_http);
        if (upload) Assert.False(await client.BackupAsync(_settings));
        else Assert.Null(await client.DownloadAsync(_settings));
        Assert.Equal("fresh", _settings.AuthToken);
        Assert.Equal(upload ? 3 : 2, _http.Calls.Count);
        Assert.Equal("/v2/auth/restore-session", _http.Calls.Last().Path);
    }

    [Fact]
    public async Task BackupStatusFailedRecoveryKeepsToken()
    {
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        _http.Answers.Enqueue((HttpStatusCode.Unauthorized, "{}"));
        Assert.Null(await new CloudSettingsBackup(_http).GetInfoAsync(_settings));
        Assert.Equal("tok", _settings.AuthToken);
        Assert.Equal(2, _http.Calls.Count);
    }

    private V2AuthService Client() => new(() => _settings, _http);
    private static HttpResponseMessage Status(HttpStatusCode code) => new(code) { Content = new StringContent("{}") };

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
