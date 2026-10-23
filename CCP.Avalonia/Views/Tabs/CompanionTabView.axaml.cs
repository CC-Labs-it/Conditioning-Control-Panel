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
    /// WPF's aggregate runtime viewmodel, v2 ConversationPage/PersonalityStudio mount and ~110
    /// compatibility accessors still need a coordinated port with their shell consumers in
    /// wire-views-tab-companion-b. Do not publish null compatibility controls.</para>
    /// </summary>
    public partial class CompanionTabView : UserControl
    {
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
            Room.TabVisibilityChanged += (_, visible) =>
                (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.OnCompanionTabVisibilityChanged(visible, Room);
        }
    }
}
