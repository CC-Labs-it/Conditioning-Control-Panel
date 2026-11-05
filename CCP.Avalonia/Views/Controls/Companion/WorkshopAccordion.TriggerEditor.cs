using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion
{
    public partial class WorkshopAccordion
    {
        // WPF MainWindow.Patreon.cs:1540.
        internal static Dictionary<string, bool> TriggerEditorData(IEnumerable<string>? triggers) =>
            (triggers ?? Array.Empty<string>()).Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct(StringComparer.OrdinalIgnoreCase).ToDictionary(t => t, _ => true);

        internal static void ApplyTriggerEditorData(ConditioningControlPanel.Models.AppSettings settings, Dictionary<string, bool> oldData,
            Dictionary<string, bool> result, IEnumerable<string> defaults)
        {
            var enabled = result.Where(pair => pair.Value).Select(pair => pair.Key).ToList();
            var modDefaults = new HashSet<string>(defaults, StringComparer.OrdinalIgnoreCase);
            var old = new HashSet<string>(oldData.Keys, StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            foreach (var trigger in enabled)
                if (!old.Contains(trigger) && !modDefaults.Contains(trigger))
                    settings.UserAddedCustomTriggers.Add(trigger);
            foreach (var trigger in old)
                if (!current.Contains(trigger)) settings.UserAddedCustomTriggers.Remove(trigger);
            settings.CustomTriggers = enabled;
        }

        private async Task EditTriggersAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window { IsVisible: true } owner) return;
            var data = TriggerEditorData(CoreSettings.Current.CustomTriggers);
            var dialog = new TextEditorDialog(ConditioningControlPanel.Localization.Loc.Get("title_trigger_phrases"), data);
            dialog.MarkSilentLines(Views.Overlays.SubliminalWhisperShow.HasRecordedVoice);
            if (await dialog.ShowDialogSafe<bool>(owner) && dialog.ResultData != null)
            {
                ApplyTriggerEditorData(CoreSettings.Current, data, dialog.ResultData,
                    App.Mods?.GetDefaultCustomTriggers() ?? new List<string>());
                CoreSettings.Save();
            }
        }
    }
}
