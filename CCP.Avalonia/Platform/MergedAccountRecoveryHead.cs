using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Arcademy;
using ConditioningControlPanel.Services.BackRoom;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.Invites;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>
/// This head's seams of Core <see cref="MergedAccountRecovery"/> (contract D), ported from WPF
/// Services/Account/MergedAccountRecoveryHead.cs and App.ReauthenticateAfterMergedSwapAsync (App.xaml.cs:4461).
/// <see cref="Seed"/> runs right after <see cref="AccountSeed.Seed"/> and <see cref="AccountSeed.Sync"/> exist, and
/// BEFORE <see cref="WireDoors"/>: a door without the seams would drop the token and leave the user silently signed out
/// (decision 2026-10-22, docs/avalonia-decisions.md). Never opens the username picker: a provider that answers
/// needs-registration is skipped, as WPF's EnsureAuthTokenAsync does.
/// </summary>
internal static class MergedAccountRecoveryHead
{
    /// <summary>WPF App._authUpgradeGate: one provider exchange at a time, re-checked inside.</summary>
    private static readonly SemaphoreSlim AuthUpgradeGate = new(1, 1);

    internal const string InboxKeyPrefix = "merged-account-signin:";

    internal static void Seed()
    {
        // WPF StopTimersForAccountSwap + ResetLoadedProfileState: Reset stops the heartbeat and forgets the load.
        MergedAccountRecovery.StopSync = () => AccountSeed.Sync?.Reset();
        MergedAccountRecovery.StampQuests = id => App.Quests?.StampOwner(id);
        MergedAccountRecovery.Reauthenticate = ReauthenticateAsync;
        // AccountSeed.LoadProfileAsync marks the profile loaded, starts the heartbeat and pushes (WPF Load + StartHeartbeat).
        MergedAccountRecovery.ReloadProfile = () => AccountSeed.LoadProfileAsync();
        MergedAccountRecovery.Finished = (canonical, reauthed) =>
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // WPF RefreshAccountUiAfterMergedSwap (MainWindow.Login.cs:49): login, then the welcome banner.
                    MainShellWindow.Current?.UpdateQuickLoginUI();
                    MainShellWindow.Current?.UpdateBannerWelcomeMessage();
                }
                catch (Exception ex) { Log.Debug("[MergedAccount] UI refresh failed: {Error}", ex.Message); }
            });
            if (!reauthed) OfferSignIn(canonical);
        };
    }

    /// <summary>The doors WPF wires in App startup (App.xaml.cs:380-384, 473, 600). FriendsApi's is FriendsHead.Create.</summary>
    internal static void WireDoors()
    {
        V2AuthService.MergedRecovery = MergedAccountRecovery.TryHandleAsync;
        GoonSignalingClient.MergedAccountHook = MergedAccountRecovery.TryHandle;
        ArcademySyncService.MergedRecovery = MergedAccountRecovery.TryHandle;
        BackRoomApi.MergedReply = MergedAccountRecovery.TryHandle;
        AiService.MergedAccountHook = r => MergedAccountRecovery.TryHandleAsync(r);
    }

    /// <summary>WPF App.ReauthenticateAfterMergedSwapAsync: Patreon, then Discord, then SubscribeStar, while no token is held.</summary>
    internal static async Task<bool> ReauthenticateAsync()
    {
        if (!NeedsAuthTokenUpgrade()) return true;
        if (AccountSeed.Patreon is { IsAuthenticated: true } patreon)
            await EnsureAuthTokenAsync("Patreon", patreon.GetAccessToken, (v2, t) => v2.AuthenticateWithPatreonAsync(t));
        if (NeedsAuthTokenUpgrade() && AccountSeed.Discord is { IsAuthenticated: true } discord)
            await EnsureAuthTokenAsync("Discord", discord.GetAccessToken, (v2, t) => v2.AuthenticateWithDiscordAsync(t));
        if (NeedsAuthTokenUpgrade() && AccountSeed.SubscribeStar is { IsAuthenticated: true } substar)
            await EnsureAuthTokenAsync("SubscribeStar", substar.GetAccessToken, (v2, t) => v2.AuthenticateWithSubstarAsync(t));
        return !NeedsAuthTokenUpgrade();
    }

    private static bool NeedsAuthTokenUpgrade() =>
        string.IsNullOrEmpty(CoreAccount.UnifiedUserId) || string.IsNullOrEmpty(CoreSettings.Current.AuthToken);

    /// <summary>WPF App.EnsureAuthTokenAsync (App.xaml.cs:4403), with V2AuthServiceHead.ApplyUserDataToSettings.</summary>
    private static async Task EnsureAuthTokenAsync(string provider, Func<string?> getAccessToken,
        Func<V2AuthService, string, Task<V2AuthService.V2AuthResponse>> authenticate)
    {
        await AuthUpgradeGate.WaitAsync();
        try
        {
            if (!NeedsAuthTokenUpgrade()) return;
            var accessToken = getAccessToken();
            if (string.IsNullOrEmpty(accessToken)) return;
            var result = await authenticate(AccountSeed.NewV2(), accessToken);
            if (result.Success && result.User != null)
            {
                var s = CoreSettings.Current;
                ProfileAdopt.ApplyUserData(s, result.User, result.AuthToken, DateTime.UtcNow);
                InviteRules.ApplyGrant(s, InviteRules.ParseUtc(result.User.InviteGrantUntilRaw), DateTime.UtcNow);
                CoreSettings.Save();
                CoreAccount.UnifiedUserId = result.User.UnifiedId;
                Log.Information("[MergedAccount] {Provider} re-auth complete: {Id}", provider, result.User.UnifiedId);
            }
            else if (result.NeedsRegistration)
                Log.Information("[MergedAccount] {Provider} re-auth: needs registration, skipping", provider);
            else
                Log.Warning("[MergedAccount] {Provider} re-auth failed: {Error}", provider, result.Error);
        }
        catch (Exception ex) { Log.Warning(ex, "[MergedAccount] {Provider} re-auth failed", provider); }
        finally { AuthUpgradeGate.Release(); }
    }

    /// <summary>WPF OfferSignIn: opens now when nothing owns the user, else an Inbox row; one per canonical.</summary>
    private static void OfferSignIn(string canonical) => StartupLadder.PresentOrInbox(new InboxItem
    {
        Key = InboxKeyPrefix + canonical,
        Glyph = "🔗",
        Title = Loc.Get("account_merged_signin_title"),
        Summary = Loc.Get("account_merged_signin_summary"),
        Open = () => _ = OpenLoginAsync(),
    });

    /// <summary>WPF MainWindow.OpenLoginAfterMergedSwap: one line of explanation (OK/Cancel), then the ordinary sign-in.
    /// The swap already stored the canonical id, so LoginDialog reads the re-login as the same account and keeps progression.</summary>
    private static async Task OpenLoginAsync()
    {
        if (MainShellWindow.Current is not { } shell) return;
        if (!await MessageDialog.ConfirmAsync(shell, Loc.Get("account_merged_signin_title"), Loc.Get("account_merged_signin_body"))) return;
        await shell.OpenUnifiedLoginDialog();
    }
}
