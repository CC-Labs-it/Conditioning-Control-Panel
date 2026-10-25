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
using Serilog;
using Serilog.Events;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The Core V2 client on the wire, through a fake handler. Expected requests are transcribed
/// VERBATIM from the pre-move WPF source (ConditioningControlPanel/Services/Account/V2AuthService.cs
/// at b30f8f53a): the same paths, JObject bodies (Newtonsoft indented, Environment.NewLine), the
/// static-ctor headers (X-Client-Version, User-Agent) and X-Auth-Token only where WPF's
/// AddAuthHeader ran. Nothing was run on Windows to record them.
/// </summary>
[Collection(CoreSecretsStatics.Name)]
public sealed class V2AuthServiceWireTests : IDisposable
{
    private const string Url = "https://codebambi-proxy.vercel.app";
    private const string Ver = "6.10.3";
    private const string Tok = "SECRET-auth-token-123";
    private readonly Func<string?>? _oldVersion = CoreReleaseContent.AppVersionProvider;
    private readonly Fake _fake = new();
    private readonly Func<string, string?>? _oldGet = CoreSecrets.RetrieveProvider;
    private readonly Action<string, string?>? _oldSet = CoreSecrets.StoreProvider;
    private readonly Dictionary<string, string?> _secrets = new();
    private readonly AppSettings _settings;

    public V2AuthServiceWireTests()
    {
        CoreReleaseContent.AppVersionProvider = () => Ver;
        // AuthToken is backed by CoreSecrets; an in-memory store stands in for the head's.
        CoreSecrets.RetrieveProvider = n => _secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => _secrets[n] = v;
        _settings = new() { AuthToken = Tok, UnifiedId = "u_abc123" };
    }

    public void Dispose()
    {
        CoreReleaseContent.AppVersionProvider = _oldVersion;
        V2AuthService.MergedRecovery = null;
        CoreSecrets.RetrieveProvider = _oldGet;
        CoreSecrets.StoreProvider = _oldSet;
    }

    private V2AuthService Client() => new(() => _settings, _fake);

    private sealed class Fake : HttpMessageHandler
    {
        public readonly List<string> Seen = new();
        public HttpStatusCode Status = HttpStatusCode.OK;
        public string Body = "{\"success\":true}";
        public Exception? Throw;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        {
            var headers = r.Headers.Concat(r.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
                .Where(h => h.Key != "Content-Length") // computed by HttpContent, not set by the client
                .Select(h => $"{h.Key}: {string.Join(",", h.Value)}").OrderBy(h => h, StringComparer.Ordinal);
            var body = r.Content == null ? "" : await r.Content.ReadAsStringAsync(ct);
            Seen.Add($"{r.Method} {r.RequestUri!.AbsoluteUri}\n{string.Join("\n", headers)}\n\n{body.Replace(Environment.NewLine, "\n")}");
            if (Throw != null) throw Throw;
            return new HttpResponseMessage(Status) { Content = new StringContent(Body) };
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("restored")]
    public async Task ExportRetriesOnceAfterRestoreSessionConfirmsToken(string? rotatedToken)
    {
        using var handler = new ExportRecoveryHandler { RestoreToken = rotatedToken };
        var result = await new V2AuthService(() => _settings, handler).ExportDataAsync(_settings.UnifiedId);
        Assert.True(result.success, result.error);
        Assert.Equal(new[] { "/v2/user/export-data", "/v2/auth/restore-session", "/v2/user/export-data" }, handler.Paths);
        Assert.Equal(rotatedToken ?? Tok, _settings.AuthToken);
        Assert.Equal(rotatedToken ?? Tok, handler.ExportTokens[1]);
    }

    [Fact]
    public async Task BackupMetadataRecoversWithoutRetryingTheMetadataRequest()
    {
        using var handler = new ExportRecoveryHandler { RefuseRestore = true };
        var client = new V2AuthService(() => _settings, handler);
        // The metadata request is refused, then restore-session is also refused. No provider
        // token changed: WPF returns no metadata and leaves the stored token intact.
        Assert.Null(await client.GetSettingsBackupInfoAsync(_settings.UnifiedId));
        Assert.Equal(new[] { "/v2/user/settings-backup", "/v2/auth/restore-session" }, handler.Paths);
        Assert.Equal(Tok, _settings.AuthToken);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, false)]
    public async Task ExportProviderFallbackRequiresChangedTokenAndHonorsCooldowns(bool rotate, bool success)
    {
        using var handler = new ExportRecoveryHandler { RefuseRestore = true };
        var validations = 0;
        var heartbeats = 0;
        var now = new DateTime(2026, 10, 25);
        var client = new V2AuthService(() => _settings, handler)
        {
            RevalidateCloudProvider = () =>
            {
                validations++;
                if (rotate) _settings.AuthToken = "renewed";
                return Task.CompletedTask;
            },
            CloudSessionRecovered = () => heartbeats++,
            CloudRecoveryNow = () => now,
        };
        var result = await client.ExportDataAsync(_settings.UnifiedId);
        Assert.Equal(success, result.success);
        Assert.Equal(1, validations);
        Assert.Equal(success ? 1 : 0, heartbeats);
        Assert.Equal(success ? "renewed" : Tok, _settings.AuthToken);
        Assert.Equal(success ? 2 : 1, handler.ExportTokens.Count);
        if (success) Assert.Equal("renewed", handler.ExportTokens[1]);
        handler.AlwaysRefuseExport = true;
        await client.ExportDataAsync(_settings.UnifiedId);
        Assert.Equal(1, validations);
        Assert.Equal(1, handler.Paths.Count(p => p == "/v2/auth/restore-session"));
        now = now.AddSeconds(31);
        await client.ExportDataAsync(_settings.UnifiedId);
        Assert.Equal(2, handler.Paths.Count(p => p == "/v2/auth/restore-session"));
        Assert.Equal(1, validations);
        now = now.AddMinutes(2);
        await client.ExportDataAsync(_settings.UnifiedId);
        Assert.Equal(2, validations);
    }

