using Avalonia.Controls;
using Avalonia.Interactivity;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Pages
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Views/Controls/Companion/Pages/PermissionsPage.xaml.cs
    /// (5681c132a). Companion > Permissions adopts the room's live AiPermissionsGrid ("PermissionsZone").
    /// </summary>
    public partial class PermissionsPage : UserControl
    {
        public PermissionsPage()
        {
            InitializeComponent();
        }

        /// <summary>WPF PermissionsPage.OnShown, through ShowTab("permissions") -> OnTabShown.</summary>
        internal void OnShown(CompanionRoomView? room)
        {
            if (room?.FindControl<AiPermissionsGrid>("PermissionsZone") is { } grid)
            {
                CompanionPageHost.Adopt(grid, PermissionsHost);
                grid.SyncFromSettings();   // WPF tab.Vm.Sync -> SyncLabEffectPermsUI
            }
        }

        private void OpenTakeover_Click(object? sender, RoutedEventArgs e)
            => (TopLevel.GetTopLevel(this) as Windows.MainShellWindow)?.ShowTab("bambitakeover");
    }
}
