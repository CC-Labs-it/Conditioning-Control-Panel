using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        private MessageDialog? _achievementSharePrompt;

        // WPF MainWindow.Patreon.cs OfferAchievementSharingAfterDiscordLink + AchievementSharePromptRule.
        // ponytail: this head has no launcher hold; its existing quiet route covers sessions and first launch.
        internal async Task OfferAchievementSharingAfterDiscordLink(bool userAsked = false)
        {
            var s = CoreSettings.Current;
            if (s.DiscordShareAchievements || _achievementSharePrompt != null) return;
            if (!userAsked && (PanicSurfaces.AnyOwnsTheScreen() || StartupLadder.IsQuiet
                || !IsVisible || WindowState == WindowState.Minimized))
            {
                StartupLadder.Inbox.File(new InboxItem
                {
                    Key = "discord-share-achievements",
                    Title = InboxFlyout.Str("inbox_discord_share_title", "Discord linked"),
                    Summary = InboxFlyout.Str("inbox_discord_share_summary", "Post your achievements to the community Discord?"),
                    Open = () => _ = OfferAchievementSharingAfterDiscordLink(userAsked: true),
                });
                return;
            }
            var account = s.UnifiedId;
            var dialog = _achievementSharePrompt = new MessageDialog(Loc.Get("title_discord_linked"),
                Loc.Get("msg_discord_share_achievements_prompt"), showCancel: true,
                okText: "Yes", cancelText: "No");
            try
            {
                if (await dialog.ShowDialogSafe<bool?>(this) == true && CoreSettings.Current.UnifiedId == account)
                {
                    CoreSettings.Current.DiscordShareAchievements = true;
                    CoreSettings.Save();
                    ProfilePage?.RefreshPrivacyState();
                    UpdateProfileSharingSummary();
                }
            }
            finally { _achievementSharePrompt = null; }
        }

        internal void StopAchievementSharePrompt() => _achievementSharePrompt?.Close(false);
    }
}
