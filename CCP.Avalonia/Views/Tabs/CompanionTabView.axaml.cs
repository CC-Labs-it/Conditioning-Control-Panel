using Avalonia.Controls;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Tabs/CompanionTabView.xaml.cs. The tab is a host
    /// for one control — <see cref="Controls.Companion.CompanionRoomView"/> — plus the hidden
    /// HelpBtnAiChat compat element. That much crosses verbatim.
    ///
    /// <para>The room owns live zone viewmodels and Workshop cells. Roster card clicks forward
    /// to the shell's Core switch; visibility forwards the room's effective edge. ponytail:
    /// the room owns zone state directly instead of publishing WPF compatibility accessors.
    /// Personality assignment and community actions forward to the shell and shared Core service.</para>
    /// </summary>
    public partial class CompanionTabView : UserControl
    {
        private bool _communityWired;

        public CompanionTabView()
        {
            InitializeComponent();

            // ponytail: WPF's aggregate runtime viewmodel/compat surface remains in
            // wire-views-tab-companion-b; the room already owns live per-zone viewmodels.

            // WPF WorkshopRosterCell forwards its card action through the tab to MainWindow.
            if (Room.WorkshopParts is { } shelf)
                shelf.Roster.CompanionCardClicked += (_, index) =>
                    (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.SwitchCompanionFromRoster(index, Room);

            // WPF hooks IsVisibleChanged here to call MainWindow.OnCompanionTabVisibilityChanged.
            // Avalonia's IsVisible is local, so the room's effective-visibility watch raises the edge.
            AttachedToVisualTree += (_, _) =>
            {
                if (!_communityWired && TopLevel.GetTopLevel(this) is Windows.MainShellWindow shell)
                { shell.WireCommunityPrompts(Room); _communityWired = true; }
            };
            Room.TabVisibilityChanged += (_, visible) =>
            {
                if (TopLevel.GetTopLevel(this) is not Windows.MainShellWindow shell) return;
                shell.OnCompanionTabVisibilityChanged(visible, Room);
                if (visible)
                {
                    Room.WorkshopParts?.Behavior.SyncFromSettings();
                    Room.WorkshopParts?.Triggers.SyncFromSettings();
                    shell.RefreshCommunityPrompts(Room);
                }
            };
        }
    }
}
