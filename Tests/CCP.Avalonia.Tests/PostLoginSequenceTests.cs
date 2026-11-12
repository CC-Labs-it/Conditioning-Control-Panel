using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Net;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia;
using Avalonia.Headless;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed partial class AccountSeedTests
{
    [Fact]
    public void FailedPostLoginReadStartsHeartbeatAndRetriesOnce() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var pending = AccountSeed.PostLoginAsync();
        Assert.True(sync.HeartbeatRunning);
        lock (wire.Seen) Assert.DoesNotContain(wire.Seen, r => r.Path == "GET /v2/user/profile");
        await pending;
        lock (wire.Seen)
            Assert.Equal(2, wire.Seen.Count(r => r.Path == "GET /v2/user/profile"));
        Assert.Empty(wire.Syncs);
    });

    [Fact]
    public void FailedReadsRecoverProgressWithoutOpeningTheWriteGate() => WithFreshInstall(async () =>
    {
        var (wire, sync, local) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        s.LastWebXpClaimId = "unread-claim";
        local.Add("local-only");
        wire.SyncReply = new Newtonsoft.Json.Linq.JObject
        {
            ["success"] = true,
            ["user"] = new Newtonsoft.Json.Linq.JObject
            {
                ["unified_id"] = "u1", ["level"] = 40, ["xp"] = L40Xp, ["current_season"] = "2026-08"
            }
        }.ToString();
        await AccountSeed.PostLoginAsync();
        lock (wire.Seen)
            Assert.Equal(2, wire.Seen.Count(r => r.Path == "GET /v2/user/profile"));
        var body = Assert.Single(wire.Syncs);
        Assert.Equal(new HashSet<string> { "unified_id", "xp", "level", "descent_epoch", "descent_auto" },
            body.Properties().Select(p => p.Name).ToHashSet());
        Assert.Equal("u1", body.Value<string>("unified_id"));
        Assert.Equal(40, s.PlayerLevel);
        Assert.False(sync.Loaded);
        Assert.False(await sync.PushAsync("ordinary"));
        Assert.Single(wire.Syncs);
    });

    [Fact]
    public void RecoveryClaimIsCredited() => WithFreshInstall(async () =>
    {
        var (wire, _, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        s.LastWebXpClaimId = null;
        var xp = ProfileAdopt.TotalXp(s);
        wire.SyncReply = """{"success":true,"web_xp":{"claim":{"id":"recovery","amount":17}}}""";
        await AccountSeed.PostLoginAsync();
        Assert.Equal("recovery", s.LastWebXpClaimId);
        Assert.Equal(xp + 17, ProfileAdopt.TotalXp(s));
    });

    [Fact]
    public void RecoveryEventuallyReadsAndSyncsLaterProgress() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        CoreSettings.Current.PlayerLevel = 2;
        await AccountSeed.PostLoginAsync();
        wire.Profile = L40Profile("u1");
        await Task.Delay(1500);
        Assert.True(sync.Loaded);
        ProgressionBank.AddClaimed(17);
        sync.UtcNow = () => T0.AddHours(1);
        Assert.True(await sync.PushAsync("later progress"));
        Assert.Equal(ProfileAdopt.TotalXp(CoreSettings.Current), wire.Syncs.Last().Value<double>("xp"));
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ReadRetriesAfterFailedOrGuardedRecovery(bool guarded) => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        CoreSettings.Current.PlayerLevel = guarded ? 1 : 2;
        CoreSettings.Current.PlayerXP = 0;
        wire.SyncStatus = HttpStatusCode.InternalServerError;
        await AccountSeed.PostLoginAsync();
        Assert.Equal(guarded ? 0 : 1, wire.Syncs.Count());
        wire.SyncStatus = HttpStatusCode.OK;
        wire.Profile = L40Profile("u1");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!sync.Loaded) await Task.Delay(50, timeout.Token);
        Assert.Equal(40, CoreSettings.Current.PlayerLevel);
    });

    [Fact]
    public void RetryAutomaticallySyncsProgressAfterCooldown() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        var initial = ProfileAdopt.TotalXp(s);
        wire.SyncReply = """{"success":true,"web_xp":{"claim":{"id":"retry-claim","amount":17}}}""";
        var now = T0;
        sync.UtcNow = () => now;
        await AccountSeed.PostLoginAsync();
        Assert.Equal(initial + 17, ProfileAdopt.TotalXp(s));
        now = T0.AddSeconds(28);
        wire.Profile = new Newtonsoft.Json.Linq.JObject { ["user"] = new Newtonsoft.Json.Linq.JObject {
            ["unified_id"] = "u1", ["level"] = 2, ["xp"] = initial, ["current_season"] = "2026-08" } }.ToString();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(9));
        while (!sync.Loaded) await Task.Delay(50, timeout.Token);
        now = T0.AddMinutes(1);
        while (wire.Syncs.Count() < 2) await Task.Delay(50, timeout.Token);
        Assert.Equal(initial + 17, wire.Syncs.Last().Value<double>("xp"));
        Assert.Equal("retry-claim", wire.Syncs.Last().Value<string>("web_xp_claim_ack"));
    });

    [Fact]
    public void FailedRecoverySyncsAfterItsBackoff() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        CoreSettings.Current.PlayerLevel = 2;
        var now = T0;
        sync.UtcNow = () => now;
        wire.SyncStatus = HttpStatusCode.InternalServerError;
        await AccountSeed.PostLoginAsync();
        Assert.Single(wire.Syncs);
        wire.SyncStatus = HttpStatusCode.OK;
        now = T0.AddSeconds(28);
        wire.Profile = L40Profile("u1");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!sync.Loaded) await Task.Delay(50, timeout.Token);
        await Task.Delay(3300, timeout.Token);
        Assert.Single(wire.Syncs);
        now = T0.AddMinutes(1);
        while (wire.Syncs.Count() < 2) await Task.Delay(50, timeout.Token);
        Assert.Equal("u1", wire.Syncs.Last().Value<string>("unified_id"));
    });

    [Fact]
    public void OldSessionNudgeDoesNotPushTheNewAccount() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        sync.MarkLoaded(null);
        sync.Nudge("old account");
        sync.Reset();
        CoreSettings.Current.UnifiedId = "u2";
        sync.MarkLoaded(null);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await Task.Delay(3500, timeout.Token);
        Assert.Empty(wire.Syncs);
        sync.Nudge("new account");
        while (!wire.Syncs.Any()) await Task.Delay(50, timeout.Token);
        Assert.Equal("u2", Assert.Single(wire.Syncs).Value<string>("unified_id"));
    });

    [Fact]
    public Task RetryRepaintsTheDisplayedLevel() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (global::Avalonia.Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new global::Avalonia.Headless.AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        await Fresh(async () =>
        {
            var (wire, sync, _) = SignIn("u1", null);
            CoreSettings.Current.PlayerLevel = 2;
            CoreSettings.Current.UserDisplayName = "Tester";
            var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
            try
            {
                shell.Show();
                shell.UpdateQuickLoginUI();
                await AccountSeed.PostLoginAsync();
                Assert.Equal("LVL 2", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
                wire.Profile = L40Profile("u1");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                while (!sync.Loaded) await Task.Delay(50, timeout.Token);
                await Task.Delay(100, timeout.Token);
                global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                Assert.Equal("LVL 40", shell.FindControl<TextBlock>("TxtLevelLabel")!.Text);
            }
            finally { shell.Close(); }
        });
    });

    [Theory]
    [InlineData(99, 0)]
    [InlineData(100, 1)]
    public void FailedReadsUseWpfsDefaultProgressBoundary(int xp, int expectedSyncs) => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        CoreSettings.Current.PlayerLevel = 1;
        CoreSettings.Current.PlayerXP = xp;
        await AccountSeed.PostLoginAsync();
        Assert.Equal(expectedSyncs, wire.Syncs.Count());
        Assert.False(sync.Loaded);
    });

    [Fact]
    public void FailedReadsCannotSendBelowTheAccountWatermark() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        s.LastConfirmedServerXp = ProfileAdopt.TotalXp(s) + 100;
        s.LastConfirmedServerXpAccount = "u1";
        s.LastConfirmedServerXpSeason = s.CurrentSeason;
        await AccountSeed.PostLoginAsync();
        Assert.Empty(wire.Syncs);
        Assert.False(sync.Loaded);
    });

    [Fact]
    public void FailedReadsDoNotSendAnUnreconciledBackup() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        var restored = typeof(SettingsService).GetProperty(nameof(SettingsService.RestoredFromBackup))!;
        restored.SetValue(CoreSettings.Service, true);
        try { await AccountSeed.PostLoginAsync(); }
        finally { restored.SetValue(CoreSettings.Service, false); }
        Assert.Empty(wire.Syncs);
        Assert.False(sync.Loaded);
    });

    [Fact]
    public void FailedReadsDoNotSendPendingMigration() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        s.PendingDescentMigrationChoice = ConditioningControlPanel.Services.Descent.DescentMigrationChoices.Restore;
        try { await AccountSeed.PostLoginAsync(); }
        finally { s.PendingDescentMigrationChoice = null; }
        Assert.Empty(wire.Syncs);
        Assert.False(sync.Loaded);
    });

    [Fact]
    public void FailedReadRecoveryDropsReplyAfterLogout() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        var s = CoreSettings.Current;
        s.PlayerLevel = 2;
        wire.SyncReply = new Newtonsoft.Json.Linq.JObject
        {
            ["success"] = true,
            ["user"] = new Newtonsoft.Json.Linq.JObject { ["level"] = 40, ["xp"] = L40Xp }
        }.ToString();
        wire.Hold = new TaskCompletionSource();
        try
        {
            var pending = AccountSeed.PostLoginAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            while (!wire.Syncs.Any()) await Task.Delay(20, timeout.Token);
            sync.Reset();
            wire.Hold.SetResult();
            await pending;
            Assert.Equal(2, s.PlayerLevel);
            Assert.False(sync.Loaded);
            Assert.Null(sync.LastSyncTime);
        }
        finally { wire.Hold.TrySetResult(); }
    });

    [Fact]
    public void FailedReadsStillAttemptSyncWhenAnEarlierReadOpenedTheGate() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", null);
        sync.MarkLoaded(null); // This account was already read in this session.
        await AccountSeed.PostLoginAsync();
        lock (wire.Seen)
            Assert.Equal(2, wire.Seen.Count(r => r.Path == "GET /v2/user/profile"));
        Assert.Single(wire.Syncs);
    });

    [Fact]
    public void FailedFirstPushGetsPostLoginSyncAttempt() => WithFreshInstall(async () =>
    {
        var (wire, sync, _) = SignIn("u1", L40Profile("u1"));
        wire.Next.Enqueue((HttpStatusCode.InternalServerError, "{}"));
        wire.Next.Enqueue((HttpStatusCode.OK, "{\"success\":true}"));
        // Let the real failure backoff expire before the subsequent sync attempt.
        var now = T0;
        sync.UtcNow = () => { now = now.AddHours(1); return now; };
        await AccountSeed.PostLoginAsync();
        Assert.True(sync.Loaded);
        Assert.Equal(2, wire.Syncs.Count());
    });

    [Fact]
    public void FailedFirstPushStillHonorsFailureBackoff() => WithFreshInstall(async () =>
    {
        var (wire, _, _) = SignIn("u1", L40Profile("u1"));
        wire.Next.Enqueue((HttpStatusCode.InternalServerError, "{}"));
        await AccountSeed.PostLoginAsync();
        Assert.Single(wire.Syncs);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PostLoginSuppressesPopupsAndRestoresAfterLoad(bool throws) => WithFreshInstall(async () =>
    {
        SignIn("u1", null);
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())));
        async Task<bool> Load()
        {
            Assert.True(engine.SuppressPopups);
            await Task.Yield();
            if (throws) throw new InvalidOperationException();
            return true;
        }
        if (throws) await Assert.ThrowsAsync<InvalidOperationException>(() => AccountSeed.PostLoginAsync(Load, engine));
        else await AccountSeed.PostLoginAsync(Load, engine);
        Assert.False(engine.SuppressPopups);
    });
}

public sealed class PostLoginRepaintTests
{
    [Fact]
    public Task ProfileLoadRepaintsOgName() => CCP.Avalonia.Testing.AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var s = CoreSettings.Current;
        var old = (s.UserDisplayName, s.IsSeason0Og);
        var loggedIn = CoreAccount.IsLoggedInProvider;
        CoreAccount.IsLoggedInProvider = () => true;
        var shell = new ConditioningControlPanel.Avalonia.Views.Windows.MainShellWindow();
        try
        {
            var load = new TaskCompletionSource();
            var refresh = shell.RefreshAfterLoginAsync(load.Task);
            s.UserDisplayName = "Adopted";
            s.IsSeason0Og = true;
            load.SetResult();
            await refresh;
            Assert.Equal("⭐ Adopted", shell.SettingsPage!.FindControl<global::Avalonia.Controls.TextBlock>("TxtLoggedInName")!.Text);
        }
        finally
        {
            shell.Close();
            (s.UserDisplayName, s.IsSeason0Og) = old;
            CoreAccount.IsLoggedInProvider = loggedIn;
        }
    });
}
