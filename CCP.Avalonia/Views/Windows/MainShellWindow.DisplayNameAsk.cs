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
    // WPF MainWindow.DisplayNameAsk.cs: server answers take precedence over a blank local name.
    internal async Task AskDisplayNameAfterSignIn(Window owner, string? name, bool? serverSaysNeeded)
    {
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(CoreSettings.Current.UnifiedId), name, serverSaysNeeded)) return;
        try
        {
            var suggestion = NeedsDisplayNameRule.Suggest(AccountSeed.Discord?.Username, AccountSeed.Discord?.DisplayName);
            await new DisplayNameDialog(suggestion, SaveRequiredDisplayNameAsync).ShowDialogSafe<bool>(owner.IsVisible ? owner : this);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display name ask failed"); }
    }

    private async Task<string?> SaveRequiredDisplayNameAsync(string name)
    {
        var (ok, error, saved) = await CoreAccount.ChangeDisplayNameAsync(name);
        if (!ok) return error ?? Loc.Get("msg_failed_to_change_display_name");
        CoreSettings.Current.UserDisplayName = saved ?? name;
        CoreSettings.Save();
        UpdateQuickLoginUI();
        UpdateBannerWelcomeMessage(); // WPF MainWindow.DisplayNameAsk.cs:99
        var label = ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName");
        if (label?.Text == Loc.Get("account_name_not_set")) label.Text = saved ?? name;
        return null;
    }
}
