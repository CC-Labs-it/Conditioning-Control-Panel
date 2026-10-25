using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Platform;

// WPF MainWindow.DisplayNameAsk: restored profiles join the modal ladder at priority 40.
internal sealed class RequiredNameAsk
{
    private string? _askedUid;
    private static DisplayNameDialog? _active;
    internal void ProfileLoaded()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ProfileLoaded); return; }
        var uid = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(uid) || _askedUid == uid || !NeedsDisplayNameRule.ShouldAsk(true, CoreSettings.Current.UserDisplayName, null)) return;
        _askedUid = uid;
        StartupLadder.EnqueueModal("display-name", 40, async owner =>
        {
            if (FriendsLanding.World().Holding) { _askedUid = null; return; }
            if (CoreSettings.Current.UnifiedId != uid || !NeedsDisplayNameRule.ShouldAsk(true, CoreSettings.Current.UserDisplayName, null)) return;
            var dialog = new DisplayNameDialog(NeedsDisplayNameRule.Suggest(AccountSeed.Discord?.Username, AccountSeed.Discord?.DisplayName), SaveAsync);
            _active = dialog;
            try { await dialog.ShowDialog<bool>(owner); }
            finally { if (_active == dialog) _active = null; }
        }, () => _askedUid = null);
    }

    internal static void CloseForPanic() => _active?.Close(false);

    private static async Task<string?> SaveAsync(string name)
    {
        var (ok, error, saved) = await CoreAccount.ChangeDisplayNameAsync(name);
        if (!ok) return error ?? Loc.Get("msg_failed_to_change_display_name");
        CoreSettings.Current.UserDisplayName = saved ?? name;
        CoreSettings.Save();
        Views.Windows.MainShellWindow.Current?.UpdateQuickLoginUI();
        return null;
    }
}
