using System;
using System.ComponentModel;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using CoreAppSettings = ConditioningControlPanel.Models.AppSettings;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>WPF MainWindow.Patreon trigger settings, editor and phrase count, and MainWindow.Assets
    /// phrase presets (:1815-1967), owned by their Workshop cell.</summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        private readonly CheckBox _toggle;
        private readonly StackPanel _panel;
        private readonly Slider _slider;
        private readonly TextBlock _intervalText;
        private readonly TextBlock _phraseCount;
        private readonly ComboBox _presets;
        private bool _loading, _attached, _writing;
        private CoreAppSettings? _settings;
        private SettingsService? _service;

        /// <summary>Raised after the Phrase Manager closes, wherever it was opened from (WPF
        /// UpdatePhraseCountDisplay after BtnManagePhrases_Click).</summary>
        internal static event Action? PhrasesChanged;

        /// <summary>Dialog seams (tests answer them; P08): name prompt, yes/no, notice.</summary>
        internal Func<Window, string, string, string, Task<string?>> AskText = async (owner, title, prompt, def) =>
        {
            var dlg = new InputDialog(title, prompt, def);
            return await dlg.ShowDialogSafe<bool?>(owner) == true ? dlg.ResultText : null;
        };
        internal Func<Window, string, string, Task<bool>> Confirm = (owner, title, msg) =>
            MessageDialog.ConfirmAsync(owner, title, msg, okText: "Yes", cancelText: "No");
        internal Func<Window, string, string, Task> Inform = (owner, title, msg) => MessageDialog.ShowAsync(owner, title, msg);

        public WorkshopTriggersCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopTriggersCellViewModel();
            _toggle = this.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            _panel = this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            _slider = this.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            _intervalText = this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;
            _phraseCount = this.FindControl<TextBlock>("TxtPhraseCount")!;
            _presets = this.FindControl<ComboBox>("CmbPhrasePresets")!;
            _toggle.IsCheckedChanged += (_, _) =>
            {
                _panel.IsVisible = _toggle.IsChecked == true;
                if (_loading) return;
                var on = _toggle.IsChecked == true;
                Write(() => CoreSettings.Current.TriggerModeEnabled = on);
                AvatarTubeWindow.Live?.RestartTriggerTimer();
                Log.Information("Trigger Mode {State}", on ? "enabled" : "disabled");
            };
            _slider.ValueChanged += (_, _) =>
            {
                _intervalText.Text = $"{(int)_slider.Value}s";
                if (_loading) return;
                Write(() => CoreSettings.Current.TriggerIntervalSeconds = (int)_slider.Value);
                AvatarTubeWindow.Live?.RestartTriggerTimer();
            };
            this.FindControl<Button>("BtnEditTriggersCompanion")!.Click += (_, _) => EditTriggers();
            this.FindControl<Button>("BtnManagePhrases")!.Click += async (_, _) =>
            {
                if (TopLevel.GetTopLevel(this) is Window owner) await ManagePhrasesAsync(owner);
            };
            _presets.SelectionChanged += (_, _) => OnPresetSelected();
            this.FindControl<Button>("BtnSavePhrasePreset")!.Click += (_, _) => SavePreset();
            this.FindControl<Button>("BtnDeletePhrasePreset")!.Click += (_, _) => DeletePreset();
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            PhrasesChanged += UpdatePhraseCount;
            Rebind();
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            PhrasesChanged -= UpdatePhraseCount;
            Disconnect();
            base.OnDetachedFromVisualTree(e);
        }

        private void Disconnect()
        {
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            if (_service != null) _service.CurrentReplaced -= QueueRefresh;
            _settings = null;
            _service = null;
        }

        private void Rebind()
        {
            Disconnect();
            _settings = CoreSettings.Current;
            _service = CoreSettings.Service;
            _settings.PropertyChanged += OnSettingsChanged;
            if (_service != null) _service.CurrentReplaced += QueueRefresh;
        }

        // The cell's own writes must not echo back as a refresh: that would snap a dragging slider to an int.
        private void Write(Action write)
        {
            _writing = true;
            try { write(); CoreSettings.Save(); }
            finally { _writing = false; }
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_loading && !_writing && e.PropertyName is null or "" or nameof(CoreAppSettings.TriggerModeEnabled)
                or nameof(CoreAppSettings.TriggerIntervalSeconds) or nameof(CoreAppSettings.PhrasePresets)
                or nameof(CoreAppSettings.CurrentPhrasePresetId)) QueueRefresh();
        }

        private void QueueRefresh() => Dispatcher.UIThread.Post(() =>
        {
            if (!_attached) return;
            if (!ReferenceEquals(_settings, CoreSettings.Current)) Rebind();
            SyncFromSettings();
        });

        private void SyncFromSettings()
        {
            _loading = true;
            try
            {
                var s = CoreSettings.Current;
                _toggle.IsChecked = s.TriggerModeEnabled;
                _panel.IsVisible = s.TriggerModeEnabled;
                _slider.Value = s.TriggerIntervalSeconds;
                _intervalText.Text = $"{s.TriggerIntervalSeconds}s";
            }
            finally { _loading = false; }
            SyncPresets();
            UpdatePhraseCount();
        }

        /// <summary>WPF RefreshPhrasePresetsComboBox (MainWindow.Assets.cs:1824).</summary>
        private void SyncPresets()
        {
            _loading = true;
            try
            {
                var s = CoreSettings.Current;
                _presets.ItemsSource = null;
                _presets.ItemsSource = s.PhrasePresets;
                if (!string.IsNullOrEmpty(s.CurrentPhrasePresetId)) _presets.SelectedValue = s.CurrentPhrasePresetId;
            }
            finally { _loading = false; }
        }

        /// <summary>WPF UpdatePhraseCountDisplay (MainWindow.Patreon.cs:1327), English as WPF.</summary>
        private void UpdatePhraseCount() => _phraseCount.Text = $"{CompanionPhraseEditorDialog.ActivePhraseCount()} active";

        /// <summary>WPF BtnManagePhrases_Click (MainWindow.Patreon.cs:1320); the shell's entry calls this too.</summary>
        internal static async Task ManagePhrasesAsync(Window owner)
        {
            await new CompanionPhraseEditorDialog().ShowDialogSafe(owner);
            PhrasesChanged?.Invoke();
        }

        /// <summary>WPF CmbPhrasePresets_SelectionChanged (MainWindow.Assets.cs:1837).</summary>
        private void OnPresetSelected()
        {
            if (_loading || _presets.SelectedItem is not PhrasePreset preset) return;
            Write(() =>
            {
                preset.ApplyToSettings();
                CoreSettings.Current.CurrentPhrasePresetId = preset.Id;
            });
            UpdatePhraseCount();
            Log.Information("Loaded phrase preset: {Name} ({Count} phrases)", preset.Name, preset.ActivePhraseCount);
        }

        /// <summary>WPF BtnSavePhrasePreset_Click (MainWindow.Assets.cs:1852).</summary>
        private async void SavePreset()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            var settings = CoreSettings.Current;
            var active = CompanionPhraseEditorDialog.ActivePhraseCount();
            var name = await AskText(owner, Loc.Get("title_save_phrase_preset"), "Enter a name for this phrase preset:",
                $"Preset {settings.PhrasePresets.Count + 1}");
            if (string.IsNullOrWhiteSpace(name)) return;
            if (!ReferenceEquals(settings, CoreSettings.Current)) { Log.Warning("Phrase preset save dropped: settings were restored"); return; }
            var preset = PhrasePreset.FromCurrentSettings(name.Trim(), active);
            Write(() =>
            {
                settings.PhrasePresets.Add(preset);
                settings.CurrentPhrasePresetId = preset.Id;
            });
            SyncPresets();
            Log.Information("Saved phrase preset: {Name} with {Count} active phrases", preset.Name, active);
        }

        /// <summary>WPF BtnDeletePhrasePreset_Click (MainWindow.Assets.cs:1942).</summary>
        private async void DeletePreset()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            if (_presets.SelectedItem is not PhrasePreset preset)
            {
                await Inform(owner, "No Preset Selected", Loc.Get("msg_please_select_a_preset_to_delete"));
                return;
            }
            var settings = CoreSettings.Current;
            if (!await Confirm(owner, "Delete Preset", $"Delete phrase preset '{preset.Name}'?\n\nThis cannot be undone.")) return;
            if (!ReferenceEquals(settings, CoreSettings.Current)) { Log.Warning("Phrase preset delete dropped: settings were restored"); return; }
            Write(() =>
            {
                settings.PhrasePresets.Remove(preset);
                settings.CurrentPhrasePresetId = null;
            });
            SyncPresets();
            Log.Information("Deleted phrase preset: {Name}", preset.Name);
        }

        private async void EditTriggers()
        {
            if (TopLevel.GetTopLevel(this) is not Window owner) return;
            try
            {
                var settings = CoreSettings.Current;
                var mod = CoreMods.ActiveModToken;
                var original = TriggerPhraseEdits.ForEditor(settings.CustomTriggers);
                var dialog = new TextEditorDialog("Trigger Phrases", original);
                if (await dialog.ShowDialogSafe<bool?>(owner) != true || dialog.ResultData is not { } edited) return;
                // A cloud restore or mod switch during the modal editor must not overwrite its new pool.
                if (!ReferenceEquals(settings, CoreSettings.Current) || !ReferenceEquals(mod, CoreMods.ActiveModToken))
                {
                    Log.Warning("Trigger phrase edit dropped: settings were restored or the mod changed while the editor was open");
                    return;
                }
                TriggerPhraseEdits.Apply(settings, original, edited, CoreMods.Service?.GetDefaultCustomTriggers());
                CoreSettings.Save();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to open trigger editor");
                await MessageDialog.ShowAsync(owner, "Error", $"Error opening trigger editor: {ex.Message}");
            }
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
