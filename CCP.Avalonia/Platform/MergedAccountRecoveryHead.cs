using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Platform;

/// <summary>WPF MergedAccountRecoveryHead: preserve the merged person's local data.</summary>
internal static class MergedAccountRecoveryHead
{
    internal static void Seed()
    {
        MergedAccountRecovery.StopSync = () => AccountSeed.Sync?.Reset();
        MergedAccountRecovery.StampQuests = id => App.Quests?.StampOwner(id);
        // Provider token exchange is part B. Until then every swap offers the ordinary sign-in.
        MergedAccountRecovery.Reauthenticate = null;
        MergedAccountRecovery.ReloadProfile = null;
        MergedAccountRecovery.Finished = (canonical, reauthed) => Dispatcher.UIThread.Post(() =>
        {
            var shell = MainShellWindow.Current;
            shell?.UpdateQuickLoginUI();
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
        V2AuthService.MergedRecovery = MergedAccountRecovery.TryHandleAsync;
    }

    private static async System.Threading.Tasks.Task OpenSignInAsync()
    {
        var shell = MainShellWindow.Current;
        if (shell == null) return;
        if (await MessageDialog.ConfirmAsync(shell, Loc.Get("account_merged_signin_title"),
            Loc.Get("account_merged_signin_body")))
            await shell.OpenUnifiedLoginDialog();
    }
}
