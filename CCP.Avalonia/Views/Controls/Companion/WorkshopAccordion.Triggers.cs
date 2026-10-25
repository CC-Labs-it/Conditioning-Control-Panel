using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion;

public partial class WorkshopAccordion
{
    private async Task EditTriggersAsync()
    {
        if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
        try
        {
            var settings = CoreSettings.Current;
            var before = (settings.CustomTriggers ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t, _ => true);
            var dialog = new TextEditorDialog("Trigger Phrases", before);
            // ponytail: recorded-voice markers need the unported HasRecordedVoice audio lookup.
            TriggerEditors.Add(dialog);
            try
            {
                if (!await dialog.ShowDialogSafe<bool>(owner) || dialog.ResultData == null) return;
                settings = CoreSettings.Current;
                var enabled = dialog.ResultData.Where(pair => pair.Value).Select(pair => pair.Key).ToList();
                var defaults = new HashSet<string>(CoreMods.Service?.GetDefaultCustomTriggers() ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);
                var old = new HashSet<string>(before.Keys, StringComparer.OrdinalIgnoreCase);
                var next = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
                foreach (var text in enabled)
                    if (!old.Contains(text) && !defaults.Contains(text)) settings.UserAddedCustomTriggers.Add(text);
                foreach (var text in old)
                    if (!next.Contains(text)) settings.UserAddedCustomTriggers.Remove(text);
                settings.CustomTriggers = enabled;
                CoreSettings.Save();
            }
            finally { TriggerEditors.Remove(dialog); }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open trigger editor");
            await MessageDialog.ShowAsync(owner, "Error", $"Error opening trigger editor: {ex.Message}");
        }
    }

    private static readonly HashSet<TextEditorDialog> TriggerEditors = new();

    internal static void CloseTriggerEditorForPanic()
    {
        // Panic discards pending edits; normal window-close keeps the editor's unsaved prompt.
        foreach (var dialog in TriggerEditors.ToArray()) dialog.DiscardForPanic();
    }
}
