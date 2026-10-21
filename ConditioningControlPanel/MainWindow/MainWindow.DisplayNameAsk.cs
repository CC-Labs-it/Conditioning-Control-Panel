using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel
{
    // Required display name (onboarding, owner call 2026-10-20). A signed-in account with no name
    // is asked right after a sign-in and, for a restored session, once its profile loads at
    // startup (through the startup ladder). Closing the dialog is allowed: the next launch asks
    // again. One ask per account per launch, whichever path gets there first.
    public partial class MainWindow
    {
        private string? _nameAskedForUid;

        private static bool IsSignedInForName => !string.IsNullOrEmpty(App.Settings?.Current?.UnifiedId);

        /// <summary>
        /// Right after a successful sign-in, over the window the sign-in ran over. Synchronous on
        /// purpose: the sign-in dialog has just closed, and any prompt queued behind it (Discord
        /// achievement sharing) waits its turn instead of stacking on top.
        /// </summary>
        private void AskDisplayNameAfterSignIn(Window owner, string? name, bool? serverSaysNeeded)
        {
            if (!NeedsDisplayNameRule.ShouldAsk(IsSignedInForName, name, serverSaysNeeded)) return;
            _nameAskedForUid = App.Settings?.Current?.UnifiedId;
            ShowDisplayNameAsk(owner.IsVisible ? owner : this, serverSaysNeeded);
        }

        /// <summary>A profile load (startup restore or later): queue the ask on the ladder.</summary>
        private void QueueDisplayNameAskIfNeeded()
        {
            var uid = App.Settings?.Current?.UnifiedId;
            if (string.IsNullOrEmpty(uid) || _nameAskedForUid == uid) return;
            // A read-before-write repaint can fire before the sync adopts the server's name.
            if (App.ProfileSync?.HasLoadedProfile != true) return;
            if (!NeedsDisplayNameRule.ShouldAsk(true, App.Settings?.Current?.UserDisplayName, null)) return;
            _nameAskedForUid = uid;
            App.Logger?.Information("Display name: account has none, queueing the ask");
            // The ladder's modal pump never waits for a session or a Lockdown, and a first profile
            // load can land mid-run (offline at launch): a hold at show time puts the ask back, for
            // the next profile load or the next launch.
            if (App.StartupLadder != null)
                App.StartupLadder.EnqueueModal("display-name", 40, owner =>
                {
                    if (Services.Friends.FriendsLanding.Holding()) { _nameAskedForUid = null; return; }
                    ShowDisplayNameAsk(owner ?? this, null);
                });
            else
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() => ShowDisplayNameAsk(this, null)));
        }

        /// <summary>Blocks until the dialog closes (the ladder measures "done" by the return).</summary>
        private void ShowDisplayNameAsk(Window owner, bool? serverSaysNeeded)
        {
            // Re-read: a name may have landed, or the account signed out, while this waited.
            if (!NeedsDisplayNameRule.ShouldAsk(IsSignedInForName, App.Settings?.Current?.UserDisplayName, serverSaysNeeded)) return;
            try
            {
                var suggestion = NeedsDisplayNameRule.Suggest(App.Discord?.Username, App.Discord?.DisplayName);
                var dialog = new DisplayNameDialog(suggestion, SaveRequiredDisplayNameAsync)
                {
                    Owner = owner,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                if (dialog.ShowDialog() != true)
                    App.Logger?.Information("Display name: ask closed without a name, asking again next launch");
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "Display name: ask failed");
            }
        }

        /// <summary>Null when saved, else the line the dialog shows.</summary>
        private async Task<string?> SaveRequiredDisplayNameAsync(string name)
        {
            if (App.ProfileSync == null) return Loc.Get("msg_failed_to_change_display_name");

            // The change first: check-name says "taken" for the asker's OWN name, and a veteran whose
            // stored name is their patron name types exactly that (the change route accepts it, and a
            // refused name never starts its cooldown). Only a refusal asks which line to show.
            var (ok, error, saved) = await App.ProfileSync.ChangeDisplayNameAsync(name);
            if (!ok)
            {
                if (await App.ProfileSync.IsDisplayNameAvailableAsync(name) == false)
                    return Loc.GetF("login_name_already_taken", name);
                return error ?? Loc.Get("msg_failed_to_change_display_name");
            }

            if (App.Settings?.Current != null)
            {
                App.Settings.Current.UserDisplayName = saved ?? name;
                App.Settings.Save();
            }
            UpdateQuickLoginUI();
            UpdateBannerWelcomeMessage();
            // Only the own-profile card wears the no-name label; another player's card stays put.
            if (DiscordTab.TxtProfileViewerName?.Text == Loc.Get("account_name_not_set"))
                DiscordTab.TxtProfileViewerName.Text = saved ?? name;
            return null;
        }
    }
}
