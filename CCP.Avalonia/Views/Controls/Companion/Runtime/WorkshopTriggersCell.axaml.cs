using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · TRIGGERS &amp; PHRASES. See the XAML header.
    ///
    /// The WPF code-behind is pure forwarding: every handler walks up to MainWindow and calls the
    /// matching internal method there. That target does not exist on this head - the phrase editor,
    /// the preset store and the avatar trigger timer all hang off <c>App</c>/<c>MainWindow</c> in
    /// the WPF project - so only the two behaviours that are purely local to this cell are wired:
    /// the toggle showing the interval panel (MainWindow.Patreon.cs:1209) and the slider writing
    /// its own value label (MainWindow.Patreon.cs:1235). The phrase-preset combo, Save and Delete
    /// (MainWindow.Assets.cs:1751-1903) and the active-phrase count (MainWindow.Patreon.cs:1327) are
    /// ported here against CCP.Core's PhrasePreset/CoreSettings. Edit Phrases and Manage Phrases
    /// stay inert until that runtime crosses.</summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        public WorkshopTriggersCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopTriggersCellViewModel();

            var toggle = this.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var panel = this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            var slider = this.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            var intervalText = this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;

            // WPF wired Checked/Unchecked separately; Avalonia has one event for both.
            toggle.IsCheckedChanged += (_, _) => panel.IsVisible = toggle.IsChecked == true;
            slider.ValueChanged += (_, _) => intervalText.Text = $"{(int)slider.Value}s";

            _presets = this.FindControl<ComboBox>("CmbPhrasePresets")!;
            _phraseCount = this.FindControl<TextBlock>("TxtPhraseCount")!;
            _presets.SelectionChanged += (_, _) => CmbPhrasePresets_SelectionChanged();
            this.FindControl<Button>("BtnSavePhrasePreset")!.Click += async (_, _) => await SavePhrasePresetAsync();
            this.FindControl<Button>("BtnDeletePhrasePreset")!.Click += async (_, _) => await DeletePhrasePresetAsync();
            AskText = async (title, prompt, def) =>
            {
                var dlg = new InputDialog(title, prompt, def);
                return await dlg.ShowDialogSafe<bool?>(TopLevel.GetTopLevel(this) as Window) == true ? dlg.ResultText : null;
            };
            Confirm = (title, msg) => MessageDialog.ConfirmAsync((TopLevel.GetTopLevel(this) as Window)!, title, msg);
            Inform = (title, msg) => MessageDialog.ShowAsync((TopLevel.GetTopLevel(this) as Window)!, title, msg);
            // WPF InitializePhrasePresets runs each time the Companion tab is shown (TabNavigation.cs:505)
            // and the count at startup (MainWindow.xaml.cs:394); this cell lives on that tab.
            AttachedToVisualTree += (_, _) => { RefreshPhrasePresetsComboBox(); UpdatePhraseCountDisplay(); };
        }

        private readonly ComboBox _presets;
        private readonly TextBlock _phraseCount;
        private bool _isLoadingPhrasePreset;

        // Dialog seams (same shape as AssetsTabView's), replaced by tests.
        internal Func<string, string, string, Task<string?>> AskText;
        internal Func<string, string, Task<bool>> Confirm;
        internal Func<string, string, Task> Inform;

        // WPF App.CompanionPhrases.GetActivePhraseCount(); the editor's LoadPhrases is this head's
        // GetAllPhrases (voice-line rows still missing, see that method).
        private static int ActivePhraseCount() => CompanionPhraseEditorDialog.LoadPhrases().Count(p => p.IsEnabled);

        /// <summary>WPF UpdatePhraseCountDisplay (MainWindow.Patreon.cs:1327).</summary>
        internal void UpdatePhraseCountDisplay() => _phraseCount.Text = Loc.GetF("label_0_active_2", ActivePhraseCount());

        /// <summary>WPF RefreshPhrasePresetsComboBox (MainWindow.Assets.cs:1760).</summary>
        internal void RefreshPhrasePresetsComboBox()
        {
            _isLoadingPhrasePreset = true;
            var s = CoreSettings.Current;
            _presets.ItemsSource = null;
            _presets.ItemsSource = s.PhrasePresets;
            if (!string.IsNullOrEmpty(s.CurrentPhrasePresetId))
                _presets.SelectedItem = s.PhrasePresets.FirstOrDefault(p => p.Id == s.CurrentPhrasePresetId);
            _isLoadingPhrasePreset = false;
        }

        /// <summary>WPF CmbPhrasePresets_SelectionChanged (MainWindow.Assets.cs:1773).</summary>
        private void CmbPhrasePresets_SelectionChanged()
        {
            if (_isLoadingPhrasePreset || _presets.SelectedItem is not PhrasePreset preset) return;
            preset.ApplyToSettings();
            CoreSettings.Current.CurrentPhrasePresetId = preset.Id;
            CoreSettings.Save();
            UpdatePhraseCountDisplay();
            Log.Information("Loaded phrase preset: {Name} ({Count} phrases)", preset.Name, preset.ActivePhraseCount);
        }

        /// <summary>WPF BtnSavePhrasePreset_Click (MainWindow.Assets.cs:1788).</summary>
        internal async Task SavePhrasePresetAsync()
        {
            var activePhraseCount = ActivePhraseCount();
            var s = CoreSettings.Current;
            // "Preset {0}": WPF's literal default name; the asset preset key carries the same text.
            var name = await AskText(Loc.Get("title_save_phrase_preset"), Loc.Get("label_enter_a_name_for_this_phrase_preset"),
                Loc.GetF("label_asset_preset_default_0", s.PhrasePresets.Count + 1));
            if (string.IsNullOrWhiteSpace(name)) return;
            var preset = PhrasePreset.FromCurrentSettings(name.Trim(), activePhraseCount);
            s.PhrasePresets.Add(preset);
            s.CurrentPhrasePresetId = preset.Id;
            CoreSettings.Save();
            RefreshPhrasePresetsComboBox();
            Log.Information("Saved phrase preset: {Name} with {Count} active phrases", preset.Name, activePhraseCount);
        }

        /// <summary>WPF BtnDeletePhrasePreset_Click (MainWindow.Assets.cs:1878).</summary>
        internal async Task DeletePhrasePresetAsync()
        {
            if (_presets.SelectedItem is not PhrasePreset preset)
            {
                await Inform(Loc.Get("title_no_preset_selected"), Loc.Get("msg_please_select_a_preset_to_delete"));
                return;
            }
            if (!await Confirm(Loc.Get("title_delete_preset"), Loc.GetF("msg_delete_phrase_preset_confirm_0", preset.Name))) return;
            CoreSettings.Current.PhrasePresets.Remove(preset);
            CoreSettings.Current.CurrentPhrasePresetId = null;
            CoreSettings.Save();
            RefreshPhrasePresetsComboBox();
            Log.Information("Deleted phrase preset: {Name}", preset.Name);
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
