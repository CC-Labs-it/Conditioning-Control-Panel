using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>This head's seams of Core <see cref="MergedAccountRecovery"/> (contract D), as WPF
/// MergedAccountRecoveryHead.Seed (Services/Account/MergedAccountRecoveryHead.cs:11), plus the Remote
/// Control door (WPF RemoteControlService.cs:129 AuthPostAsync). Seeded once, after <see cref="AccountSeed.Seed"/>.
/// ponytail: the other contract D doors (V2AuthService, Arcademy, Friends, BackRoom, AI) stay unseeded here.</summary>
internal static class MergedAccountHead
{
    /// <summary>Tests only: the shell the swap repaints and offers the sign-in over.</summary>
    internal static Func<MainShellWindow?> Shell = () => MainShellWindow.Current;

    internal static void Seed()
    {
        MergedAccountRecovery.StopSync = () =>
        {
            // Separate trys (WPF :15-19): a throwing stop must not skip the loaded-state reset.
            try { AccountSeed.Sync?.StopHeartbeat(); }
            catch (Exception ex) { Log.Debug("[MergedAccount] stopping timers failed: {Error}", ex.Message); }
            try { AccountSeed.Sync?.Reset(); }
            catch (Exception ex) { Log.Debug("[MergedAccount] sync state reset failed: {Error}", ex.Message); }
        };
        MergedAccountRecovery.StampQuests = id => App.Quests?.StampOwner(id);
        MergedAccountRecovery.Reauthenticate = ReauthenticateAsync;
        // WPF :23-28 LoadProfileAsync + StartHeartbeat; AccountSeed.LoadProfileAsync starts the heartbeat on success.
        MergedAccountRecovery.ReloadProfile = () => AccountSeed.LoadProfileAsync();
        MergedAccountRecovery.Finished = (canonical, reauthed) => Dispatcher.UIThread.Post(() =>
        {
            try { Shell()?.RefreshAccountUiAfterMergedSwap(); }
            catch (Exception ex) { Log.Debug("[MergedAccount] UI refresh failed: {Error}", ex.Message); }
            if (!reauthed) OfferSignIn(canonical);
        });
        RemoteRelay.MergedRecovery = response => MergedAccountRecovery.TryHandleAsync(response);
    }

    private static bool NeedsAuthToken() =>
        string.IsNullOrEmpty(CoreAccount.UnifiedUserId) || string.IsNullOrEmpty(CoreSettings.Current.AuthToken);

    /// <summary>WPF App.ReauthenticateAfterMergedSwapAsync (App.xaml.cs:4615) over EnsureAuthTokenAsync (:4556):
    /// each signed-in provider in turn, until one mints a token for the canonical.</summary>
    internal static async Task<bool> ReauthenticateAsync()
    {
        if (!NeedsAuthToken()) return true;
        if (AccountSeed.Patreon is { IsAuthenticated: true } patreon)
            await EnsureAuthTokenAsync("Patreon", patreon.GetAccessToken(), (v2, t) => v2.AuthenticateWithPatreonAsync(t));
        if (NeedsAuthToken() && AccountSeed.Discord is { IsAuthenticated: true } discord)
            await EnsureAuthTokenAsync("Discord", discord.GetAccessToken(), (v2, t) => v2.AuthenticateWithDiscordAsync(t));
        if (NeedsAuthToken() && AccountSeed.SubscribeStar is { IsAuthenticated: true } substar)
            await EnsureAuthTokenAsync("SubscribeStar", substar.GetAccessToken(), (v2, t) => v2.AuthenticateWithSubstarAsync(t));
        return !NeedsAuthToken();
    }

    private static async Task EnsureAuthTokenAsync(string provider, string? accessToken,
        Func<V2AuthService, string, Task<V2AuthService.V2AuthResponse>> authenticate)
    {
        if (string.IsNullOrEmpty(accessToken)) return;
        try
        {
            var v2 = AccountSeed.NewV2();
            var result = await authenticate(v2, accessToken);
            if (result.Success && result.User != null)
            {
                // WPF V2AuthServiceHead.ApplyUserDataToSettings (:15-23), then App.UnifiedUserId.
                var s = CoreSettings.Current;
                ProfileAdopt.ApplyUserData(s, result.User, result.AuthToken, DateTime.UtcNow);
                ConditioningControlPanel.Services.Invites.InviteRules.ApplyGrant(s, ConditioningControlPanel.Services.Invites.InviteRules.ParseUtc(result.User.InviteGrantUntilRaw), DateTime.UtcNow);
                CoreSettings.Save();
                CoreAccount.UnifiedUserId = result.User.UnifiedId;
                Log.Information("[MergedAccount] {Provider} re-auth landed on {Id}", provider, result.User.UnifiedId);
            }
            else Log.Warning("[MergedAccount] {Provider} re-auth failed: {Error}", provider, result.Error);
        }
        catch (Exception ex) { Log.Warning(ex, "[MergedAccount] {Provider} re-auth threw", provider); }
    }

    /// <summary>WPF OfferSignIn (MergedAccountRecoveryHead.cs:59): opens now when nothing owns the user,
    /// otherwise one Inbox row per canonical.</summary>
    private static void OfferSignIn(string canonical) => StartupLadder.PresentOrInbox(new InboxItem
    {
        Key = "merged-account-signin:" + canonical,
        Title = Loc.Get("account_merged_signin_title"),
        Summary = Loc.Get("account_merged_signin_summary"),
        Open = () => { if (Shell() is { } shell) _ = shell.OpenLoginAfterMergedSwap(); },
    });
}
