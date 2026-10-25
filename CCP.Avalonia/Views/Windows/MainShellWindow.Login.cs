// PORTED from ConditioningControlPanel/MainWindow/MainWindow.Login.cs (399 lines): the login dialog,
// logout and the painting of signed-in state.
//
// Profile load / known-fields sync and account-switch clearing live in AccountSeed / LoginDialog.
// ponytail: broader profile fields are still pending their read-before-write port (see shell-login ledger).

using System;
using Avalonia.Controls;
using System.Threading.Tasks;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>
        /// Paints every surface that changes with signed-in-ness: the login button and the
        /// signed-in strip on the Settings door, and the login overlays that gate the Quests and
        /// Enhancements tabs.
        ///
        /// <para>Controls are found by name through the hosting view rather than through a
        /// generated field, so a view this head does not carry is skipped instead of throwing -
        /// and so it does not matter which XAML loader each tab view happened to use.</para>
        ///
        /// </summary>
        /// <param name="accountChanged">A real sign-in/out: the lapse pass saves, as WPF's does.</param>
        internal void UpdateQuickLoginUI(bool accountChanged = false)
        {
            try
            {
                var s = CoreSettings.Current;
                // Not WPF's `s.UnifiedId ||`: a stored id with nothing to prove it is not restored (AccountSeed.RestoreSession).
                var isLoggedIn = CoreAccount.IsLoggedIn;
                var displayName = s.UserDisplayName ?? AccountSeed.Patreon?.DisplayName ?? AccountSeed.Discord?.DisplayName
                                  ?? AccountSeed.SubscribeStar?.DisplayName ?? "User";

                var settings = SettingsPage;
                SetVisible(settings?.FindControl<Button>("BtnUnifiedLogin"), !isLoggedIn);
                SetVisible(settings?.FindControl<Border>("LoggedInStatusPanel"), isLoggedIn);

                SetVisible(Named<Tabs.QuestsTabView>("QuestsTab")?.FindControl<Border>("QuestsLoginOverlay"), !isLoggedIn);
                SetVisible(Named<Tabs.EnhancementsTabView>("EnhancementsTab")?.FindControl<Border>("EnhancementsLoginOverlay"), !isLoggedIn);

                if (isLoggedIn && settings?.FindControl<TextBlock>("TxtLoggedInName") is { } name)
                    name.Text = s.IsSeason0Og ? $"⭐ {displayName}" : displayName;
                // Sign-in, startup restore and logout all land here; WPF repaints the header on
                // each (Login.cs:198 UpdateLevelDisplay, OnProfileLoaded).
                UpdateLevelDisplay();
                RefreshProfileBubble();   // WPF UpdateXPBarLoginState: the bubble paints the same identity
                // WPF UpdatePatreonUI -> RefreshEntitlementVeils: an account change moves every veil.
                RefreshEntitlementVeils(persist: accountChanged);
                // WPF Patreon.cs:294: the pass is per-account, so sign-in/out moves the intake door.
                App.IntakePass.RaiseChanged();
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "UpdateQuickLoginUI failed");
            }
        }

        private static bool _notRememberedShown;

        /// <summary>WPF OpenUnifiedLoginDialog, up to the sync it starts (units 6-7). Then, once per run,
        /// the "sign-in not remembered" notice when the tokens could only be kept in memory.</summary>
        internal async Task OpenUnifiedLoginDialog(Window? owner = null)
        {
            var dialog = new LoginDialog();
            if (await dialog.ShowDialogSafe<bool>(owner ?? this) && dialog.Result is not null)
            {
                UpdateQuickLoginUI(accountChanged: true);
                if (SecretStore.NotRemembered && !_notRememberedShown)
                {
                    _notRememberedShown = true;
                    await MessageDialog.ShowAsync(owner ?? this, Loc.Get("login_not_remembered_title"), Loc.Get("login_not_remembered_body"));
                }
                // WPF Login.cs:214: the sharing question does not wait for the background profile load,
                // and the profile load does not wait for the answer.
                var offer = AccountSeed.Discord?.IsAuthenticated == true
                    ? OfferAchievementSharingAfterDiscordLink() : Task.CompletedTask;
                // The cloud profile may adopt a different level (WPF OnProfileLoaded -> UpdateLevelDisplay).
                try { await dialog.ProfileLoad; } catch (Exception ex) { Serilog.Log.Debug("ProfileLoad: {E}", ex.Message); }
                UpdateLevelDisplay();
                await offer;
            }
        }

        /// <summary>WPF BtnQuickLogout_Click (AccountSeed.Logout carries the pre-logout sync and progression clear).</summary>
        internal async void Logout() => await LogoutAsync();

        internal async Task LogoutAsync()
        {
            await AccountSeed.Logout();
            UpdateQuickLoginUI(accountChanged: true);
            if (SecretStore.ClearFailed)
                await MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.Get("logout_not_complete_body"));
        }

        private bool _achievementShareOfferUp;

        /// <summary>WPF Patreon.cs:836, called after sign-in (Login.cs:214). A provider link is not
        /// consent to sharing: ask separately, with the panel as owner. A game or a hidden panel
        /// parks a keyed Inbox row; only its explicit Open bypasses that deferral.</summary>
        internal async Task OfferAchievementSharingAfterDiscordLink(bool userAsked = false)
        {
            if (AccountSeed.Discord?.IsAuthenticated != true || _achievementShareOfferUp) return;
            var s = CoreSettings.Current;
            var routing = AchievementSharePromptDecision.Decide(s.DiscordShareAchievements,
                PanicSurfaces.AnyOwnsTheScreen(), LauncherWindow.Instance is { IsVisible: true },
                IsVisible && WindowState != WindowState.Minimized, userAsked);
            if (routing == AchievementSharePromptRouting.Skip) return;
            if (routing == AchievementSharePromptRouting.Inbox)
            {
                // File directly: the passive presenter does not yet sample game / launcher state.
                // Re-routing through PresentOrInbox could open precisely over the game we just saw.
                StartupLadder.Inbox.File(new InboxItem
                {
                    Key = "discord-share-achievements",
                    Title = Loc.Get("inbox_discord_share_title"),
                    Summary = Loc.Get("inbox_discord_share_summary"),
                    Open = () => _ = OfferAchievementSharingAfterDiscordLink(userAsked: true),
                });
                return;
            }
            _achievementShareOfferUp = true;
            try
            {
                if (!await MessageDialog.ConfirmAsync(this, Loc.Get("title_discord_linked"),
                    Loc.Get("msg_discord_share_achievements_prompt"),
                    okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"))) return;
                s.DiscordShareAchievements = true;
                CoreSettings.Save();
                if (ProfilePage?.PrivacyPanel.FindControl<CheckBox>("ChkDiscordTabShareAchievements") is { } checkbox)
                    checkbox.IsChecked = true;
                UpdateProfileSharingSummary();
            }
            finally { _achievementShareOfferUp = false; }
        }

        private static void SetVisible(Control? control, bool visible)
        {
            if (control is not null) control.IsVisible = visible;
        }
    }
}
