using System;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Companion.Runtime
{
    /// <summary>Z8 · TRIGGERS &amp; PHRASES. See the XAML header.
    ///
    /// WPF forwards the controls to MainWindow. This head restores settings locally and
    /// hoists editing and phrase-preset selection/save/delete to the Workshop host.
    /// Manage Phrases forwards to the owning shell. The live avatar trigger timer still awaits its runtime.
    /// </summary>
    public partial class WorkshopTriggersCell : UserControl
    {
        private bool _syncing;
        public event EventHandler? EditTriggersRequested;
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

            this.FindControl<Button>("BtnEditTriggersCompanion")!.Click += (_, _) => EditTriggersRequested?.Invoke(this, EventArgs.Empty);

            var toggle = this.FindControl<CheckBox>("ChkTriggerModeCompanion")!;
            var panel = this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!;
            var slider = this.FindControl<Slider>("SliderTriggerIntervalCompanion")!;
            var intervalText = this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!;

            // WPF MainWindow/MainWindow.Patreon.cs:1489-1525: toggle/save and interval without Save.
            toggle.IsCheckedChanged += (_, _) =>
            {
                panel.IsVisible = toggle.IsChecked == true;
                if (_syncing) return;
                CoreSettings.Current.TriggerModeEnabled = toggle.IsChecked == true;
                CoreSettings.Save();
                // ponytail: RestartTriggerTimer needs the unported tube trigger/audio loop.
            };
            slider.ValueChanged += (_, _) =>
            {
                if (_syncing) return;
                var value = (int)slider.Value;
                intervalText.Text = $"{value}s";
                // WPF updates this setting without a Save on every slider tick.
                CoreSettings.Current.TriggerIntervalSeconds = value;
            };
            SyncFromSettings();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } service) service.CurrentReplaced += CurrentReplaced;
            SyncFromSettings();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } service) service.CurrentReplaced -= CurrentReplaced;
            base.OnDetachedFromVisualTree(e);
        }

        private void CurrentReplaced() => Dispatcher.UIThread.Post(SyncFromSettings);

        // WPF MainWindow/MainWindow.Patreon.cs:1336-1338 restores without writing.
        private void SyncFromSettings()
        {
            _syncing = true;
            try
            {
                var s = CoreSettings.Current;
                this.FindControl<CheckBox>("ChkTriggerModeCompanion")!.IsChecked = s.TriggerModeEnabled;
                this.FindControl<StackPanel>("TriggerSettingsPanelCompanion")!.IsVisible = s.TriggerModeEnabled;
                this.FindControl<Slider>("SliderTriggerIntervalCompanion")!.Value = s.TriggerIntervalSeconds;
                this.FindControl<TextBlock>("TxtTriggerIntervalCompanion")!.Text = $"{s.TriggerIntervalSeconds}s";
            }
            finally { _syncing = false; }
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
