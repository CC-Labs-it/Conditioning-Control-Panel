using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Controls.Invites;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;
using ConditioningControlPanel.Services.Invites;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MergedAccountRecoveryHead: preserve the merged person's local data.</summary>
internal static class MergedAccountRecoveryHead
{
    internal static volatile bool PendingSignIn;
    internal static void Seed()
    {
        MergedAccountRecovery.StopSync = () => AccountSeed.Sync?.Reset();
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
            var shell = MainShellWindow.Current;
            // WPF MainWindow.Login.cs:51-58: repaint the account surfaces.
            shell?.AppSettingsPage?.RefreshSections();
            shell?.UpdateQuickLoginUI();
            shell?.UpdateBannerWelcomeMessage();
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
                Open = () => _ = OpenSignInAsync(),
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
    }

    // WPF App.xaml.cs:4615: try the signed-in providers in order until one mints a V2 token.
    private static async Task<bool> ReauthenticateAsync()
    {
        if (AccountSeed.Patreon?.IsAuthenticated == true &&
            await ExchangeAsync(AccountSeed.Patreon.GetAccessToken, (v, t) => v.AuthenticateWithPatreonAsync(t))) return true;
        if (AccountSeed.Discord?.IsAuthenticated == true &&
            await ExchangeAsync(AccountSeed.Discord.GetAccessToken, (v, t) => v.AuthenticateWithDiscordAsync(t))) return true;
        if (AccountSeed.SubscribeStar?.IsAuthenticated == true &&
            await ExchangeAsync(AccountSeed.SubscribeStar.GetAccessToken, (v, t) => v.AuthenticateWithSubstarAsync(t))) return true;
        PendingSignIn = true;
        CoreAccount.UnifiedUserId = null;
        return false;
    }

    private static async Task<bool> ExchangeAsync(Func<string?> getToken,
        Func<V2AuthService, string, Task<V2AuthService.V2AuthResponse>> authenticate)
    {
        try
        {
            var token = getToken();
            if (string.IsNullOrEmpty(token)) return false;
            var result = await authenticate(AccountSeed.NewV2(), token);
            // WPF App.xaml.cs:4578-4581 adopts the user even without a token; only a token ends the loop.
            if (!result.Success || result.User == null) return false;
            var now = DateTime.UtcNow;
            ProfileAdopt.ApplyUserData(CoreSettings.Current, result.User, result.AuthToken, now);
            InviteRules.ApplyGrant(CoreSettings.Current, InviteRules.ParseUtc(result.User.InviteGrantUntilRaw), now);
            CoreAccount.UnifiedUserId = result.User.UnifiedId;
            CoreSettings.Save();
            if (!string.IsNullOrEmpty(result.User.UnifiedId) && !string.IsNullOrEmpty(result.AuthToken)) PendingSignIn = false;
            return !string.IsNullOrEmpty(result.User.UnifiedId) && !string.IsNullOrEmpty(result.AuthToken);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Merged account provider sign-in failed");
            return false;
        }
    }

    private static async Task OpenSignInAsync()
    {
        var shell = MainShellWindow.Current;
        if (shell == null) return;
        if (await MessageDialog.ConfirmAsync(shell, Loc.Get("account_merged_signin_title"),
            Loc.Get("account_merged_signin_body")))
            await shell.OpenUnifiedLoginDialog();
    }
}
