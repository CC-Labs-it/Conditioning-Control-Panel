// WPF MainWindow.Exclusives.cs: shell routing; shared view owns roster and chrome.
using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        // No member of this partial is referenced from MainShellWindow.axaml.

        /// <summary>WPF RefreshExclusivesTab, for the entitlement events (tier change, day rollover).
        /// The body lives in ExclusivesTabView.RefreshVault.</summary>
        internal void RefreshExclusivesTab()
        {
            Named<Tabs.ExclusivesTabView>("ExclusivesTab")?.RefreshVault();
            PlansVault?.RefreshVault();
            RefreshInvites();
        }

        /// <summary>WPF OpenExclusiveFeature (main 2e9080399): each card opens the door the launcher or the Play
        /// wall uses, so the card never decides access; the door's own gate refuses. Doors with no host on this
        /// head never get here (ExclusivesTabView.IsOnThisBuild).</summary>
        internal void OpenExclusiveFeature(string key)
        {
            if (LockdownActive) return;   // PLAYBOOK P05, like OpenInvitesCard
            switch (key)
            {
                case "gazeminigame": Named<Tabs.PlayTabView>("PlayTab")?.OpenGazeMinigame(); break;
                default: ShowTab(key); break;
            }
        }

        /// <summary>WPF _invitePanel.RefreshAsync(): throttled inside, so a repaint storm costs one read.</summary>
        internal Tabs.ExclusivesTabView? PlansVault => AppSettingsPage?.FindControl<Controls.AppSettings.AccountSettingsSection>("SectionAccount")?.PlansView;

        internal void RefreshInvites() => _ = PlansVault?.InvitesPanel?.RefreshAsync();
    }
}