    [Fact]
    public async Task MergedRestoreSessionStillFallsThroughToProviderValidation()
    {
        // WPF ProfileSyncService:4433: a merge tombstone fails only the restore step.
        using var handler = new ExportRecoveryHandler();
        V2AuthService.MergedRecovery = (_, _) => Task.FromResult(handler.Paths[^1] == "/v2/auth/restore-session");
        var validations = 0;
        var client = new V2AuthService(() => _settings, handler)
        {
            RevalidateCloudProvider = () => { validations++; _settings.AuthToken = "renewed"; return Task.CompletedTask; },
        };
        var result = await client.ExportDataAsync(_settings.UnifiedId);
        Assert.True(result.success, result.error);
        Assert.Equal(1, validations);
        Assert.Equal("renewed", handler.ExportTokens[1]);
    }

    [Theory]
    [InlineData(HttpStatusCode.Forbidden, 1)]
    [InlineData(HttpStatusCode.Unauthorized, 2)]
    public async Task ExportNeverRetriesNon401OrRetriesMoreThanOnce(HttpStatusCode status, int exports)
    {
        using var handler = new ExportRecoveryHandler { AlwaysRefuseExport = true, Refusal = status };
        var result = await new V2AuthService(() => _settings, handler).ExportDataAsync(_settings.UnifiedId);
        Assert.False(result.success);
        Assert.Equal("expired", result.error);
        Assert.Equal(exports, handler.ExportTokens.Count);
    }

    private sealed class ExportRecoveryHandler : HttpMessageHandler
    {
        public readonly List<string> Paths = new();
        public readonly List<string> ExportTokens = new();
        public bool RefuseRestore, AlwaysRefuseExport;
        public string? RestoreToken;
        public HttpStatusCode Refusal = HttpStatusCode.Unauthorized;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var export = path == "/v2/user/export-data";
            if (export) ExportTokens.Add(request.Headers.GetValues("X-Auth-Token").Single());
            var refused = export ? ExportTokens.Count == 1 || AlwaysRefuseExport : RefuseRestore;
            return Task.FromResult(new HttpResponseMessage(refused ? Refusal : HttpStatusCode.OK)
            { Content = new StringContent(refused ? "{\"error\":\"expired\"}" :
                !export && RestoreToken != null ? "{\"auth_token\":\"" + RestoreToken + "\"}" : "{}") });
        }
    }

