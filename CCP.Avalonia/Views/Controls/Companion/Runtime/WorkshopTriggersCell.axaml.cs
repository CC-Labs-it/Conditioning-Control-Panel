using System;
using System.Collections.Generic;
using System.Linq;
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
    /// WPF forwards the controls to MainWindow. This head keeps the toggle's local visibility
    /// and interval label here, and hoists phrase-preset selection/save/delete to the Workshop host.
    /// Manage Phrases forwards to the owning shell; the trigger timer awaits its runtime.
    /// </summary>
    public partial class WorkshopTriggersCell : UserControl
    {
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
            this.FindControl<Button>("BtnEditTriggersCompanion")!.Click += async (_, _) => await EditTriggersAsync();
            // WPF forwards the real Phrase Manager action to its owning shell.
            this.FindControl<Button>("BtnManagePhrases")!.Click += (sender, e) =>
                this.FindAncestorOfType<MainShellWindow>()?.BtnManagePhrases_Click(sender, e);

            var toggle = this.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var panel = this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            var slider = this.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            var intervalText = this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;

            // WPF wired Checked/Unchecked separately; Avalonia has one event for both.
            toggle.IsCheckedChanged += (_, _) => panel.IsVisible = toggle.IsChecked == true;
            slider.ValueChanged += (_, _) => intervalText.Text = $"{(int)slider.Value}s";
        }
        internal async System.Threading.Tasks.Task EditTriggersAsync()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner || !owner.IsVisible) return;
            var settings = CoreSettings.Current;
            var original = (settings.CustomTriggers ?? new List<string>())
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase)
                .ToDictionary(t => t, _ => true);
            var dialog = new TextEditorDialog(Loc.Get("title_trigger_phrases"), original);
            dialog.MarkSilentLines(text =>
            {
                try { return Views.Overlays.SubliminalWhisperShow.FindLinkedAudio(text) != null; }
                catch { return false; }
            });
            if (await dialog.ShowDialog<bool?>(owner) != true || dialog.ResultData == null) return;
            var enabled = dialog.ResultData.Where(pair => pair.Value).Select(pair => pair.Key).ToList();
            var defaults = new HashSet<string>(App.Mods?.GetDefaultCustomTriggers() ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            var old = new HashSet<string>(original.Keys, StringComparer.OrdinalIgnoreCase);
            var current = new HashSet<string>(enabled, StringComparer.OrdinalIgnoreCase);
            foreach (var phrase in enabled)
                if (!old.Contains(phrase) && !defaults.Contains(phrase)) settings.UserAddedCustomTriggers.Add(phrase);
            foreach (var phrase in old)
                if (!current.Contains(phrase)) settings.UserAddedCustomTriggers.Remove(phrase);
            settings.CustomTriggers = enabled;
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
