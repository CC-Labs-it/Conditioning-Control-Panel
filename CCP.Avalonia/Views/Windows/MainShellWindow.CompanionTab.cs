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
            if (CompanionCore.SwitchCompanion(id)) room.RefreshCompanionIdentity();
            // ponytail: AvatarTubeWindow's avatar-set switching is still absent. Keep its
            // existing set until wire-views-tab-companion-b ports WPF SwitchToCompanionAvatar
            // together with the unlock and pinned single-emote-mod guards.
        }

        // ponytail: the runtime aggregate/compat accessors, personality prompt assignment,
        // community browsing/import/export/refresh and prompt labels remain in
        // wire-views-tab-companion-b. The existing Core engine is the only switching rule.
    }
}
