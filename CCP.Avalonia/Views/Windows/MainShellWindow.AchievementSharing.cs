using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Startup;

namespace ConditioningControlPanel.Avalonia.Views.Windows;

public partial class MainShellWindow
{
    /// <summary>WPF MainWindow.Patreon.cs:836. A Discord link is not an opt-in: ask on the
    /// panel, or leave an Inbox row when a game or the hidden panel cannot own the question.</summary>
    internal async Task OfferAchievementSharingAfterDiscordLink(bool userAsked = false)
    {
        try
        {
            var s = CoreSettings.Current;
            var route = AchievementSharePromptDecision.Decide(s.DiscordShareAchievements,
                PanicSurfaces.AnyOwnsTheScreen(), launcherHasTheScreen: false,
                panelOnScreen: IsVisible && WindowState != WindowState.Minimized, userAsked);
            // ponytail: this head has no startup-ladder Held bit. The launcher hides its panel,
            // so panelOnScreen covers that route as well as closing to the tray.
            if (route == AchievementSharePromptRouting.Skip) return;
            if (route == AchievementSharePromptRouting.Inbox)
            {
                // File directly: PresentOrInbox's generic passive rule cannot see a game or a
                // hidden panel, and could otherwise reopen this question immediately.
                StartupLadder.Inbox.File(new InboxItem
                {
                    // ponytail: InboxItem exposes a text glyph only; no new emoji icon.
                    Key = "discord-share-achievements",
                    Title = Loc.Get("inbox_discord_share_title"),
                    Summary = Loc.Get("inbox_discord_share_summary"),
                    Open = () => _ = OfferAchievementSharingAfterDiscordLink(userAsked: true),
                });
                return;
            }
            if (await MessageDialog.ConfirmAsync(this, Loc.Get("title_discord_linked"),
                    Loc.Get("msg_discord_share_achievements_prompt"), okText: "Yes", cancelText: "No"))
            {
                s.DiscordShareAchievements = true;
                CoreSettings.Save();
                Named<Tabs.DiscordTabView>("DiscordTab")?.PrivacyPanel.RefreshAchievementSharing();
            }
        }
        catch (Exception ex) { Serilog.Log.Warning(ex, "Discord achievement-sharing offer failed"); }
    }
}
