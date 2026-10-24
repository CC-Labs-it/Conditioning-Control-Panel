using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Account;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // ponytail: restored-session priority-40 scheduling needs the modal startup ladder;
    // StartupLadder on this head currently arbitrates passive surfaces only.
    internal async Task AskDisplayNameAfterSignIn(Window owner, string? name, bool? serverSaysNeeded)
    {
        if (!NeedsDisplayNameRule.ShouldAsk(CoreAccount.IsLoggedIn, name, serverSaysNeeded)) return;
        if (!NeedsDisplayNameRule.ShouldAsk(CoreAccount.IsLoggedIn, CoreSettings.Current.UserDisplayName, serverSaysNeeded)) return;
        try
        {
            var suggestion = NeedsDisplayNameRule.Suggest(AccountSeed.Discord?.Username, AccountSeed.Discord?.DisplayName);
            await new DisplayNameDialog(suggestion, SaveRequiredDisplayNameAsync).ShowDialogSafe<bool>(owner.IsVisible ? owner : this);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display name: ask failed"); }
    }

    internal async Task<string?> SaveRequiredDisplayNameAsync(string name)
    {
        // Capture the account: a sign-out or switch while the network is pending must not rename its replacement.
        var uid = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(uid)) return Loc.Get("msg_failed_to_change_display_name");
        if (await AccountSeed.NewV2().IsDisplayNameAvailableAsync(name) == false)
            return Loc.GetF("login_name_already_taken", name);
        if (CoreSettings.Current.UnifiedId != uid) return Loc.Get("msg_failed_to_change_display_name");
        var (ok, error, saved) = await CoreAccount.ChangeDisplayNameAsync(name);
        if (!ok) return error ?? Loc.Get("msg_failed_to_change_display_name");
        if (CoreSettings.Current.UnifiedId != uid) return Loc.Get("msg_failed_to_change_display_name");
        CoreSettings.Current.UserDisplayName = saved ?? name;
        CoreSettings.Save();
        UpdateQuickLoginUI();
        UpdateBannerWelcomeMessage();
        var label = ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName");
        if (label?.Text == Loc.Get("account_name_not_set")) label.Text = saved ?? name;
        return null;
    }
}
