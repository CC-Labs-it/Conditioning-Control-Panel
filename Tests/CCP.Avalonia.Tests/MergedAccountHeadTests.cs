using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Contract D on this head (Platform/MergedAccountHead.cs, WPF MergedAccountRecoveryHead.cs): the app seeds
/// it, and a merged relay answer runs the swap through the head's own seams.</summary>
public sealed class MergedAccountHeadTests
{
    private const string Canonical = "u_canon123";

    [Fact]
    public void AppStartupSeedsTheHeadAfterTheAccountProviders()
    {
        var app = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Avalonia", "App.axaml.cs"));
        int seed = app.IndexOf("if (Platform.AccountSeed.Seed())", StringComparison.Ordinal);
        var call = Regex.Match(app, @"^\s*Platform\.MergedAccountHead\.Seed\(\);", RegexOptions.Multiline);   // a live line, not a comment
        int merged = call.Success ? call.Index : -1;
        Assert.True(seed > 0 && merged > seed, "MergedAccountHead.Seed must run once AccountSeed built the providers");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task AMergedRelayAnswerSwapsTheAccountThroughTheHead(bool discordSignedIn) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                .UseSkia().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register).UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var old = (MergedAccountRecovery.StopSync, MergedAccountRecovery.StampQuests, MergedAccountRecovery.Reauthenticate,
            MergedAccountRecovery.ReloadProfile, MergedAccountRecovery.Finished, RemoteRelay.MergedRecovery, MergedAccountHead.Shell);
        var (oldSettings, oldGet, oldSet, oldV2, oldSync, oldId) =
            (CoreSettings.ServiceProvider, CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider, AccountSeed.NewV2, AccountSeed.Sync, CoreAccount.UnifiedUserId);
        var service = new SettingsService { SaveDebounceDueTimeMilliseconds = Timeout.Infinite };
        CoreSettings.ServiceProvider = () => service;
        var s = CoreSettings.Current;
        s.UnifiedId = CoreAccount.UnifiedUserId = "u_tombstone1";
        s.AuthToken = "tombstone-token";
        (s.PlayerLevel, s.PendingDescentMigrationChoice) = (51, "restore");   // a pending migration owns its ledger
        var secrets = new Dictionary<string, string?>();
        if (discordSignedIn)
            secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData { AccessToken = "discord", ExpiresAt = DateTime.UtcNow.AddHours(1) });
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        var server = new Server();
        AccountSeed.NewV2 = () => new V2AuthService(() => s, server);
        AccountSeed.Sync = null;
        AccountSeed.Seed(p => new ProviderSubscription(p, () => s), p => new DiscordAccount(() => p, () => s));
        MergedAccountRecovery.ResetForTest();
        StartupLadder.ResetForTests();
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));   // the offer becomes an Inbox row
        MergedAccountHead.Shell = () => null;
        try
        {
            MergedAccountHead.Seed();
            using var relay = new RemoteRelay(() => s.AuthToken, () => s.UnifiedId, "1", (_, _) => null, _ => { }, server) { AutoPoll = false };
            Assert.Null(await relay.StartAsync("light"));   // the refused start stays refused
            await MergedAccountRecovery.LastSwap;
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(Canonical, s.UnifiedId);
            Assert.Equal(Canonical, CoreAccount.UnifiedUserId);
            Assert.Equal(discordSignedIn ? "canonical-token" : null, s.AuthToken);
            Assert.Equal(discordSignedIn ? 1 : 0, server.DiscordAuths);
            Assert.Equal(discordSignedIn ? 1 : 0, server.ProfileReads);   // ReloadProfile only after a re-auth
            Assert.Equal(51, s.PlayerLevel);   // the re-auth adopt is this head's login adopt (AccountSeed.ApplyLoginUserData)
            Assert.Equal(0, server.OffUiThreadCalls);   // re-auth and reload reach TierRaised, which touches controls
            var offer = StartupLadder.Inbox.Items.Where(i => i.Key == "merged-account-signin:" + Canonical).ToList();
            Assert.Equal(discordSignedIn ? 0 : 1, offer.Count);
            Assert.All(offer, i => Assert.Equal("Link", i.IconKey));   // WPF Glyph 🔗
        }
        finally
        {
            (MergedAccountRecovery.StopSync, MergedAccountRecovery.StampQuests, MergedAccountRecovery.Reauthenticate,
                MergedAccountRecovery.ReloadProfile, MergedAccountRecovery.Finished, RemoteRelay.MergedRecovery, MergedAccountHead.Shell) = old;
            MergedAccountRecovery.ResetForTest();
            StartupLadder.ResetForTests();
            service.SealForReset();
            AccountSeed.Sync = oldSync;
            AccountSeed.NewV2 = oldV2;
            CoreSettings.ServiceProvider = oldSettings;
            CoreSecrets.RetrieveProvider = oldGet;
            CoreSecrets.StoreProvider = oldSet;
            AccountSeed.Seed();
            CoreAccount.UnifiedUserId = oldId;
        }
    });

    private sealed class Server : HttpMessageHandler
    {
        public int DiscordAuths, ProfileReads, OffUiThreadCalls;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            (HttpStatusCode code, string body) answer = path switch
            {
                "/v2/remote/start" => (HttpStatusCode.Conflict, "{\"error\":\"merged\",\"canonical_unified_id\":\"" + Canonical + "\"}"),
                "/v2/auth/discord" => (HttpStatusCode.OK, "{\"success\":true,\"auth_token\":\"canonical-token\",\"user\":{\"unified_id\":\"" + Canonical + "\",\"display_name\":\"Bambi\",\"level\":150,\"xp\":999999}}"),
                _ => (HttpStatusCode.ServiceUnavailable, "{}"),
            };
            if (path == "/v2/auth/discord") Interlocked.Increment(ref DiscordAuths);
            if (path != "/v2/remote/start" && !Dispatcher.UIThread.CheckAccess()) Interlocked.Increment(ref OffUiThreadCalls);
            if (path.StartsWith("/v2/user/profile", StringComparison.Ordinal)) Interlocked.Increment(ref ProfileReads);
            return Task.FromResult(new HttpResponseMessage(answer.code) { Content = new StringContent(answer.body) });
        }
    }

    private static string RepoRoot([CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
