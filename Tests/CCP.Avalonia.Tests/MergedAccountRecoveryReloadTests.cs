using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

public class MergedAccountRecoveryReloadTests
{
    private sealed class Wire(bool failProfile) : HttpMessageHandler
    {
        internal readonly ConcurrentQueue<string> Paths = new();
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            Paths.Enqueue(path);
            await Task.Yield(); // The real network completes asynchronously.
            if (path == "/v2/user/profile" && failProfile)
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("{}") };
            var body = path == "/v2/auth/patreon"
                ? "{\"success\":true,\"auth_token\":\"new-token\",\"user\":{\"unified_id\":\"u_canon123\",\"level\":1,\"xp\":0}}"
                : "{\"user\":{\"unified_id\":\"u_canon123\",\"level\":1,\"xp\":0}}";
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public Task FailedReadRestartsHeartbeatButNeverEnablesPush() => Exercise(failProfile: true);

    [Fact]
    public Task SuccessfulReadPresentsArmedWelcomeBackOnUiThread() => Exercise(failProfile: false);

    private static Task Exercise(bool failProfile) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        using var saved = new MergedRecoveryState();
        MergedAccountRecoveryReviewTests.Setup();
        WelcomeBackOffer.ResetForTests();
        var directory = Directory.CreateTempSubdirectory("ccp-merge-reload-");
        var secrets = new Dictionary<string, string?> { ["patreon_auth"] = JsonConvert.SerializeObject(
            new PatreonTokenData { AccessToken = "provider", ExpiresAt = DateTime.UtcNow.AddDays(1) }) };
        CoreSecrets.RetrieveProvider = key => secrets.GetValueOrDefault(key);
        CoreSecrets.StoreProvider = (key, value) => secrets[key] = value;
        var settings = CoreSettings.Current;
        (settings.UnifiedId, settings.AuthToken, settings.PlayerLevel, settings.PlayerXP, settings.OfflineMode, settings.InviteGrantUntil)
            = ("u_tombstone1", "old-token", 7, 42, false, null);
        var wire = new Wire(failProfile);
        var sync = new SyncPush(() => null, () => false, wire);
        AccountSeed.NewV2 = () => new V2AuthService(() => settings, wire);
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => settings),
            p => new DiscordAccount(() => p, () => settings)));
        AccountSeed.Sync = sync;
        sync.MarkLoaded(null);
        MergedAccountRecovery.SettingsForTest = () => settings;
        MergedAccountRecoveryHead.Seed();
        var (shown, shownOnUi) = (0, false);
        WelcomeBackOffer.Present = _ => { shownOnUi = Dispatcher.UIThread.CheckAccess(); shown++; return Task.CompletedTask; };
        WelcomeBackOffer.NewBackup = WelcomeBackRestoreTests.NoBackupClient; // never the real proxy
        WelcomeBackOffer.Arm(true, directory.FullName);
        try
        {
            using var response = new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("{\"error\":\"merged\",\"canonical_unified_id\":\"u_canon123\"}") };
            Assert.True(await V2AuthService.MergedRecovery!(response, null));
            await MergedAccountRecovery.LastSwap;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal("u_canon123", settings.UnifiedId);
            Assert.Equal("new-token", settings.AuthToken);
            Assert.Contains("/v2/user/profile", wire.Paths);
            if (failProfile)
            {
                Assert.False(sync.Loaded);
                Assert.True(sync.HeartbeatRunning);
                Assert.False(await sync.PushAsync("test"));
                Assert.DoesNotContain("/v2/user/sync", wire.Paths);
                Assert.Equal(0, shown);
            }
            else
            {
                Assert.True(sync.Loaded);
                Assert.Equal(1, shown);
                Assert.True(shownOnUi);
            }
        }
        finally
        {
            sync.StopHeartbeat();
            WelcomeBackOffer.ResetForTests();
            StartupLadder.ResetForTests();
            InvitePanel.CancelExpiry();
            directory.Delete(true);
        }
    });
}
