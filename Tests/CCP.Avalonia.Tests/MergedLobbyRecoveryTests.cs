using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Games;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Lobby;
using Xunit;
namespace CCP.Avalonia.Tests;
public sealed class MergedLobbyRecoveryTests
{
    private sealed class Conflict : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(
                "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon456\"}") });
    }
    private sealed class Provider(string kind) : HttpMessageHandler
    {
        public int Auth, Profile;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            bool auth = request.RequestUri!.AbsolutePath == "/v2/auth/" + kind;
            if (auth) Auth++; else Profile++;
            var body = auth
                ? "{\"success\":true,\"auth_token\":\"new-token\",\"user\":{\"unified_id\":\"u_canon789\",\"display_name\":\"Person\",\"discord_id\":\"discord-1\",\"patreon_id\":\"patron-1\",\"patreon_tier\":2,\"invite_grant_until\":\"" + DateTime.UtcNow.AddDays(7).ToString("O") + "\"}}"
                : "{\"user\":{\"unified_id\":\"u_canon789\",\"display_name\":\"Person\"}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
        }
    }
    [Theory, InlineData("fyp", false), InlineData("loom", false), InlineData("breakoutdemo", true)]
    public void StartupGameProbeDistinguishesWebHosts(string id, bool isGame) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(AppFonts.Options(systemEmoji: false)).UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var window = new GameWindow(GameWindow.Games[id]);
        try { window.Show(); Assert.Equal(isGame, StartupLadder.World(DateTime.UtcNow).GameHostUp); }
        finally { window.Close(); }
    });
    [Theory, InlineData("quiet", "patreon"), InlineData("game", "discord"), InlineData("launcher", "discord")]
    public async Task ClaimMergedConflictStopsSyncAdoptsCanonicalAndOffersSignIn(string owner, string providerName)
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>().WithInterFont().With(AppFonts.Options(systemEmoji: false)).UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var secrets = new System.Collections.Generic.Dictionary<string, string?>();
            CoreSecrets.RetrieveProvider = key => secrets.TryGetValue(key, out var value) ? value : null;
            CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
            var settings = CoreSettings.Current;
            GameWindow? game = null; ConditioningControlPanel.Avalonia.Views.Windows.LauncherWindow? launcher = null;
            var oldSync = AccountSeed.Sync;
            var oldV2 = AccountSeed.NewV2;
            var sync = new SyncPush(() => Array.Empty<string>(), () => false);
            sync.MarkLoaded(Array.Empty<string>());
            try
            {
                settings.OfflineMode = false;
                settings.UnifiedId = "u_old1234";
                settings.AuthToken = "old-token";
                CoreAccount.UnifiedUserId = "u_old1234";
                MergedAccountRecovery.ResetForTest();
                MergedAccountRecovery.SettingsForTest = () => settings;
                Assert.True(AccountSeed.Seed()); // providers have not authenticated
                AccountSeed.Sync = sync;
                AccountSeed.SeedMergedRecovery();
                StartupLadder.ResetForTests();
                if (owner == "quiet") StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));
                if (owner == "game") { game = new GameWindow(GameWindow.Games["breakoutdemo"]); game.Show(); } if (owner == "launcher") launcher = ConditioningControlPanel.Avalonia.Views.Windows.LauncherWindow.Open();
                Assert.Equal(owner == "game", StartupLadder.World(DateTime.UtcNow).GameHostUp); Assert.Equal(owner == "launcher", StartupLadder.World(DateTime.UtcNow).LauncherHolding);
                using var http = new HttpClient(new Conflict());
                var api = new RemoteLobbyApi(http, () => ("u_old1234", "old-token"), "http://127.0.0.1:9");
                var result = await api.TryClaimAsync("u_subject");
                Assert.Null(result.Url);
                Assert.False(result.Refresh);
                await MergedAccountRecovery.LastSwap.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("u_canon456", settings.UnifiedId);
                Assert.Equal("u_canon456", CoreAccount.UnifiedUserId);
                Assert.True(string.IsNullOrEmpty(settings.AuthToken));
                Assert.False(sync.Loaded);
                var item = Assert.Single(StartupLadder.Inbox.Items);
                Assert.Equal("merged-account-signin:u_canon456", item.Key); Assert.True(item.HasLinkIcon);
                Assert.Equal(ConditioningControlPanel.Localization.Loc.Get("account_merged_signin_title"), item.Title);
                launcher?.Close(); launcher = null; game?.Close(); game = null;
                // A different merged account with a provider credential mints a new token and reloads.
                StartupLadder.ResetForTests();
                MergedAccountRecovery.ResetForTest();
                MergedAccountRecovery.SettingsForTest = () => settings;
                settings.PatreonPremiumValidUntil = settings.PatreonLabValidUntil = settings.InviteGrantUntil = null; settings.PatreonTier = 0;
                settings.UnifiedId = "u_old5678";
                settings.AuthToken = "old-token";
                CoreAccount.UnifiedUserId = "u_old5678";
                secrets[providerName + "_auth"] = "{\"access_token\":\"provider-token\",\"expires_at\":\"2099-01-01T00:00:00Z\"}";
                var provider = new Provider(providerName);
                AccountSeed.Sync = null;
                AccountSeed.NewV2 = () => new V2AuthService(() => settings, provider);
                AccountSeed.SeedMergedRecovery();
                Assert.True(MergedAccountRecovery.TryHandle(409, "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon789\"}"));
                await MergedAccountRecovery.LastSwap.WaitAsync(TimeSpan.FromSeconds(10));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("u_canon789", settings.UnifiedId);
                Assert.Equal("new-token", settings.AuthToken);
                Assert.True(settings.PatreonPremiumValidUntil > DateTime.UtcNow); Assert.True(settings.PatreonLabValidUntil > DateTime.UtcNow); Assert.True(settings.HasInviteGrant); Assert.InRange(settings.InviteGrantUntil!.Value, DateTime.UtcNow.AddDays(6.99), DateTime.UtcNow.AddDays(7.01));
                Assert.Equal(1, provider.Auth);
                Assert.Equal(1, provider.Profile);
                Assert.Empty(StartupLadder.Inbox.Items);
            }
            finally
            {
                launcher?.Close(); game?.Close(); game = null;
                AccountSeed.Sync = oldSync;
                AccountSeed.NewV2 = oldV2;
                StartupLadder.ResetForTests();
                MergedAccountRecovery.ResetForTest();
            }
        });
    }
}
