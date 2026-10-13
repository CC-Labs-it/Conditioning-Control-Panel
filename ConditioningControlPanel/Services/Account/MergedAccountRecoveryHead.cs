using System;

namespace ConditioningControlPanel.Services
{
    /// <summary>
    /// The WPF seams of Core <see cref="MergedAccountRecovery"/> (contract D): what only this head
    /// can do around the identity swap. Seeded once in App startup.
    /// </summary>
    public static class MergedAccountRecoveryHead
    {
        public static void Seed()
        {
            MergedAccountRecovery.StopSync = () =>
            {
                // Separate trys (as WPF had): a throwing timer stop must not skip the defaults-guard reset.
                try { App.ProfileSync?.StopTimersForAccountSwap(); }
                catch (Exception ex) { App.Logger?.Debug("[MergedAccount] stopping timers failed: {Error}", ex.Message); }
                try { App.ProfileSync?.ResetLoadedProfileState(); }
                catch (Exception ex) { App.Logger?.Debug("[MergedAccount] sync state reset failed: {Error}", ex.Message); }
            };
            MergedAccountRecovery.StampQuests = id => App.Quests?.StampOwner(id);
            MergedAccountRecovery.Reauthenticate = App.ReauthenticateAfterMergedSwapAsync;
            MergedAccountRecovery.ReloadProfile = async () =>
            {
                if (App.ProfileSync == null) return;
                await App.ProfileSync.LoadProfileAsync();
                App.ProfileSync.StartHeartbeat();
            };
            MergedAccountRecovery.Finished = (canonical, reauthed) =>
            {
                RefreshAccountUi();
                if (!reauthed) OfferSignIn(canonical);
            };
        }

        private static void RefreshAccountUi()
        {
            try
            {
                var main = App.MainWindowRef;
                if (main == null) return;
                main.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { main.RefreshAccountUiAfterMergedSwap(); }
                    catch (Exception ex) { App.Logger?.Debug("[MergedAccount] UI refresh failed: {Error}", ex.Message); }
                }), System.Windows.Threading.DispatcherPriority.Normal);
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("[MergedAccount] UI refresh dispatch failed: {Error}", ex.Message);
            }
        }

        /// <summary>
        /// The passive surface for the no-credential case: opens at once when nothing owns the
        /// user, otherwise becomes an Inbox row. Keyed per canonical so a second refusal in the
        /// same session cannot post it twice.
        /// </summary>
        private static void OfferSignIn(string canonical)
        {
            var ladder = App.StartupLadder;
            var item = new Startup.InboxItem
            {
                Key = "merged-account-signin:" + canonical,
                Glyph = "🔗",
                Title = Localization.Loc.Get("account_merged_signin_title"),
                Summary = Localization.Loc.Get("account_merged_signin_summary"),
                Open = () => App.MainWindowRef?.OpenLoginAfterMergedSwap(),
            };

            if (ladder != null)
            {
                ladder.PresentOrInbox(item);
                return;
            }

            // No presenter yet (very early boot): the login button is still there, and the next
            // launch validates the restored session and lands in the same place.
            App.Logger?.Debug("[MergedAccount] no startup presenter; leaving the sign-in to the account button");
        }
    }
}
