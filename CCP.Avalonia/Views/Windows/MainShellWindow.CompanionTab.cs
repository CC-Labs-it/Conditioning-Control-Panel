using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    public partial class MainShellWindow
    {
        /// <summary>WPF MainWindow.CompanionTab.cs CompanionCard_Click: free switching through
        /// the shared engine, then refresh the actual room controls.</summary>
        internal void SwitchCompanionFromRoster(int index, CompanionRoomView room)
        {
            if (index is < 0 or > 4) return;
            var id = (CompanionId)index;
            if (CoreMods.Service?.IsCompanionSupported(id) == false) return;
            if (!CompanionCore.SwitchCompanion(id)) return;
            room.RefreshCompanionIdentity();
            _avatarTubeWindow?.SwitchToCompanionAvatar(id);   // WPF MainWindow.CompanionTab.cs:327-361
        }

        // Community browsing/import/export/refresh and activation use the Core service owned
        // by WorkshopCommunityCell; MakeHerYoursViewModel shares it for browsing and installed
        // prompt names, and both views re-sync after changes.
        // The runtime aggregate/compat accessors and per-companion personality prompt assignment
        // remain in wire-views-tab-companion-b. Core owns the switching rules.
    }
}
