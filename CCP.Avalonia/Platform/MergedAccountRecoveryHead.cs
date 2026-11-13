using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel.Avalonia.Platform;
/// <summary>WPF MergedAccountRecoveryHead (App.xaml.cs:416-419): the one place this head seeds contract D's swap seams,
/// called from <see cref="AccountSeed.Seed"/> once the providers exist. Preserves the merged person's local data.</summary>
internal static class MergedAccountRecoveryHead
{
    private static readonly System.Threading.SemaphoreSlim AuthGate = new(1, 1);
    internal static void Seed()
    {
        MergedAccountRecovery.StopSync = () => { CoreSettings.Current.MergedSignInPending = true; AccountSeed.Sync?.Reset(); };
        MergedAccountRecovery.StampQuests = id => App.Quests?.StampOwner(id);
        MergedAccountRecovery.Reauthenticate = ReauthenticateAsync;
        MergedAccountRecovery.ReloadProfile = () => Dispatcher.UIThread.InvokeAsync(async () =>
        {
            await AccountSeed.LoadProfileAsync();
            // WPF restarts the heartbeat after the attempt, even if the profile could not be read.
            // Only a successful read marks the profile loaded and permits pushes.
            if (AccountSeed.Sync is { HeartbeatRunning: false } sync) sync.StartHeartbeat();
        });
        MergedAccountRecovery.Finished = (canonical, reauthed) => Dispatcher.UIThread.Post(() =>
        {
            var shell = AccountSeed.RecoveryShell();
            // WPF MainWindow.Login.cs:51-58: UpdateQuickLoginUI also repaints the banner; the section repaints provider and linking rows.
            shell?.UpdateQuickLoginUI(accountChanged: true);
            shell?.AppSettingsPage?.SectionAccount.Refresh();
            if (reauthed)
            {
                InvitePanel.ArmExpiry();
                return;
            }
            var item = new InboxItem
            {
                Key = "merged-account-signin:" + canonical,
                Glyph = "🔗",
                Title = Loc.Get("account_merged_signin_title"),
                Summary = Loc.Get("account_merged_signin_summary"),
                Open = () => { if (AccountSeed.RecoveryShell() is { } s) _ = s.OpenLoginAfterMergedSwap(); },
            };
            // There may be no shell yet, or the launcher or a game may own the screen.
            if (shell == null || !shell.IsVisible || shell.WindowState == global::Avalonia.Controls.WindowState.Minimized
                || LauncherWindow.Instance is { IsVisible: true } || PanicSurfaces.AnyOwnsTheScreen())
                StartupLadder.Inbox.File(item);
            else
                StartupLadder.PresentOrInbox(item);
        });
        ConditioningControlPanel.Services.BackRoom.BackRoomApi.MergedReply = MergedAccountRecovery.TryHandle;
        V2AuthService.MergedRecovery = MergedAccountRecovery.TryHandleAsync;
        AiService.MergedAccountHook = response => MergedAccountRecovery.TryHandleAsync(response);
        // WPF RemoteControlService.cs:129 AuthPostAsync: the Remote Control door.
        RemoteRelay.MergedRecovery = response => MergedAccountRecovery.TryHandleAsync(response);
    }

    // WPF App.ReauthenticateAfterMergedSwapAsync + EnsureAuthTokenAsync (App.xaml.cs:3851-3930): serialized, rechecked
    // inside the gate, providers in order Patreon, Discord, SubscribeStar.
    internal static async Task<bool> ReauthenticateAsync()
    {
        await AuthGate.WaitAsync();
        try
        {
            if (!string.IsNullOrEmpty(CoreAccount.UnifiedUserId) && !string.IsNullOrEmpty(CoreSettings.Current.AuthToken)) return true;
            if (AccountSeed.Patreon?.IsAuthenticated == true && await ExchangeAsync(AccountSeed.Patreon.GetAccessToken, (v, t) => v.AuthenticateWithPatreonAsync(t))) return true;
            if (AccountSeed.Discord?.IsAuthenticated == true && await ExchangeAsync(AccountSeed.Discord.GetAccessToken, (v, t) => v.AuthenticateWithDiscordAsync(t))) return true;
            if (AccountSeed.SubscribeStar?.IsAuthenticated == true && await ExchangeAsync(AccountSeed.SubscribeStar.GetAccessToken, (v, t) => v.AuthenticateWithSubstarAsync(t))) return true;
            CoreSettings.Current.MergedSignInPending = true;
            return false;
        }
        finally { AuthGate.Release(); }
    }

    private static async Task<bool> ExchangeAsync(Func<string?> getToken,
        Func<V2AuthService, string, Task<V2AuthService.V2AuthResponse>> authenticate)
    {
        try
        {
            var token = getToken();
            if (string.IsNullOrEmpty(token)) return false;
            var result = await authenticate(AccountSeed.NewV2(), token);
            // WPF App.xaml.cs:3873-3877 adopts the user even without a token; only a token ends the loop.
            if (!result.Success || result.User == null) return false;
            var now = DateTime.UtcNow;
            AccountSeed.ApplyLoginUserData(CoreSettings.Current, result.User, result.AuthToken, now);   // keeps a pending Descent migration's ledger, as LoginDialog
            InviteRules.ApplyGrant(CoreSettings.Current, InviteRules.ParseUtc(result.User.InviteGrantUntilRaw), now);
            CoreAccount.UnifiedUserId = result.User.UnifiedId;
            var minted = !string.IsNullOrEmpty(result.User.UnifiedId) && !string.IsNullOrEmpty(result.AuthToken);
            if (minted) CoreSettings.Current.MergedSignInPending = false;
            CoreSettings.Save();
            return minted;
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Merged account provider sign-in failed");
            return false;
        }
    }
}
