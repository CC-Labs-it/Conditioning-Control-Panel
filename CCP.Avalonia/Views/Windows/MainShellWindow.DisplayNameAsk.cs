using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.DisplayNameAsk.cs: server answers take precedence over a blank local name, and a save
    // never lands on an account that changed while the dialog was up.
    internal async Task AskDisplayNameAfterSignIn(Window owner, string? name, bool? serverSaysNeeded)
    {
        var uid = CoreSettings.Current.UnifiedId;
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(uid), name, serverSaysNeeded)) return;
        try
        {
            var suggestion = NeedsDisplayNameRule.Suggest(AccountSeed.Discord?.Username, AccountSeed.Discord?.DisplayName);
            await new DisplayNameDialog(suggestion, n => SaveRequiredDisplayNameAsync(n, uid)).ShowDialogSafe<bool>(owner.IsVisible ? owner : this);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display name ask failed"); }
    }

    private async Task<string?> SaveRequiredDisplayNameAsync(string name, string uid)
    {
        if (CoreSettings.Current.UnifiedId != uid) return Loc.Get("msg_failed_to_change_display_name");
        // WPF DisplayNameAsk.cs:82-86: the change first (check-name says "taken" for the asker's own name), then availability.
        var (ok, error, saved) = await NeedsDisplayNameRule.SaveRequiredAsync(name,
            CoreAccount.ChangeDisplayNameAsync, value => AccountSeed.NewV2().IsDisplayNameAvailableAsync(value));
        if (!ok) return error ?? Loc.Get("msg_failed_to_change_display_name");
        if (CoreSettings.Current.UnifiedId != uid) return Loc.Get("msg_failed_to_change_display_name");
        CoreSettings.Current.UserDisplayName = saved ?? name;
        CoreSettings.Save();
        UpdateQuickLoginUI();
        UpdateBannerWelcomeMessage(); // WPF MainWindow.DisplayNameAsk.cs:99
        var label = ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName");
        if (label?.Text == Loc.Get("account_name_not_set")) label.Text = saved ?? name;
        return null;
    }
}