    [Fact]
    public async Task EveryRequestMatchesTheWpfFixture()
    {
        var c = Client();
        await c.AuthenticateWithDiscordAsync("dtok", "Bambi");
        await c.AuthenticateWithPatreonAsync("ptok");
        await c.AuthenticateWithSubstarAsync("stok", "  ");
        await c.RegisterAsync("INV", "Bambi", "pw");
        await c.LoginAsync("Bambi", "pw");
        await c.LinkProviderAsync("u_abc123", "discord", "dtok");
        _fake.Body = "{\"user\":{\"unified_id\":\"u_abc123\",\"level\":7}}";
        var user = await c.GetUserProfileAsync("u a&b");
        _fake.Body = "{}";
        await c.UpdateUserProfileAsync("u_abc123", xp: 10, level: 2, achievements: new[] { "a1" });
        await c.SendHeartbeatAsync("u_abc123");
        await c.DeleteAccountAsync("u_abc123");
        await c.AuthorizeMobileLinkAsync();

        Assert.Equal(7, user!.Level);
        string[] expected =
        {
            Post("/v2/auth/discord", "{\n  \"access_token\": \"dtok\",\n  \"display_name\": \"Bambi\"\n}", false),
            Post("/v2/auth/patreon", "{\n  \"access_token\": \"ptok\"\n}", false),
            Post("/v2/auth/substar", "{\n  \"access_token\": \"stok\"\n}", false),
            Post("/v2/auth/register", "{\n  \"invite_code\": \"INV\",\n  \"display_name\": \"Bambi\",\n  \"password\": \"pw\"\n}", false),
            Post("/v2/auth/login", "{\n  \"display_name\": \"Bambi\",\n  \"password\": \"pw\"\n}", false),
            Post("/v2/auth/link", "{\n  \"unified_id\": \"u_abc123\",\n  \"provider\": \"discord\",\n  \"access_token\": \"dtok\"\n}", true),
            $"GET {Url}/v2/user/profile?unified_id=u%20a%26b\nUser-Agent: ConditioningControlPanel/{Ver}\nX-Auth-Token: {Tok}\nX-Client-Version: {Ver}\n\n",
            Post("/v2/user/update", "{\n  \"unified_id\": \"u_abc123\",\n  \"xp\": 10,\n  \"level\": 2,\n  \"achievements\": [\n    \"a1\"\n  ]\n}", true),
            Post("/v2/user/heartbeat", $"{{\"unified_id\":\"u_abc123\",\"is_active\":true,\"in_session\":false,\"app_version\":\"{Ver}\"}}", true),
            Post("/v2/user/delete-account", "{\n  \"unified_id\": \"u_abc123\",\n  \"confirmation\": \"DELETE\"\n}", true),
            Post("/v2/auth/device/authorize", "{\n  \"unified_id\": \"u_abc123\",\n  \"client\": \"ccp-desktop\"\n}", true),
        };
        Assert.Equal(expected, _fake.Seen);
    }

    [Fact]
    public async Task NoTokenMeansNoAuthHeader()
    {
        _settings.AuthToken = null;
        await Client().SendHeartbeatAsync("u_abc123");
        Assert.DoesNotContain("X-Auth-Token", Assert.Single(_fake.Seen));
    }

    [Fact]
    public async Task ErrorsMapAsWpfDid()
    {
        var c = Client();
        _fake.Status = HttpStatusCode.BadRequest; _fake.Body = "{\"error\":\"name_taken\"}";
        Assert.Equal("name_taken", (await c.AuthenticateWithDiscordAsync("t")).Error);
        _fake.Status = HttpStatusCode.InternalServerError; _fake.Body = "<html>";
        Assert.Equal("HTTP 500", (await c.LoginAsync("a", "b")).Error);
        Assert.Null(await c.GetUserProfileNodeAsync("u_abc123"));
        Assert.False(await c.SendHeartbeatAsync("u_abc123"));
        _fake.Throw = new HttpRequestException("down");
        Assert.Equal("Login failed. Please try again.", (await c.LoginAsync("a", "b")).Error);
        Assert.Equal("Registration failed. Please try again.", (await c.RegisterAsync("i", "a", "b")).Error);
        Assert.Equal("down", (await c.AuthenticateWithPatreonAsync("t")).Error);
        _fake.Throw = null;

        // Contract D goes through the head's seam; unseeded it is an ordinary error.
        _fake.Status = HttpStatusCode.Conflict; _fake.Body = "{\"error\":\"merged\"}";
        Assert.Equal("merged", (await c.LinkProviderAsync("u", "discord", "t")).Error);
        V2AuthService.MergedRecovery = (_, _) => Task.FromResult(true);
        Assert.Equal("account_merged", (await c.LinkProviderAsync("u", "discord", "t")).Error);
        Assert.False(await c.UpdateUserProfileAsync("u", xp: 1));
    }

    [Fact]
    public async Task TokensAndPasswordsAreNeverLogged()
    {
        var sink = new Sink();
        var old = Log.Logger;
        Log.Logger = new LoggerConfiguration().MinimumLevel.Verbose().WriteTo.Sink(sink).CreateLogger();
        try
        {
            var c = Client();
            _fake.Body = $"{{\"success\":true,\"auth_token\":\"{Tok}\",\"link_code\":\"LINKCODE-9\"}}";
            await c.AuthenticateWithDiscordAsync("ACCESS-777", "Bambi");
            await c.LoginAsync("Bambi", "PASSWORD-555");
            await c.LinkProviderAsync("u_abc123", "patreon", "ACCESS-777");
            await c.AuthorizeMobileLinkAsync();
            _fake.Status = HttpStatusCode.InternalServerError;
            await c.GetUserProfileNodeAsync("u_abc123");
            await c.AuthorizeMobileLinkAsync();
            _fake.Throw = new HttpRequestException("down");
            await c.LoginAsync("Bambi", "PASSWORD-555");
            await c.AuthenticateWithPatreonAsync("ACCESS-777");
        }
        finally { Log.Logger = old; }

        Assert.NotEmpty(sink.Lines);
        foreach (var secret in new[] { Tok, "ACCESS-777", "PASSWORD-555", "LINKCODE-9" })
            Assert.DoesNotContain(sink.Lines, l => l.Contains(secret));
    }

