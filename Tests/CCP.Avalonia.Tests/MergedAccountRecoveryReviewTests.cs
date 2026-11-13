using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using CCP.Tests.Shared;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class MergedAccountRecoveryReviewTests
{
    private const string Body = "{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}";

    internal static void Setup()
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
        internal HttpStatusCode Restore = HttpStatusCode.OK;
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
            var status = path == "/v2/auth/restore-session" ? Restore : ok ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(status == HttpStatusCode.OK ? body : "{}") });
        }
    }

    [Theory]
    [InlineData("patreon")]
    [InlineData("discord")]
    [InlineData("substar")]
    [InlineData(null)]
    public Task ProviderExchangeReloadsOrOffersSignIn(string? provider) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var saved = new MergedRecoveryState();
        Setup();
        var secrets = new Dictionary<string, string?>();
        var tokenData = JsonConvert.SerializeObject(new PatreonTokenData { AccessToken = "provider", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        secrets["patreon_auth"] = secrets["substar_auth"] = tokenData;
        secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData { AccessToken = "discord", ExpiresAt = DateTime.UtcNow.AddDays(1) });
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var settings = CoreSettings.Current;
        (settings.UnifiedId, settings.AuthToken, settings.PlayerLevel, settings.PlayerXP, settings.OfflineMode, settings.InviteGrantUntil, settings.MergedSignInPending)
            = ("u_tombstone1", null, 7, 42, provider == "substar", null, false);
        var wire = new Wire(provider);
        AccountSeed.NewV2 = () => new V2AuthService(() => settings, wire);
        AccountSeed.Sync = null;
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => settings),
            p => new DiscordAccount(() => p, () => settings)));
        AccountSeed.RestoreSession();
        await AccountSeed.ValidateRestoredSessionAsync();
        Assert.True(CoreAccount.IsLoggedIn); // ordinary tokenless provider, both offline and failed exchange
        (settings.AuthToken, settings.OfflineMode) = ("old", false);
        wire = new Wire(provider);
        var original = AccountSeed.Patreon!;
        var oldUid = AccountSeed.LoadedProfileUid;
        var tierEvent = typeof(ProviderSubscription).GetField("TierChanged", BindingFlags.Instance | BindingFlags.NonPublic)!;
        ProviderSubscription? replacement = null;
        using (new MergedRecoveryState())
            Assert.True(AccountSeed.Seed(p => { AccountSeed.LoadedProfileUid = "test"; return replacement = new ProviderSubscription(p); }));
        Assert.Same(original, AccountSeed.Patreon);
        Assert.Equal(oldUid, AccountSeed.LoadedProfileUid);
        Assert.NotNull(tierEvent.GetValue(original));
        Assert.Null(tierEvent.GetValue(replacement!));
        MergedAccountRecovery.SettingsForTest = () => settings;
        MergedAccountRecoveryHead.Seed();
        try
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent(Body) };
            Assert.True(await V2AuthService.MergedRecovery!(response, null));
            await MergedAccountRecovery.LastSwap;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u_canon123", settings.UnifiedId);
            if (provider != null) CoreSettings.Current.MergedSignInPending = true;
            Assert.True(CoreAccount.IsLoggedIn);
            Assert.Equal("u_canon123", CoreAccount.UnifiedUserId);
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
            // The pending merge survives a restart; the startup exchange clears it.
            (settings.AuthToken, settings.MergedSignInPending, CoreAccount.UnifiedUserId) = (null, JsonConvert.DeserializeObject<AppSettings>(JsonConvert.SerializeObject(settings))!.MergedSignInPending, null);
            AccountSeed.RestoreSession();
            Assert.True(CoreAccount.IsLoggedIn);
            await AccountSeed.ValidateRestoredSessionAsync();
            Assert.True(CoreAccount.IsLoggedIn && CoreAccount.UnifiedUserId == "u_canon123");
            // No provider mints: restore-session re-issues the token (WPF App.xaml.cs:4805) and ends the pending merge.
            Assert.Equal(provider == null, wire.Paths.Contains("/v2/auth/restore-session"));
            Assert.True(settings.AuthToken == "canonical-token" && !settings.MergedSignInPending);
            (settings.AuthToken, settings.MergedSignInPending, wire.Restore) = (null, true, HttpStatusCode.NotFound);
            if (provider == null) await AccountSeed.ValidateRestoredSessionAsync(); // 404: the identity is gone
            Assert.Equal(provider == null, CoreAccount.UnifiedUserId == null && settings.UnifiedId == null && !settings.MergedSignInPending);
        }
        finally
        {
            StartupLadder.ResetForTests();
            InvitePanel.CancelExpiry();
        }
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AiMergedResponseTriggersRecovery(bool purchase) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var saved = new MergedRecoveryState();
        Setup();
        var secrets = new Dictionary<string, string?>();
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var settings = CoreSettings.Current;
        (settings.UnifiedId, settings.AuthToken, settings.OfflineMode, settings.HasLinkedDiscord) = (null, "old", false, false);
        CoreAccount.UnifiedUserId = "u_tombstone1";
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => settings), p => new DiscordAccount(() => p, () => settings)));
        CoreSession.IsSessionRunningProvider = () => true; // the visible shell files the offer instead of opening it
        MergedAccountRecovery.SettingsForTest = () => settings;
        MergedAccountRecoveryHead.Seed();
        using var server = new FakeServer();
        server.Handle = ctx => FakeServer.Send(ctx, Encoding.UTF8.GetBytes(Body), 409);
        using var ai = new AiService(server.Prefix);
        using var http = new HttpClient();
        var lifetimeField = typeof(Application).GetField("_applicationLifetime", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var lifetime = lifetimeField.GetValue(Application.Current);
        var shell = new MainShellWindow();
        try
        {
            lifetimeField.SetValue(Application.Current, new ClassicDesktopStyleApplicationLifetime { MainWindow = shell });
            shell.Show();
            shell.ShowTab("appsettings");
            Dispatcher.UIThread.RunJobs();
            var discord = shell.AppSettingsPage!.SectionAccount.FindControl<TextBlock>("TxtBtnDiscordLogin")!;
            Assert.Equal(Loc.Get("btn_login"), discord.Text);
#pragma warning disable CS0618
            if (purchase)
                await new BackRoomApi(http, () => ("u_tombstone1", "old"), null, baseUrl: server.Prefix.TrimEnd('/'))
                    .RelayAsync("counter", "buy", "idem", null);
            else await ai.GetBambiReplyExAsync("hello there");
#pragma warning restore CS0618
            await MergedAccountRecovery.LastSwap;
            Dispatcher.UIThread.RunJobs();
            Assert.Contains(server.Requests, r => r.Path == (purchase ? "/v2/backroom/counter/buy" : "/v2/ai/chat"));
            Assert.Equal("u_canon123", settings.UnifiedId);
            CoreAccount.UnifiedUserId = null;
            AccountSeed.RestoreSession();
            Assert.True(CoreAccount.IsLoggedIn && CoreAccount.UnifiedUserId == "u_canon123" && settings.AuthToken == null);
            Assert.True(StartupLadder.Inbox.Contains("merged-account-signin:u_canon123"));
            Assert.Equal(Loc.Get("btn_link_discord"), discord.Text); // WPF MainWindow.Login.cs:56 UpdateDiscordUI
        }
        finally
        {
            shell.Close();
            lifetimeField.SetValue(Application.Current, lifetime);
            StartupLadder.ResetForTests();
            InvitePanel.CancelExpiry();
        }
    });
}
