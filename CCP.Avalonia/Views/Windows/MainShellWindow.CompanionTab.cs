using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
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
            room.RefreshCompanionIdentity();
            _avatarTubeWindow?.SwitchToCompanionAvatar(id);   // WPF MainWindow.CompanionTab.cs:327-361
        }

        // WPF Services/Companion/CompanionService.cs:212,424. Reuse Core consent and moderation.
        private void ApplyRosterPrompt(int index)
        {
            var id = CoreSettings.Current.GetCompanionPromptId(index);
            var room = Named<Tabs.CompanionTabView>("CompanionTab")?.FindControl<CompanionRoomView>("Room");
            var service = room?.WorkshopParts?.Community.Service;
            if (!string.IsNullOrEmpty(id) && service?.GetInstalledPrompt(id)?.PromptSettings != null)
                service.ActivatePrompt(id);
        }

        private void InitializeCompanionPromptSwitch()
        {
            void OnSwitched(CompanionId id) => Dispatcher.UIThread.Post(() => ApplyRosterPrompt((int)id));
            CompanionCore.Switched += OnSwitched;
            Closed += (_, _) => CompanionCore.Switched -= OnSwitched;
        }

        /// <summary>Test seams: the native picker (null = cancelled) and the message box.</summary>
        internal static Func<MainShellWindow, int, Task<string?>> PickPromptFile = async (shell, index) =>
        {
            var custom = Path.Combine(CorePaths.EffectiveAssets, "prompts");
            var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
            var start = Directory.Exists(custom) && Directory.EnumerateFiles(custom, "*.json").Any() ? custom : shipped;
            var sp = shell.StorageProvider;
            var files = await sp.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = $"Select AI Personality for {CompanionDefinition.GetById(index).Name}",
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Prompt JSON files") { Patterns = new[] { "*.json" } } },
                SuggestedStartLocation = Directory.Exists(start) ? await sp.TryGetFolderFromPathAsync(start) : null
            });
            return files.Count > 0 ? files[0].TryGetLocalPath() : null;
        };

        internal static Func<Window, string, string, Task> InformPrompt = (owner, title, text) => MessageDialog.ShowAsync(owner, title, text);

        // WPF MainWindow.CompanionTab.cs:367-461. Native picker cancellation changes nothing.
        internal async Task AssignCompanionPersonalityAsync(int index, WorkshopRosterCell roster)
        {
            if (index is < 0 or > 4) return;
            try
            {
                if (await PickPromptFile(this, index) is not { } file) return;
                var room = Named<Tabs.CompanionTabView>("CompanionTab")?.FindControl<CompanionRoomView>("Room");
                var prompt = room?.WorkshopParts?.Community.Service.ImportFromFile(file);
                if (prompt == null) return;
                // Retain the old lane's stricter rejection of a JSON null settings object.
                if (prompt.PromptSettings == null)
                {
                    room!.WorkshopParts!.Community.Service.RemovePrompt(prompt.Id);
                    return;
                }
                CoreSettings.Current.SetCompanionPromptId(index, prompt.Id);
                CoreSettings.Save();
                if ((int)CompanionCore.ActiveCompanion == index) ApplyRosterPrompt(index);
                roster.Refresh();
                room?.RefreshCommunity();
                await InformPrompt(this, Loc.Get("title_personality_assigned"),
                    Loc.GetF("msg_personality_assigned", CompanionDefinition.GetById(index).Name, prompt.Name));
            }
            catch (Exception ex)
            {
                await InformPrompt(this, Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message));
            }
        }

        // Remaining aggregate compat and Personality Studio require their shell consumers.
    }
}
