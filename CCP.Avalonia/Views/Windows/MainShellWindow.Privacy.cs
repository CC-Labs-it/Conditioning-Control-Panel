using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Privacy;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    private static MessageDialog? _achievementSharingPrompt;

    internal async Task<bool> AskAchievementSharing()
    {
        if (_achievementSharingPrompt != null) return false;
        var dialog = new MessageDialog(Loc.Get("title_discord_linked"),
            Loc.Get("msg_discord_share_achievements_prompt"), showCancel: true,
            okText: Loc.Get("btn_yes"), cancelText: Loc.Get("btn_no"));
        _achievementSharingPrompt = dialog;
        try { return await dialog.ShowDialogSafe<bool?>(this) == true; }
        finally { if (_achievementSharingPrompt == dialog) _achievementSharingPrompt = null; }
    }

    internal static void CloseAchievementSharingForPanic() => _achievementSharingPrompt?.Close(false);

    internal Task OfferAchievementSharingAfterDiscordLinkAsync(bool userAsked = false) =>
        OfferAchievementSharingAsync(AskAchievementSharing, userAsked);

    internal async Task OfferAchievementSharingAsync(Func<Task<bool>> ask, bool userAsked = false)
    {
        var route = AchievementSharePromptRule.Decide(CoreSettings.Current.DiscordShareAchievements,
            PanicSurfaces.AnyOwnsTheScreen(), IsStartupDialogShowing,
            IsVisible && WindowState != WindowState.Minimized, userAsked);
        if (route == AchievementSharePromptRouting.Skip) return;
        if (route == AchievementSharePromptRouting.Inbox)
        {
            StartupLadder.Inbox.File(new InboxItem
            {
                Key = "discord-share-achievements",
                Title = InboxFlyout.Str("inbox_discord_share_title", "Discord linked"),
                Summary = InboxFlyout.Str("inbox_discord_share_summary", "Post your achievements to the community Discord?"),
                Open = () => _ = OfferAchievementSharingAsync(ask, userAsked: true),
            });
            return;
        }
        if (!await ask()) return;
        CoreSettings.Current.DiscordShareAchievements = true;
        CoreSettings.Save();
        Named<Tabs.DiscordTabView>("DiscordTab")?.PrivacyPanel.RefreshPrivacy();
    }
}
