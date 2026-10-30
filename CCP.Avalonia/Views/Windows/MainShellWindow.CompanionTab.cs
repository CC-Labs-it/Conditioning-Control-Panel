using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;
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
            if (!CompanionCore.SwitchCompanion(id, room.WorkshopParts?.Community.Service)) return;
            room.RefreshCompanionIdentity();
            room.RefreshCommunity(); // WPF MainWindow.CompanionTab.cs:147, after card refresh
            _avatarTubeWindow?.SwitchToCompanionAvatar(id);   // WPF MainWindow.CompanionTab.cs:327-361
        }

        internal Func<Task<string?>>? PersonalityFileForTests { get; set; }

        // WPF MainWindow.CompanionTab.cs:368-446: import JSON, assign, apply if active, refresh labels.
        internal async Task AssignCompanionPersonalityAsync(int index, CompanionRoomView room)
        {
            if (index is < 0 or > 4) return;
            try
            {
                string? path;
                if (PersonalityFileForTests != null) path = await PersonalityFileForTests();
                else
                {
                    var own = Path.Combine(CorePaths.EffectiveAssets, "prompts");
                    var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
                    var initial = Directory.Exists(own) && Directory.EnumerateFiles(own, "*.json").Any() ? own : shipped;
                    var folder = Directory.Exists(initial) ? await StorageProvider.TryGetFolderFromPathAsync(initial) : null;
                    path = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                    {
                        Title = Loc.Get("title_import_community_prompt"), AllowMultiple = false,
                        SuggestedStartLocation = folder,
                        FileTypeFilter = new[] { new FilePickerFileType("JSON") { Patterns = new[] { "*.json" } } }
                    })).FirstOrDefault()?.TryGetLocalPath();
                }
                if (path == null) return;
                var prompt = room.WorkshopParts!.Community.Service.ImportFromFile(path);
                if (prompt == null) return;
                AssignImportedPersonality(index, prompt.Id, room);
                await Controls.Companion.Runtime.WorkshopCommunityCell.ShowPromptDialogAsync(this, new MessageDialog(Loc.Get("title_personality_assigned"),
                    Loc.GetF("msg_personality_assigned", CompanionDefinition.GetById(index).Name, prompt.Name), false));
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to assign companion prompt");
                await Controls.Companion.Runtime.WorkshopCommunityCell.ShowPromptDialogAsync(this, new MessageDialog(Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message), false));
            }
        }

        internal void AssignImportedPersonality(int index, string promptId, CompanionRoomView room)
        {
            if (index is < 0 or > 4) return;
            CoreSettings.Current.SetCompanionPromptId(index, promptId);
            CoreSettings.Save();
            if ((int)CompanionCore.ActiveCompanion == index) CompanionCore.ApplyCompanionPrompt((CompanionId)index, room.WorkshopParts?.Community.Service);
            room.RefreshRoster();
            room.RefreshCommunity();
        }
    }
}
