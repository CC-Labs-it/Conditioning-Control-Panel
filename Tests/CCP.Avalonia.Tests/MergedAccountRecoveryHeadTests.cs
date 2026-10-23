using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>Contract D on this head (Platform/MergedAccountRecoveryHead.cs, WPF MergedAccountRecoveryHead.cs +
/// App.ReauthenticateAfterMergedSwapAsync): every seam seeded from startup before any door, a merged 409 with no
/// provider credential ends in Finished(false) and the sign-in Inbox row, and a signed-in provider re-mints the token.</summary>
public sealed class MergedAccountRecoveryHeadTests
{
    private const string Old = "u_oldaccount01", Canonical = "u_canonical01";
    private const string Merged = "{\"error\":\"merged\",\"canonical_unified_id\":\"" + Canonical + "\"}";

    /// <summary>P04/P31: App startup seeds the seams right after the sync exists, then the doors, then validates.</summary>
    [Fact]
    public void AppStartupSeedsEverySeamBeforeAnyDoorAndBeforeTheValidate()
    {
        var app = Strip(File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "App.axaml.cs")));
        int sync = app.IndexOf("AccountSeed.Sync = new SyncPush(", StringComparison.Ordinal);
        int seed = app.IndexOf("MergedAccountRecoveryHead.Seed();", StringComparison.Ordinal);
        int doors = app.IndexOf("MergedAccountRecoveryHead.WireDoors();", StringComparison.Ordinal);
        int validate = app.IndexOf("AccountSeed.ValidateRestoredSessionAsync()", StringComparison.Ordinal);
        Assert.True(sync > 0 && sync < seed && seed < doors && doors < validate, $"sync {sync}, seed {seed}, doors {doors}, validate {validate}");
        var friends = Strip(File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "Platform", "FriendsHead.cs")));
        Assert.Matches(@"new FriendsApi\([^;]*MergedAccountRecovery\.TryHandle\)", friends);
        var pbp = Strip(File.ReadAllText(Path.Combine(Root(), "CCP.Avalonia", "Views", "Games", "GameWindow.Pbp.cs")));
        Assert.Contains("MergedAccountRecovery.TryHandle(status, body)", pbp);
    }

    [Fact]
    public Task SeedLeavesNoSeamNullAndWireDoorsFillsEveryDoor() => WithState(_ =>
    {
        MergedAccountRecoveryHead.Seed();
        Assert.NotNull(MergedAccountRecovery.StopSync);
        Assert.NotNull(MergedAccountRecovery.StampQuests);
        Assert.NotNull(MergedAccountRecovery.Reauthenticate);
        Assert.NotNull(MergedAccountRecovery.ReloadProfile);
        Assert.NotNull(MergedAccountRecovery.Finished);
        MergedAccountRecoveryHead.WireDoors();
        Assert.NotNull(V2AuthService.MergedRecovery);
        Assert.NotNull(GoonSignalingClient.MergedAccountHook);
        Assert.NotNull(ArcademySyncService.MergedRecovery);
        Assert.NotNull(BackRoomApi.MergedReply);
        Assert.NotNull(AiService.MergedAccountHook);
        return Task.CompletedTask;
    });

    [Fact]
    public Task AMerged409WithNoProviderSignInFinishesFalseAndPostsTheInboxRow() => WithState(async secrets =>
    {
        var (finished, requests) = Arm(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        Assert.True(MergedAccountRecovery.TryHandle(409, Merged));
        await MergedAccountRecovery.LastSwap;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal<(string, bool)?>((Canonical, false), finished());
        Assert.Empty(requests);
        Assert.Equal(Canonical, CoreSettings.Current.UnifiedId);
        Assert.Null(CoreSettings.Current.AuthToken);
        Assert.Contains(StartupLadder.Inbox.Items, i => i.Key == "merged-account-signin:" + Canonical);
    });

    /// <summary>Provider order Patreon -> Discord, and a needs-registration answer is skipped (never the picker).</summary>
    [Fact]
    public Task WithASignedInProviderTheSwapReauthenticatesSkippingNeedsRegistration() => WithState(async secrets =>
    {
        secrets["patreon_auth"] = JsonConvert.SerializeObject(new PatreonTokenData
        { AccessToken = "p", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        secrets["discord_auth"] = JsonConvert.SerializeObject(new DiscordTokenData
        { AccessToken = "d", RefreshToken = "r", ExpiresAt = DateTime.UtcNow.AddDays(10) });
        var (finished, requests) = Arm(r => r.RequestUri!.AbsolutePath switch
        {
            "/v2/auth/patreon" => Json("{\"success\":false,\"needs_registration\":true}"),
            "/v2/auth/discord" => Json("{\"success\":true,\"user\":{\"unified_id\":\"" + Canonical + "\",\"display_name\":\"Bambi\"},\"auth_token\":\"fresh\"}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        });
        Assert.True(MergedAccountRecovery.TryHandle(409, Merged));
        await MergedAccountRecovery.LastSwap;
        Dispatcher.UIThread.RunJobs();

        Assert.Equal<(string, bool)?>((Canonical, true), finished());
        Assert.Equal(new[] { "/v2/auth/patreon", "/v2/auth/discord" }, requests.Take(2));
        Assert.Equal("fresh", CoreSettings.Current.AuthToken);
        Assert.Equal(Canonical, CoreAccount.UnifiedUserId);
        Assert.DoesNotContain(StartupLadder.Inbox.Items, i => i.Key.StartsWith("merged-account-signin:", StringComparison.Ordinal));
    });

    /// <summary>Seeds the head over a signed-in tombstone; returns the Finished capture and the request paths.</summary>
    private static (Func<(string, bool)?> Finished, List<string> Requests) Arm(Func<HttpRequestMessage, HttpResponseMessage> answer)
    {
        var requests = new List<string>();
        Assert.True(AccountSeed.Seed(p => new ProviderSubscription(p, () => new AppSettings())));
        AccountSeed.NewV2 = () => new V2AuthService(() => CoreSettings.Current, new Fake(r => { lock (requests) requests.Add(r.RequestUri!.AbsolutePath); return answer(r); }));
        var s = CoreSettings.Current;
        (s.UnifiedId, s.AuthToken, s.OfflineMode) = (Old, "tombstone", false);
        CoreAccount.UnifiedUserId = Old;
        MergedAccountRecovery.SettingsForTest = () => CoreSettings.Current;
        MergedAccountRecoveryHead.Seed();
        (string, bool)? done = null;
        var inner = MergedAccountRecovery.Finished!;
        MergedAccountRecovery.Finished = (c, r) => { done = (c, r); inner(c, r); };
        StartupLadder.BeginFirstLaunchQuiet(TimeSpan.FromMinutes(10));   // quiet: the offer becomes an Inbox row
        return (() => done, requests);
    }

    private static Task WithState(Func<Dictionary<string, string?>, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var secrets = new Dictionary<string, string?>();
        var (get, set) = (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider);
        var (stop, stamp, reauth, reload, fin) = (MergedAccountRecovery.StopSync, MergedAccountRecovery.StampQuests,
            MergedAccountRecovery.Reauthenticate, MergedAccountRecovery.ReloadProfile, MergedAccountRecovery.Finished);
        var (v2, goon, arc, back, ai, newV2) = (V2AuthService.MergedRecovery, GoonSignalingClient.MergedAccountHook,
            ArcademySyncService.MergedRecovery, BackRoomApi.MergedReply, AiService.MergedAccountHook, AccountSeed.NewV2);
        CoreSecrets.RetrieveProvider = n => secrets.GetValueOrDefault(n);
        CoreSecrets.StoreProvider = (n, v) => secrets[n] = v;
        MergedAccountRecovery.ResetForTest();
        try { await body(secrets); }
        finally
        {
            (CoreSecrets.RetrieveProvider, CoreSecrets.StoreProvider) = (get, set);
            MergedAccountRecovery.StopSync = stop;
            MergedAccountRecovery.StampQuests = stamp;
            MergedAccountRecovery.Reauthenticate = reauth;
            MergedAccountRecovery.ReloadProfile = reload;
            MergedAccountRecovery.Finished = fin;
            V2AuthService.MergedRecovery = v2;
            GoonSignalingClient.MergedAccountHook = goon;
            ArcademySyncService.MergedRecovery = arc;
            BackRoomApi.MergedReply = back;
            AiService.MergedAccountHook = ai;
            AccountSeed.NewV2 = newV2;
            MergedAccountRecovery.ResetForTest();
            CoreAccount.UnifiedUserId = null;
        }
    });

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    private sealed class Fake(Func<HttpRequestMessage, HttpResponseMessage> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct) => Task.FromResult(answer(r));
    }

    private static string Strip(string src) =>   // P23: comments never count
        Regex.Replace(src, @"//[^\n]*|/\*.*?\*/", " ", RegexOptions.Singleline);

    private static string Root([System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", ".."));
}
