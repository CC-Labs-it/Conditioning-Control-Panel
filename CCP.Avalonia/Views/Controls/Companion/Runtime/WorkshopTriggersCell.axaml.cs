using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · TRIGGERS &amp; PHRASES. See the XAML header.
    ///
    /// Settings and text editing follow MainWindow.Patreon.cs:1335-1339,1489-1584.
    /// The live avatar trigger timer and phrase count still await their runtime.
    /// </summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        private bool _isLoading;

        public event EventHandler? PhrasePresetSelectionChanged;
        public event EventHandler? SavePhrasePresetRequested;
        public event EventHandler? DeletePhrasePresetRequested;

        public WorkshopTriggersCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopTriggersCellViewModel();
            this.FindControl<ComboBox>("CmbPhrasePresets")!.SelectionChanged += (_, _) => PhrasePresetSelectionChanged?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnSavePhrasePreset")!.Click += (_, _) => SavePhrasePresetRequested?.Invoke(this, EventArgs.Empty);
            this.FindControl<Button>("BtnDeletePhrasePreset")!.Click += (_, _) => DeletePhrasePresetRequested?.Invoke(this, EventArgs.Empty);
            // WPF forwards the real Phrase Manager action to its owning shell.
            this.FindControl<Button>("BtnManagePhrases")!.Click += (sender, e) =>
                this.FindAncestorOfType<MainShellWindow>()?.BtnManagePhrases_Click(sender, e);

            var toggle = this.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var panel = this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            var slider = this.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            var intervalText = this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;

            // WPF wired Checked/Unchecked separately; Avalonia has one event for both.
            toggle.IsCheckedChanged += (_, _) =>
            {
                panel.IsVisible = toggle.IsChecked == true;
                if (_isLoading) return;
                CoreSettings.Current.TriggerModeEnabled = toggle.IsChecked == true;
                CoreSettings.Save();
            };
            slider.ValueChanged += (_, _) =>
            {
                intervalText.Text = $"{(int)slider.Value}s";
                if (_isLoading) return;
                // WPF :1521 updates the model; only the toggle explicitly calls Save (:1498).
                CoreSettings.Current.TriggerIntervalSeconds = (int)slider.Value;
            };
            this.FindControl<Button>("BtnEditTriggersCompanion")!.Click += async (_, _) => await EditTriggersAsync();
            AttachedToVisualTree += (_, _) => SyncFromSettings();
            SyncFromSettings();
        }
        internal void SyncFromSettings()
        {
            _isLoading = true;
            try
            {
                var s = CoreSettings.Current;
                this.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked = s.TriggerModeEnabled;
                this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible = s.TriggerModeEnabled;
                this.FindControl<Slider>("SliderTriggerIntervalCompanion")!.Value = s.TriggerIntervalSeconds;
                this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text = $"{s.TriggerIntervalSeconds}s";
            }
            finally { _isLoading = false; }
        }

        private async Task EditTriggersAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            // WPF MainWindow.Patreon.cs:1534-1543: no defaults, ignore blanks and case duplicates.
            var data = (CoreSettings.Current.CustomTriggers ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t, _ => true);
            var dialog = new TextEditorDialog(Loc.Get("title_trigger_phrases"), data);
            // MarkSilentLines needs the unported recorded-voice lookup; keep editing independent.
            if (await dialog.ShowDialogSafe<bool?>(owner) != true || dialog.ResultData is not { } result) return;
            var enabled = result.Where(kv => kv.Value).Select(kv => kv.Key).ToList();
            var defaults = new HashSet<string>(CoreMods.Service?.GetDefaultCustomTriggers() ?? new List<string>(),
                StringComparer.OrdinalIgnoreCase);
            var old = new HashSet<string>(data.Keys, StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            // WPF :1561-1584: preserve user additions across mod switches and remove deleted ones.
            foreach (var t in enabled)
                if (!old.Contains(t) && !defaults.Contains(t)) CoreSettings.Current.UserAddedCustomTriggers.Add(t);
            foreach (var t in old)
                if (!current.Contains(t)) CoreSettings.Current.UserAddedCustomTriggers.Remove(t);
            CoreSettings.Current.CustomTriggers = enabled;
            CoreSettings.Save();
        }
    }

    /// <summary>
    /// Supplies the strings the view binds to, every one from CCP.Core's <see cref="Loc"/> - the
    /// same runtime and the same JSON the WPF head reads. This exists because WPF's {loc:Str key}
    /// markup extension derives from System.Windows.Markup.MarkupExtension and stays in the head.
    /// </summary>
    public sealed class WorkshopTriggersCellViewModel
    {
        public string LocTriggerMode => Loc.Get("label_trigger_mode");
        public string LocRandomPhrasesHint => Loc.Get("label_random_conditioning_phrases_with_audio");
        public string LocInterval => Loc.Get("label_interval");

        // Both of these are STATIC keys holding the at-rest value, not formatted strings: the WPF
        // head overwrites them with plain interpolation once real data arrives
        // ($"{value}s" at MainWindow.Patreon.cs:1235, $"{count} active" at :1132). No Loc.GetF
        // anywhere on this cell, so there is no format key to look up.
        public string LocIntervalValue => Loc.Get("label_60s");
        public string LocPhraseCount => Loc.Get("label_0_active");

        public string LocEditPhrases => Loc.Get("btn_edit_phrases_2");
        public string LocManagePhrases => Loc.Get("label_manage_phrases");
        public string LocSave => Loc.Get("btn_save");
        public string LocDelete => Loc.Get("btn_delete");
        public string LocTooltipSavePreset => Loc.Get("tooltip_save_current_phrase_config_as_a_preset");
        public string LocTooltipDeletePreset => Loc.Get("tooltip_delete_selected_phrase_preset");
    }
}
