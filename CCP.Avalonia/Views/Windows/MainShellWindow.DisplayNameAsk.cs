using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    // WPF MainWindow.DisplayNameAsk.cs:25-30,56-75. Startup profile-load queue remains part B.
    internal async Task AskDisplayNameAfterSignIn(Window owner, string? name, bool? serverSaysNeeded)
    {
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(CoreSettings.Current.UnifiedId), name, serverSaysNeeded)) return;
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(CoreSettings.Current.UnifiedId),
                CoreSettings.Current.UserDisplayName, serverSaysNeeded)) return;
        try
        {
            var suggestion = NeedsDisplayNameRule.Suggest(AccountSeed.Discord?.Username, AccountSeed.Discord?.DisplayName);
            var dialog = new DisplayNameDialog(suggestion, SaveRequiredDisplayNameAsync);
            await dialog.ShowDialogSafe<bool>(owner.IsVisible ? owner : this);
        }
        catch (System.Exception ex) { Serilog.Log.Warning(ex, "Display name: ask failed"); }
    }

    // WPF MainWindow.DisplayNameAsk.cs:78-108: change first, check only a refusal.
    internal async Task<string?> SaveRequiredDisplayNameAsync(string name)
    {
        var (error, saved) = await ChangeRequiredDisplayNameAsync(name);
        if (error != null) return error;
        CoreSettings.Current.UserDisplayName = saved;
        CoreSettings.Save();
        UpdateQuickLoginUI();
        UpdateBannerWelcomeMessage();
        if (ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName") is { } label
            && label.Text == Loc.Get("account_name_not_set")) label.Text = saved;
        return null;
    }

    // Kept separate from repainting so the change/check ordering can be tested without fonts.
    internal static async Task<(string? Error, string? Name)> ChangeRequiredDisplayNameAsync(string name)
    {
        var (ok, error, saved) = await CoreAccount.ChangeDisplayNameAsync(name);
        if (!ok)
        {
            if (await AccountSeed.NewV2().IsDisplayNameAvailableAsync(name) == false)
                return (Loc.GetF("login_name_already_taken", name), null);
            return (error ?? Loc.Get("msg_failed_to_change_display_name"), null);
        }
        return (null, saved ?? name);
    }
}
