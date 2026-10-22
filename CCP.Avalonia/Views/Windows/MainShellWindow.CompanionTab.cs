using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
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

        /// <summary>Test seam for the picker: receives WPF's start folder, returns the chosen file or null.</summary>
        internal Func<string?, Task<string?>>? PersonalityFileForTests { get; set; }

        /// <summary>WPF MainWindow.CompanionTab.cs:367 BtnCompanionPersonality_Click: pick a prompt JSON,
        /// import it, assign it to this companion, activate it when she is already active, relabel the
        /// cards and confirm. WPF's locked branch (:382) is unreachable: IsCompanionUnlocked is always true.</summary>
        internal async Task AssignPersonalityFromRoster(int index, CompanionRoomView room)
        {
            if (index is < 0 or > 4) return;
            var id = (CompanionId)index;
            var def = CompanionDefinition.GetById(id);

            // WPF :410-422: the user's own prompts folder when it holds JSON, else the shipped presets.
            var userFolder = Path.Combine(CorePaths.EffectiveAssets, "prompts");
            var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
            string? start = Directory.Exists(userFolder) && Directory.EnumerateFiles(userFolder, "*.json").Any() ? userFolder
                : Directory.Exists(shipped) ? shipped : null;

            string? path;
            if (PersonalityFileForTests != null) path = await PersonalityFileForTests(start);
            else
            {
                var folder = start == null ? null : await StorageProvider.TryGetFolderFromPathAsync(start);
                path = (await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
                {
                    Title = $"Select AI Personality for {def.Name}",
                    FileTypeFilter = new[] { new FilePickerFileType("Prompt JSON files") { Patterns = new[] { "*.json" } } },
                    SuggestedStartLocation = folder,
                })).FirstOrDefault()?.TryGetLocalPath();
            }
            if (path == null) return;

            int advisory = 0;
            try
            {
                using var prompts = new CommunityPromptService();
                prompts.ValidationAdvisory += (_, count) => advisory = count;
                var prompt = prompts.ImportFromFile(path);
                if (prompt == null) return;
                CoreSettings.Current.SetCompanionPromptId(index, prompt.Id);
                CoreSettings.Save();
                // WPF :436: SwitchCompanion early-returns on old == new, so activate here.
                if (CompanionCore.ActiveCompanion == id) CompanionCore.ApplyCompanionPrompt(id, prompts);
                room.RefreshRoster();
                Serilog.Log.Information("Assigned prompt '{Prompt}' to companion {Companion}", prompt.Name, def.Name);
                if (advisory > 0)   // WPF App.xaml.cs:2585 ValidationAdvisory warning
                    await MessageDialog.ShowAsync(this, Loc.Get("community_prompt_warning_title"), Loc.GetF("community_prompt_warning_body", advisory));
                await MessageDialog.ShowAsync(this, Loc.Get("title_personality_assigned"),
                    Loc.GetF("msg_personality_assigned", def.Name, prompt.Name));
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "Failed to assign prompt to companion");
                await MessageDialog.ShowAsync(this, Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message));
            }
        }

        // ponytail: the runtime aggregate/compat accessors and the v2 ConversationPage mount remain in
        // wire-views-tab-companion-b-next. The existing Core engine is the only switching rule.
    }
}
