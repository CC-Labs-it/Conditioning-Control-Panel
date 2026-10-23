using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// The Profile tab's "Privacy &amp; Sharing" body, ported from the WPF head.
    ///
    /// Local choices are painted before handlers attach. Avatar/DM/online consent changes use the
    /// existing profile push, as WPF MainWindow.Patreon.cs:910-985; each flag stays independent.
    /// </summary>
    public partial class ProfilePrivacyPanel : UserControl
    {
        public ProfilePrivacyPanel()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new ProfilePrivacyPanelViewModel();
            WirePresence();
            WireAchievementSharing();
            WireCloudPrivacy();
        }

        // WPF ChkShareAchievements_Changed (MainWindow.Patreon.cs:884): this choice is local,
        // unlike the avatar/DM flags below it, which must revoke on the server immediately.
        private void WireAchievementSharing()
        {
            if (this.FindControl<CheckBox>("ChkDiscordTabShareAchievements") is not { } sharing) return;
            sharing.IsChecked = CoreSettings.Current.DiscordShareAchievements;
            sharing.IsCheckedChanged += (_, _) =>
            {
                var on = sharing.IsChecked == true;
                if (CoreSettings.Current.DiscordShareAchievements == on) return;
                CoreSettings.Current.DiscordShareAchievements = on;
                CoreSettings.Save();
            };
            AttachedToVisualTree += (_, _) => sharing.IsChecked = CoreSettings.Current.DiscordShareAchievements;
        }

        private bool _paintingPrivacy;

        private void WireCloudPrivacy()
        {
            var s = CoreSettings.Current;
            var paint = new System.Collections.Generic.List<System.Action>();
            void Bind(string name, System.Func<bool> get, System.Action<bool> set, bool cloud = true)
            {
                if (this.FindControl<CheckBox>(name) is not { } box) return;
                paint.Add(() => box.IsChecked = get());
                box.IsChecked = get();
                box.IsCheckedChanged += async (_, _) =>
                {
                    if (_paintingPrivacy || get() == (box.IsChecked == true)) return;
                    set(box.IsChecked == true);
                    CoreSettings.Save();
                    if (cloud && Platform.AccountSeed.Sync is { } sync)
                    {
                        // WPF's sync cooldown still applies; preserve the change until the next allowed push.
                        if (!await sync.PushAsync("privacy")) sync.Nudge("privacy");
                    }
                };
            }
            Bind("ChkDiscordTabAllowDm", () => s.AllowDiscordDm, v => s.AllowDiscordDm = v);
            Bind("ChkDiscordTabShowOnline", () => s.ShowOnlineStatus, v => s.ShowOnlineStatus = v);
            Bind("ChkDiscordTabSharePfp", () => s.ShareProfilePicture, v => s.ShareProfilePicture = v);
            Bind("ChkPublicShareRealAvatar", () => s.PublicShareRealAvatar, v => s.PublicShareRealAvatar = v);
            Bind("ChkGoonShareAvatar", () => s.GoonShareAvatar, v => s.GoonShareAvatar = v);
            Bind("ChkGoonShareDiscordDm", () => s.GoonShareDiscordDm, v => s.GoonShareDiscordDm = v);
            Bind("ChkGoonRichPresence", () => s.GoonRichPresence, v => s.GoonRichPresence = v, cloud: false);
            Bind("ChkDiscordTabShareLevelUps", () => s.DiscordShareLevelUps, v => s.DiscordShareLevelUps = v, cloud: false);
            AttachedToVisualTree += (_, _) =>
            {
                _paintingPrivacy = true;
                try { foreach (var refresh in paint) refresh(); }
                finally { _paintingPrivacy = false; }
            };
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
    /// set by that host, which is not ported, so they are not modelled here.
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
