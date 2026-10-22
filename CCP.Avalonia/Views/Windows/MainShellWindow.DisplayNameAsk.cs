// WPF MainWindow.DisplayNameAsk.cs (bc0bb187e, 25d180311): required account name.
using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private string? _nameAskedForUid;
    private bool _nameSignInActive;

    private void InitializeDisplayNameAsk()
    {
        AccountSeed.ProfileLoaded += OnNameProfileLoaded;
        Closed += (_, _) => AccountSeed.ProfileLoaded -= OnNameProfileLoaded;
    }

    private void OnNameProfileLoaded() => Dispatcher.UIThread.Post(QueueDisplayNameAskIfNeeded);

    // The profile-loaded signal is raised only after adoption. Do not ask during the sign-in
    // dialog's own load: its success path asks synchronously before the other post-login notices.
    internal void QueueDisplayNameAskIfNeeded()
    {
        var s = CoreSettings.Current;
        if (_nameSignInActive || string.IsNullOrEmpty(s.UnifiedId) || _nameAskedForUid == s.UnifiedId
            || !NeedsDisplayNameRule.ShouldAsk(true, s.UserDisplayName, null)) return;
        _nameAskedForUid = s.UnifiedId;
        // ponytail: this head has no modal startup ladder. Use WPF's dispatcher fallback; a
        // session/Lockdown or another modal defers to the next profile load or next launch.
        Dispatcher.UIThread.Post(async () =>
        {
            if (NameAskHeld() || OwnedWindows.Any(w => w.IsVisible)) { _nameAskedForUid = null; return; }
            await ShowDisplayNameAskAsync(this, null);
        }, DispatcherPriority.Normal);
    }

    private static bool NameAskHeld() => FriendsLanding.World().Holding;

    internal async Task AskDisplayNameAfterSignInAsync(Window owner, string? name, bool? needed)
    {
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(CoreSettings.Current.UnifiedId), name, needed)) return;
        if (NameAskHeld()) { _nameAskedForUid = null; return; }
        _nameAskedForUid = CoreSettings.Current.UnifiedId;
        await ShowDisplayNameAskAsync(owner.IsVisible ? owner : this, needed);
    }

    private async Task ShowDisplayNameAskAsync(Window owner, bool? needed)
    {
        var s = CoreSettings.Current;
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(s.UnifiedId), s.UserDisplayName, needed)) return;
        try
        {
            var suggestion = NeedsDisplayNameRule.Suggest(AccountSeed.Discord?.Username, AccountSeed.Discord?.DisplayName);
            await new DisplayNameDialog(suggestion, SaveRequiredDisplayNameAsync).ShowDialogSafe<bool>(owner);
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Display name ask failed"); }
    }

    internal async Task<string?> SaveRequiredDisplayNameAsync(string name)
    {
        var id = CoreSettings.Current.UnifiedId;
        if (string.IsNullOrEmpty(id)) return Loc.Get("msg_failed_to_change_display_name");
        var client = AccountSeed.NewV2();
        // The change endpoint accepts our own name. Only after refusal ask whether it is taken.
        var (ok, error, saved) = await client.ChangeDisplayNameAsync(id, name);
        if (!ok)
            return await client.IsDisplayNameAvailableAsync(name) == false
                ? Loc.GetF("login_name_already_taken", name)
                : error ?? Loc.Get("msg_failed_to_change_display_name");
        if (CoreSettings.Current.UnifiedId != id) return Loc.Get("msg_failed_to_change_display_name");
        CoreSettings.Current.UserDisplayName = saved ?? name;
        CoreSettings.Save();
        UpdateQuickLoginUI();
        UpdateBannerWelcomeMessage();
        if (ProfilePage?.FindControl<TextBlock>("TxtProfileViewerName") is { } label
            && label.Text == Loc.Get("account_name_not_set")) label.Text = saved ?? name;
        return null;
    }
}
