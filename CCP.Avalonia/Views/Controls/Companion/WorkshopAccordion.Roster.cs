using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

public partial class WorkshopAccordion
{
    private static readonly HashSet<Window> RosterMessages = new();

    internal static void CloseRosterMessagesForPanic()
    {
        foreach (var window in RosterMessages.ToArray()) window.Close();
    }

    internal static async Task ShowRosterMessageAsync(Window owner, string title, string message)
    {
        var dialog = new MessageDialog(title, message, showCancel: false);
        RosterMessages.Add(dialog);
        try { await dialog.ShowDialogSafe<bool?>(owner); }
        finally { RosterMessages.Remove(dialog); }
    }

    // Small OS-picker seam: headless tests never invoke a desktop picker.
    internal Func<Window, string, Task<string?>> RosterPromptPicker { get; set; } = PickRosterPromptAsync;

    private static async Task<string?> PickRosterPromptAsync(Window owner, string name)
    {
        var provider = owner.StorageProvider;
        var options = new FilePickerOpenOptions
        {
            Title = $"Select AI Personality for {name}", AllowMultiple = false,
            FileTypeFilter = new[] { new FilePickerFileType("Prompt JSON files") { Patterns = new[] { "*.json" } } }
        };
        var custom = Path.Combine(CorePaths.EffectiveAssets, "prompts");
        var shipped = Path.Combine(AppContext.BaseDirectory, "assets", "prompts");
        var start = Directory.Exists(custom) && Directory.EnumerateFiles(custom, "*.json").Any()
            ? custom : Directory.Exists(shipped) ? shipped : null;
        if (start != null) options.SuggestedStartLocation = await provider.TryGetFolderFromPathAsync(start);
        return (await provider.OpenFilePickerAsync(options)).FirstOrDefault()?.TryGetLocalPath();
    }

    internal async Task AssignRosterPersonalityAsync(WorkshopRosterCell roster, int index)
    {
        if (index is < 0 or > 4 || TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
        // WPF CompanionService.cs:307: IsCompanionUnlocked always returns true.
        // WPF MainWindow.CompanionTab.cs:367-456: picker, assignment and result dialog.
        var name = CompanionDefinition.GetById((CompanionId)index).Name;
        try
        {
            var path = await RosterPromptPicker(owner, name);
            if (path == null || !owner.IsVisible) return;
            var prompt = InstalledCommunityPrompts.ImportAndAssign(path, index);
            if (prompt == null) return; // WPF import failures return null without a success dialog
            roster.Refresh();
            await ShowRosterMessageAsync(owner, Loc.Get("title_personality_assigned"),
                Loc.GetF("msg_personality_assigned", name, prompt.Name));
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Workshop: personality assignment failed");
            await ShowRosterMessageAsync(owner, Loc.Get("title_error"), Loc.GetF("msg_failed_to_import_prompt", ex.Message));
        }
    }
}
