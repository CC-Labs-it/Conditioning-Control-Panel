using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
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
            ApplyRosterPrompt(index);
            room.RefreshCompanionIdentity();
            _avatarTubeWindow?.SwitchToCompanionAvatar(id);   // WPF MainWindow.CompanionTab.cs:327-361
        }

        private static string PromptFolder => Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);

        private void ApplyRosterPrompt(int index)
        {
            if (InstalledCommunityPrompts.ApplyAssigned(PromptFolder, CoreSettings.Current, index,
                async count => await MessageDialog.ShowAsync(this, Loc.Get("community_prompt_warning_title"),
                    Loc.GetF("community_prompt_warning_body", count)))) CoreSettings.Save();
        }

        // WPF MainWindow.CompanionTab.cs:367-461. Native picker cancellation changes nothing.
        internal async Task AssignCompanionPersonalityAsync(int index, WorkshopRosterCell roster)
        {
            if (index is < 0 or > 4) return;
            try
            {
                var custom = Path.Combine(CorePaths.EffectiveAssets, "prompts");
                var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
                var start = Directory.Exists(custom) && Directory.EnumerateFiles(custom, "*.json").Any() ? custom : shipped;
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Select AI Personality for {CompanionDefinition.GetById(index).Name}",
                    AllowMultiple = false,
                    FileTypeFilter = new[] { new FilePickerFileType("Prompt JSON files") { Patterns = new[] { "*.json" } } },
                    SuggestedStartLocation = Directory.Exists(start) ? await StorageProvider.TryGetFolderFromPathAsync(start) : null
                });
                if (files.Count == 0 || files[0].TryGetLocalPath() is not { } file) return;
                var prompt = InstalledCommunityPrompts.Import(PromptFolder, file, CoreSettings.Current);
                if (prompt == null) return;
                CoreSettings.Current.SetCompanionPromptId(index, prompt.Id);
                CoreSettings.Save();
                if ((int)CompanionCore.ActiveCompanion == index) ApplyRosterPrompt(index);
                roster.Refresh();
                await MessageDialog.ShowAsync(this, Loc.Get("title_personality_assigned"),
                    Loc.GetF("msg_personality_assigned", CompanionDefinition.GetById(index).Name, prompt.Name));
            }
            catch (Exception ex)
            {
                await MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message));
            }
        }

        // ponytail: aggregate compat, Personality Studio and community catalogue actions remain unported.
    }
}
