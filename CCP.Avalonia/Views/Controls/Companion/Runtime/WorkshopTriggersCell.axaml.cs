using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using System.ComponentModel;
using TriggerSettings = ConditioningControlPanel.Models.AppSettings;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · TRIGGERS &amp; PHRASES. See the XAML header.
    ///
    /// WPF forwards the controls to MainWindow. This head restores trigger settings here
    /// and hoists phrase-preset selection/save/delete to the Workshop host.
    /// Manage Phrases forwards to the owning shell. Edit Phrases and the live avatar trigger timer
    /// still await their runtime.
    /// </summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        private bool _loading;
        private bool _attached;
        private TriggerSettings? _observed;

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

            toggle.IsCheckedChanged += (_, _) =>
            {
                panel.IsVisible = toggle.IsChecked == true;
                if (_loading) return;
                CoreSettings.Current.TriggerModeEnabled = toggle.IsChecked == true;
                CoreSettings.Save();
            };
            slider.ValueChanged += (_, _) =>
            {
                var value = (int)slider.Value;
                intervalText.Text = $"{value}s";
                if (_loading) return;
                CoreSettings.Current.TriggerIntervalSeconds = value;
                CoreSettings.Save();
            };
            SyncFromSettings();
        }
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            if (CoreSettings.Service is { } service) service.CurrentReplaced += OnCurrentReplaced;
            ObserveSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _attached = false;
            if (CoreSettings.Service is { } service) service.CurrentReplaced -= OnCurrentReplaced;
            if (_observed != null) _observed.PropertyChanged -= OnSettingsChanged;
            _observed = null;
            base.OnDetachedFromVisualTree(e);
        }

        private void OnCurrentReplaced() => Dispatcher.UIThread.Post(() =>
        {
            if (_attached) ObserveSettings();
        });

        private void ObserveSettings()
        {
            if (_observed != null) _observed.PropertyChanged -= OnSettingsChanged;
            _observed = CoreSettings.Current;
            _observed.PropertyChanged += OnSettingsChanged;
            SyncFromSettings();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TriggerSettings.TriggerModeEnabled) ||
                e.PropertyName == nameof(TriggerSettings.TriggerIntervalSeconds))
                Dispatcher.UIThread.Post(SyncFromSettings);
        }

        private void SyncFromSettings()
        {
            _loading = true;
            try
            {
                var settings = CoreSettings.Current;
                this.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked = settings.TriggerModeEnabled;
                this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible = settings.TriggerModeEnabled;
                this.FindControl<Slider>("SliderTriggerIntervalCompanion")!.Value = settings.TriggerIntervalSeconds;
                this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text = $"{settings.TriggerIntervalSeconds}s";
            }
            finally { _loading = false; }
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
