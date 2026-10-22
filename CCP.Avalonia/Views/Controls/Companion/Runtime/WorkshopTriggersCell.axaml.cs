using System;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
using ConditioningControlPanel.Localization;
using CoreAppSettings = ConditioningControlPanel.Models.AppSettings;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>WPF MainWindow.Patreon trigger settings and editor, owned by their Workshop cell.</summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        private readonly CheckBox _toggle;
        private readonly StackPanel _panel;
        private readonly Slider _slider;
        private readonly TextBlock _intervalText;
        private bool _loading, _attached, _writing;
        private CoreAppSettings? _settings;
        private SettingsService? _service;

        public WorkshopTriggersCell()
        {
            AvaloniaXamlLoader.Load(this);
            DataContext = new WorkshopTriggersCellViewModel();
            _toggle = this.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            _panel = this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            _slider = this.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            _intervalText = this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;
            _toggle.IsCheckedChanged += (_, _) =>
            {
                _panel.IsVisible = _toggle.IsChecked == true;
                if (_loading) return;
                Write(() => CoreSettings.Current.TriggerModeEnabled = _toggle.IsChecked == true);
            };
            _slider.ValueChanged += (_, _) =>
            {
                _intervalText.Text = $"{(int)_slider.Value}s";
                if (_loading) return;
                Write(() => CoreSettings.Current.TriggerIntervalSeconds = (int)_slider.Value);
            };
            this.FindControl<Button>("BtnEditTriggersCompanion")!.Click += (_, _) => EditTriggers();
            // ponytail: tube timer, CompanionPhraseService count/management and phrase presets
            // remain wire-views-companion-workshop-triggers-cell-b; no playback is claimed here.
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            Rebind();
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
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
                or nameof(CoreAppSettings.TriggerIntervalSeconds)) QueueRefresh();
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
