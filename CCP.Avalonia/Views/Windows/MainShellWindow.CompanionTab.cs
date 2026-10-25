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
            // WPF MainWindow.CompanionTab.cs:327-361: the tube follows with its 200 ms fade out/in.
            if (_avatarTubeWindow is { } tube) _ = SwitchTubeAvatarAsync(tube, id);
        }

        private static async System.Threading.Tasks.Task SwitchTubeAvatarAsync(
            global::ConditioningControlPanel.Avalonia.Views.AvatarTube.AvatarTubeWindow tube, CompanionId id)
        {
            try { await tube.SwitchToCompanionAvatarAsync(id); }
            catch (System.Exception ex) { Serilog.Log.Warning(ex, "Workshop: companion avatar switch failed"); }
        }

        // ponytail: the runtime aggregate/compat accessors remain in wire-views-tab-companion-b.
        // Personality assignment and community prompts live in WorkshopRosterCell /
        // WorkshopCommunityCell over the Core CommunityPromptService.
    }
}