    private sealed class Sink : Serilog.Core.ILogEventSink
    {
        public readonly List<string> Lines = new();
        public void Emit(LogEvent e) => Lines.Add(e.RenderMessage() + " " + e.Exception);
    }

    // --- restore-session: the check half of WPF App.ValidateRestoredSessionAsync (App.xaml.cs:4191-4307) ---

    [Theory]
    [InlineData(HttpStatusCode.NotFound, "{}", V2AuthService.RestoreOutcome.Cleared, null, Tok)]
    [InlineData(HttpStatusCode.OK, "{\"auth_token\":\"NEW\"}", V2AuthService.RestoreOutcome.Validated, "u_abc123", "NEW")]
    [InlineData(HttpStatusCode.OK, "not json", V2AuthService.RestoreOutcome.Validated, "u_abc123", Tok)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":\"bad\"}", V2AuthService.RestoreOutcome.TokenCleared, "u_abc123", null)]
    [InlineData(HttpStatusCode.Unauthorized, "{\"error\":\"legacy_user_reauth_required\"}", V2AuthService.RestoreOutcome.Cleared, null, null)]
    [InlineData(HttpStatusCode.InternalServerError, "{}", V2AuthService.RestoreOutcome.Kept, "u_abc123", Tok)]
    public async Task RestoreSession_OutcomesMatchWpf(HttpStatusCode status, string body, V2AuthService.RestoreOutcome outcome, string? id, string? token)
    {
        _fake.Status = status;
        _fake.Body = body;
        Assert.Equal(outcome, await Client().ValidateRestoredSessionAsync("u_abc123"));
        Assert.Equal(id, _settings.UnifiedId);
        Assert.Equal(token, _settings.AuthToken);
        Assert.Equal(Post("/v2/auth/restore-session", $"{{\n  \"unified_id\": \"u_abc123\",\n  \"client_version\": \"{Ver}\"\n}}", true), _fake.Seen.Single());
    }

    [Fact]
    public async Task RestoreSession_NetworkError_KeepsTheCache()
    {
        _fake.Throw = new HttpRequestException("offline");
        Assert.Equal(V2AuthService.RestoreOutcome.Kept, await Client().ValidateRestoredSessionAsync("u_abc123"));
        Assert.Equal("u_abc123", _settings.UnifiedId);
        Assert.Equal(Tok, _settings.AuthToken);
    }

    [Fact]
    public void ApplyIdentity_WritesIdentityOnly_NeverProgression()
    {
        var s = new AppSettings { PlayerLevel = 12, PlayerXP = 345, UnifiedId = "old" };
        V2AuthService.ApplyIdentity(s, new V2AuthService.V2User
        {
            UnifiedId = "u9", DisplayName = "Bambi", IsSeason0Og = true, CurrentSeason = "2026-09", HighestLevelEver = 40,
            DiscordId = "d", PatreonId = null, PatreonTier = 2, Level = 50, Xp = 99999,
        }, "T2");
        Assert.Equal(("u9", "Bambi", true, "2026-09", 40, true, false, 2, "T2"),
            (s.UnifiedId, s.UserDisplayName, s.IsSeason0Og, s.CurrentSeason, s.HighestLevelEver, s.HasLinkedDiscord, s.HasLinkedPatreon, s.PatreonTier, s.AuthToken));
        Assert.Equal(12, s.PlayerLevel);   // XP take-higher is unit 6
        Assert.Equal(345, s.PlayerXP);
        V2AuthService.ApplyIdentity(s, new V2AuthService.V2User { UnifiedId = "u9" }, null);
        Assert.Equal("T2", s.AuthToken);   // no token in the response keeps the stored one
    }

    private static string Post(string path, string body, bool auth) =>
        $"POST {Url}{path}\nContent-Type: application/json; charset=utf-8\n" +
        (auth ? $"User-Agent: ConditioningControlPanel/{Ver}\nX-Auth-Token: {Tok}\n" : $"User-Agent: ConditioningControlPanel/{Ver}\n") +
        $"X-Client-Version: {Ver}\n\n{body}";
}
