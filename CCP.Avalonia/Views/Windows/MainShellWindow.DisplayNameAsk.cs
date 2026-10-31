using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Startup;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private string? _nameAskedForUid;
    private int _nameAskGeneration;
    private bool _nameAskClosed;
    internal Task? NameAskTask;

    // WPF MainWindow.xaml.cs:726 OnProfileLoaded; DisplayNameAsk.cs:34-52.
    private void HookDisplayNameAsk()
    {
        AccountSeed.ProfileLoaded += OnNameProfileLoaded;
        Opened += (_, _) => QueueDisplayNameAskIfNeeded();
        Closed += (_, _) => { _nameAskClosed = true; _nameAskGeneration++; AccountSeed.ProfileLoaded -= OnNameProfileLoaded; };
    }

    private void OnNameProfileLoaded() => Dispatcher.UIThread.Post(QueueDisplayNameAskIfNeeded, DispatcherPriority.Normal);

    private void QueueDisplayNameAskIfNeeded()
    {
        var uid = CoreSettings.Current.UnifiedId;
        if (_nameAskClosed || string.IsNullOrEmpty(uid) || _nameAskedForUid == uid
            || AccountSeed.LoadedProfileUid != uid
            || !NeedsDisplayNameRule.ShouldAsk(true, CoreSettings.Current.UserDisplayName, null)) return;
        _nameAskedForUid = uid;
        var generation = ++_nameAskGeneration;
        NameAskTask = RunQueuedNameAskAsync(uid, generation);
    }

    // Name-only startup queue: WPF StartupPresenter.cs:42-55,440-477,569-587.
    // Other startup modal producers still await the general priority ladder.
    private async Task RunQueuedNameAskAsync(string uid, int generation)
    {
        await Task.Delay(750);
        var waited = 0;
        bool Parked() => !IsVisible || WindowState == WindowState.Minimized
            || ConditioningControlPanel.Avalonia.Views.Games.GameWindow.ActiveOpen() != null;
        bool Ready() => StartupQueueCore.CanStartModal(IsStartupDialogShowing || OwnedWindows.Any(w => w.IsVisible),
            AppUpdater.IsUpdateDialogActive, CoreTutorial.IsActive, IsVisible, Parked());
        while (!_nameAskClosed && generation == _nameAskGeneration && !Ready() && waited < 300000)
        {
            await Task.Delay(500);
            if (!Parked()) waited += 500;
        }
        if (_nameAskClosed || generation != _nameAskGeneration || !Ready() || CoreSettings.Current.UnifiedId != uid) return;
        // WPF DisplayNameAsk.cs:48: release the latch for a later profile load, not a timer retry.
        if (FriendsLanding.World().Holding) { _nameAskedForUid = null; return; }
        IsStartupDialogShowing = true;
        try { await ShowDisplayNameAsk(this, null); }
        finally { IsStartupDialogShowing = false; }
    }

    // WPF MainWindow.DisplayNameAsk.cs:25-30,56-75.
    internal async Task AskDisplayNameAfterSignIn(Window owner, string? name, bool? serverSaysNeeded)
    {
        if (!NeedsDisplayNameRule.ShouldAsk(!string.IsNullOrEmpty(CoreSettings.Current.UnifiedId), name, serverSaysNeeded)) return;
        _nameAskedForUid = CoreSettings.Current.UnifiedId;
        _nameAskGeneration++; // a sign-in already answered the pending restored-profile offer
        await ShowDisplayNameAsk(owner.IsVisible ? owner : this, serverSaysNeeded);
    }

    private async Task ShowDisplayNameAsk(Window owner, bool? serverSaysNeeded)
    {
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
