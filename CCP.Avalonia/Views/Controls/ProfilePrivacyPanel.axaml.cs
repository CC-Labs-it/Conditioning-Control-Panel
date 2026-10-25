using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// WPF ProfilePrivacyPanel: presence, local sharing and push-on-change audience consent.
    /// Only explicitly chosen consent fields are sent, scoped to the account that chose them.
    /// </summary>
    public partial class ProfilePrivacyPanel : UserControl
    {
        public ProfilePrivacyPanel()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new ProfilePrivacyPanelViewModel();
            WirePresence();
            WireSharing("ChkDiscordTabShareAchievements", s => s.DiscordShareAchievements, (s, on) => s.DiscordShareAchievements = on);
            WireSharing("ChkDiscordTabShareLevelUps", s => s.DiscordShareLevelUps, (s, on) => s.DiscordShareLevelUps = on);
            WireSharing("ChkGoonRichPresence", s => s.GoonRichPresence, (s, on) => s.GoonRichPresence = on);
            WireSharing("ChkDiscordTabShowOnline", s => s.ShowOnlineStatus, (s, on) => s.ShowOnlineStatus = on, SyncBody.Field.ShowOnlineStatus);
            WireSharing("ChkDiscordTabAllowDm", s => s.AllowDiscordDm, (s, on) => s.AllowDiscordDm = on, SyncBody.Field.AllowDiscordDm);
            WireSharing("ChkDiscordTabSharePfp", s => s.ShareProfilePicture, (s, on) => s.ShareProfilePicture = on, SyncBody.Field.ShareProfilePicture);
            WireSharing("ChkPublicShareRealAvatar", s => s.PublicShareRealAvatar, (s, on) => s.PublicShareRealAvatar = on, SyncBody.Field.PublicShareAvatar);
            WireSharing("ChkGoonShareAvatar", s => s.GoonShareAvatar, (s, on) => s.GoonShareAvatar = on, SyncBody.Field.GoonShareAvatar);
            WireSharing("ChkGoonShareDiscordDm", s => s.GoonShareDiscordDm, (s, on) => s.GoonShareDiscordDm = on, SyncBody.Field.GoonShareDm);
            RefreshPrivacy();
            if (this.FindControl<Button>("BtnDiscordTabLogin") is { } login)
                login.Click += async (_, _) => await LoginAsync();
        }

        internal static System.Func<SyncBody.Field, System.Threading.Tasks.Task> PushPrivacy = field =>
        {
            if (Platform.AccountSeed.Sync is { } sync) return sync.PushPrivacyAsync(field);
            if (SyncPush.RememberPrivacyChange(CoreSettings.Current, field)) CoreSettings.Save();
            return System.Threading.Tasks.Task.CompletedTask;
        };

        private static async System.Threading.Tasks.Task PushSafe(SyncBody.Field field)
        {
            try { await PushPrivacy(field); }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Privacy switch: immediate sync push failed"); }
        }

        internal event System.Action? PrivacyChanged;

        private bool _paintingLocal;

        // Adapted from parity/trunk 4f739180ca68a1e45f62c70a78d2bfbd0f1afcc0.
        // Local sharing saves only; audience consent also requests the Core push-on-change path.
        private void WireSharing(string name, System.Func<global::ConditioningControlPanel.Models.AppSettings, bool> read,
            System.Action<global::ConditioningControlPanel.Models.AppSettings, bool> write, SyncBody.Field consent = SyncBody.Field.None)
        {
            if (this.FindControl<CheckBox>(name) is not { } box) return;
            box.IsCheckedChanged += (_, _) =>
            {
                if (_paintingLocal) return;
                var s = CoreSettings.Current;
                var on = box.IsChecked == true;
                if (read(s) == on) return;
                write(s, on);
                CoreSettings.Save();
                PrivacyChanged?.Invoke();
                if (consent != SyncBody.Field.None) _ = PushSafe(consent);
            };
        }

        internal void RefreshPrivacy()
        {
            _paintingLocal = true;
            try
            {
                var s = CoreSettings.Current;
                var discord = Platform.AccountSeed.Discord;
                var linked = discord?.IsAuthenticated == true;
                if (this.FindControl<TextBlock>("TxtDiscordTabStatus") is { } status)
                    status.Text = linked ? Loc.GetF("label_connected_as_0", discord!.Username) : Loc.Get("label_not_connected");
                if (this.FindControl<TextBlock>("TxtDiscordTabInfo") is { } info)
                    info.Text = Loc.Get(linked ? "label_discord_account_linked" : "label_link_discord_for_community_features");
                if (this.FindControl<Button>("BtnDiscordTabLogin")?.Content is TextBlock word)
                    word.Text = Loc.Get(linked ? "btn_logout" : string.IsNullOrEmpty(s.UnifiedId) ? "btn_login" : "btn_link_discord_2");
                // Paint before handling user changes; reattaching the long-lived panel must not push.
                PaintConsent("ChkDiscordTabShowOnline", s.ShowOnlineStatus);
                PaintConsent("ChkDiscordTabAllowDm", s.AllowDiscordDm);
                PaintConsent("ChkDiscordTabSharePfp", s.ShareProfilePicture);
                PaintConsent("ChkPublicShareRealAvatar", s.PublicShareRealAvatar);
                PaintConsent("ChkGoonShareAvatar", s.GoonShareAvatar);
                PaintConsent("ChkGoonShareDiscordDm", s.GoonShareDiscordDm);
                if (this.FindControl<CheckBox>("ChkDiscordTabShareAchievements") is { } achievements)
                    achievements.IsChecked = s.DiscordShareAchievements;
                if (this.FindControl<CheckBox>("ChkDiscordTabShareLevelUps") is { } levels)
                    levels.IsChecked = s.DiscordShareLevelUps;
                if (this.FindControl<CheckBox>("ChkGoonRichPresence") is { } goon)
                    goon.IsChecked = s.GoonRichPresence;
            }
            finally { _paintingLocal = false; }
        }

        private void PaintConsent(string name, bool on)
        {
            if (this.FindControl<CheckBox>(name) is not { } box) return;
            box.IsChecked = on;
        }

        internal System.Func<Views.Windows.MainShellWindow, System.Threading.Tasks.Task> DiscordAction = shell =>
            shell.AppSettingsPage?.FindControl<AppSettings.AccountSettingsSection>("SectionAccount")?.DiscordFromProfileAsync()
                ?? System.Threading.Tasks.Task.CompletedTask;

        internal async System.Threading.Tasks.Task LoginAsync(Views.Windows.MainShellWindow? parent = null)
        {
            try
            {
                var host = TopLevel.GetTopLevel(this) as Window;
                var shell = parent ?? host as Views.Windows.MainShellWindow ?? host?.Owner as Views.Windows.MainShellWindow;
                if (shell == null || CoreSettings.Current.OfflineMode) return;
                if (Platform.AccountSeed.Discord?.IsAuthenticated != true && host is Dialogs.ProfilePrivacyDialog dialog)
                    dialog.Close();
                await DiscordAction(shell);
            }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Privacy panel Discord action failed"); }
            finally { RefreshPrivacy(); }
        }

        private bool _paintingPresence;

        /// <summary>WPF ChkDiscordRichPresence_Changed (MainWindow.AccountShell.cs:279-316) and
        /// ChkShowLevelInPresence_Changed (MainWindow.Patreon.cs:902): presence never arms without a
        /// linked Discord; Show level updates the presence at once.</summary>
        private void WirePresence()
        {
            var rp = this.FindControl<CheckBox>("ChkDiscordTabRichPresence");
            var lvl = this.FindControl<CheckBox>("ChkDiscordTabShowLevel");
            PaintPresence();
            if (rp != null) rp.IsCheckedChanged += (_, _) =>
            {
                if (_paintingPresence) return;
                var s = CoreSettings.Current;
                bool on = rp.IsChecked == true;
                if (on && !s.HasLinkedDiscord)
                {
                    PaintPresence();
                    if (TopLevel.GetTopLevel(this) is Window { IsVisible: true } owner && !Tabs.SettingsTabView.SuppressDialogsForTest)
                        _ = Dialogs.MessageDialog.ShowAsync(owner, "Discord Not Linked",
                            Loc.Get("msg_discord_rich_presence_requires_a_linked_disco"));
                    return;
                }
                if (s.DiscordRichPresenceEnabled == on) return;
                s.DiscordRichPresenceEnabled = on;
                CoreSettings.Save();
                App.ApplyRichPresence();
            };
            if (lvl != null) lvl.IsCheckedChanged += (_, _) =>
            {
                if (_paintingPresence) return;
                CoreSettings.Current.DiscordShowLevelInPresence = lvl.IsChecked == true;
                CoreSettings.Save();
                App.DiscordRpc?.UpdateLevel(CoreSettings.Current.PlayerLevel);
            };
            AttachedToVisualTree += (_, _) => { App.RichPresenceChanged -= PaintPresence; App.RichPresenceChanged += PaintPresence; };
            DetachedFromVisualTree += (_, _) => App.RichPresenceChanged -= PaintPresence;
        }

        private void PaintPresence()
        {
            _paintingPresence = true;
            try
            {
                if (this.FindControl<CheckBox>("ChkDiscordTabRichPresence") is { } rp) rp.IsChecked = CoreSettings.Current.DiscordRichPresenceEnabled;
                if (this.FindControl<CheckBox>("ChkDiscordTabShowLevel") is { } lvl) lvl.IsChecked = CoreSettings.Current.DiscordShowLevelInPresence;
            }
            finally { _paintingPresence = false; }
        }

        /// <summary>The panel joins a window only when its dialog opens, after the shell's offline
        /// pass; grey its login button here too (WPF UpdateOfflineModeUI).</summary>
        protected override void OnAttachedToVisualTree(global::Avalonia.VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            RefreshPrivacy();
            PaintPresence();
            Views.Windows.MainShellWindow.SetOfflineDisabled(
                this.FindControl<Button>("BtnDiscordTabLogin"), CoreSettings.Current.OfflineMode);
        }
    }

    /// <summary>
    /// Supplies the strings the view binds to, all from CCP.Core's <see cref="Loc"/> - the same
    /// runtime and the same JSON the WPF head reads. This exists because WPF's {loc:Str key}
    /// markup extension derives from System.Windows.Markup.MarkupExtension and stays in the head.
    ///
    /// Every key below is static in the original markup; none of them is formatted. The three
    /// controls MainWindow.Browser.cs overwrites at runtime (TxtDiscordTabStatus,
    /// TxtDiscordTabInfo, BtnDiscordTabLogin) take their markup default here, exactly as the WPF
    /// panel does before a Discord connection exists - the "connected" strings
    /// (label_connected_as_0, label_discord_account_linked, btn_logout, btn_link_discord_2) are
    /// painted by RefreshPrivacy from the account when the panel opens.
    /// </summary>
    public sealed class ProfilePrivacyPanelViewModel
    {
        public string LocNotConnected => Loc.Get("label_not_connected");
        public string LocLinkDiscordForCommunityFeatures => Loc.Get("label_link_discord_for_community_features");
        public string LocLogin => Loc.Get("btn_login");

        public string LocGroupPresence => Loc.Get("profile_privacy_group_presence");
        public string LocDiscordRichPresence => Loc.Get("label_discord_rich_presence");
        public string LocShowYourActivityStatus => Loc.Get("label_show_your_activity_status");
        public string LocShowLevelInStatus => Loc.Get("label_show_level_in_status");
        public string LocDisplayYourLevel => Loc.Get("label_display_your_level");
        public string LocShowOnlineStatus => Loc.Get("label_show_online_status");
        public string LocAppearOfflineWhenDisabled => Loc.Get("label_appear_offline_when_disabled");

        public string LocCommunitySharing => Loc.Get("label_community_sharing");
        public string LocShareAchievements => Loc.Get("label_share_achievements");
        public string LocPostAchievementsToDiscord => Loc.Get("label_post_achievements_to_discord");
        public string LocShareLevelMilestones => Loc.Get("label_share_level_milestones");
        public string LocPostLevelUpsToDiscord => Loc.Get("label_post_level_ups_to_discord");
        public string LocAllowDmsFromLeaderboard => Loc.Get("label_allow_dms_from_leaderboard");
        public string LocLetOthersMessageYou => Loc.Get("label_let_others_message_you");
        public string LocShareProfilePicture => Loc.Get("label_share_profile_picture");
        public string LocShowYourAvatarToOthers => Loc.Get("label_show_your_avatar_to_others");
        public string LocTooltipPublicShareRealAvatar => Loc.Get("tooltip_public_share_real_avatar");
        public string LocPublicShareRealAvatar => Loc.Get("label_public_share_real_avatar");
        public string LocPublicShareRealAvatarDesc => Loc.Get("label_public_share_real_avatar_desc");

        public string LocGoonGameSharing => Loc.Get("label_goon_game_sharing");
        public string LocTooltipGoonShareAvatar => Loc.Get("tooltip_goon_share_avatar");
        public string LocGoonShareAvatar => Loc.Get("label_goon_share_avatar");
        public string LocTooltipGoonShareDiscordDm => Loc.Get("tooltip_goon_share_discord_dm");
        public string LocGoonShareDiscordDm => Loc.Get("label_goon_share_discord_dm");
        public string LocTooltipGoonRichPresence => Loc.Get("tooltip_goon_rich_presence");
        public string LocGoonRichPresence => Loc.Get("label_goon_rich_presence");
    }
}
