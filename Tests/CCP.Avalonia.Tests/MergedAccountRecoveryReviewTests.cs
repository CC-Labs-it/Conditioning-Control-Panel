using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class MergedAccountRecoveryReviewTests
{
    private const string Body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}";

    private static void Setup()
    {
        if (Application.Current is null)
            AppBuilder.Configure<App>().WithInterFont().With(AppFonts.Options())
                .ConfigureFonts(AppFonts.Register).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        StartupLadder.ResetForTests();
        InvitePanel.CancelExpiry();
        MergedAccountRecovery.ResetForTest();
    }

    private sealed class Wire(string? successfulProvider) : HttpMessageHandler
    {
        internal readonly List<string> Paths = new();
        internal string? ProfileQuery, ProfileToken;
        internal readonly DateTime GrantUntil = DateTime.UtcNow.AddDays(1);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Add(path);
            var profile = path == "/v2/user/profile";
            if (profile)
            {
                ProfileQuery = request.RequestUri.Query;
                ProfileToken = request.Headers.TryGetValues("X-Auth-Token", out var values) ? values.Single() : null;
            }
            var ok = profile || path == "/v2/auth/" + successfulProvider;
            var body = profile ? "{\"user\":{\"unified_id\":\"u_canon123\",\"level\":1,\"xp\":0}}"
                : "{\"success\":true,\"auth_token\":\"canonical-token\",\"user\":{\"unified_id\":\"u_canon123\",\"level\":1,\"xp\":0,\"invite_grant_until\":\"" + GrantUntil.ToString("O") + "\"}}";
            return Task.FromResult(new HttpResponseMessage(ok ? HttpStatusCode.OK : HttpStatusCode.Unauthorized)
            { Content = new StringContent(ok ? body : "{}") });
        }
    }

    [Theory]
    [InlineData("patreon")]
    [InlineData("discord")]
    [InlineData("substar")]
    [InlineData(null)]
    public Task ProviderExchangeReloadsOrOffersSignIn(string? provider) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        using var saved = new MergedRecoveryState();
        var secrets = new Dictionary<string, string?>();
        var tokenData = JsonConvert.SerializeObject(new PatreonTokenData { AccessToken = "provider", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        secrets["patreon_auth"] = secrets["substar_auth"] = tokenData;
        secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData { AccessToken = "discord", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var settings = CoreSettings.Current;
        settings.UnifiedId = "u_tombstone1";
        settings.AuthToken = "old";
        settings.PlayerLevel = 7;
        settings.PlayerXP = 42;
        settings.OfflineMode = false;
        settings.InviteGrantUntil = null;
        var wire = new Wire(provider);
        AccountSeed.NewV2 = () => new V2AuthService(() => settings, wire);
        AccountSeed.Sync = null;
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => settings),
            p => new DiscordAccount(() => p, () => settings)));
        MergedAccountRecovery.SettingsForTest = () => settings;
        MergedAccountRecoveryHead.Seed();
        try
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(Body) };
            Assert.True(await V2AuthService.MergedRecovery!(response, null));
            await MergedAccountRecovery.LastSwap;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u_canon123", settings.UnifiedId);
            Assert.Equal(7, settings.PlayerLevel);
            Assert.Equal(42, settings.PlayerXP);
            var expected = provider switch
            {
                "patreon" => new[] { "/v2/auth/patreon" },
                "discord" => new[] { "/v2/auth/patreon", "/v2/auth/discord" },
                _ => new[] { "/v2/auth/patreon", "/v2/auth/discord", "/v2/auth/substar" },
            };
            Assert.Equal(expected, wire.Paths.Where(p => p.StartsWith("/v2/auth/")));
            Assert.Equal(provider != null, wire.Paths.Contains("/v2/user/profile"));
            Assert.Equal(provider != null ? "?unified_id=u_canon123" : null, wire.ProfileQuery);
            Assert.Equal(provider != null ? "canonical-token" : null, wire.ProfileToken);
            Assert.Equal(provider != null ? "canonical-token" : null, settings.AuthToken);
            Assert.Equal(provider != null ? wire.GrantUntil : null, settings.InviteGrantUntil);
            Assert.Equal(provider != null, InvitePanel.ExpiryArmed);
            Assert.Equal(provider == null, StartupLadder.Inbox.Contains("merged-account-signin:u_canon123"));
        }
        finally
        {
            StartupLadder.ResetForTests();
            InvitePanel.CancelExpiry();
        }
    });

    [Fact]
    public Task AiMergedResponseTriggersRecovery() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        Setup();
        using var saved = new MergedRecoveryState();
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var settings = CoreSettings.Current;
        settings.UnifiedId = "u_tombstone1";
        settings.AuthToken = "old";
        settings.OfflineMode = false;
        CoreAccount.UnifiedUserId = "u_tombstone1";
        MergedAccountRecovery.SettingsForTest = () => settings;
        MergedAccountRecoveryHead.Seed();
        using var server = new FakeServer();
        server.Handle = ctx => FakeServer.Send(ctx, Encoding.UTF8.GetBytes(Body), 409);
        using var ai = new AiService(server.Prefix);
        try
        {
#pragma warning disable CS0618
            await ai.GetBambiReplyExAsync("hello there");
#pragma warning restore CS0618
            await MergedAccountRecovery.LastSwap;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(server.Requests, r => r.Path == "/v2/ai/chat");
            Assert.Equal("u_canon123", settings.UnifiedId);
            Assert.Null(settings.AuthToken);
            Assert.True(StartupLadder.Inbox.Contains("merged-account-signin:u_canon123"));
        }
        finally
        {
            StartupLadder.ResetForTests();
            InvitePanel.CancelExpiry();
        }
    });
}
