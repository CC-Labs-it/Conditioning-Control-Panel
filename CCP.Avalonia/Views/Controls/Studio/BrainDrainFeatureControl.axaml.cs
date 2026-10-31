using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>Visual settings drive BrainDrainOverlay while the engine runs.
    /// Audio, melt and clip-library actions still await the head's BrainDrainService.</summary>
    public partial class BrainDrainFeatureControl : UserControl
    {
        public BrainDrainFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            RefreshClipCount();

            // Audio library remains a placeholder until BrainDrainService lands.
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();

            // WPF .xaml.cs:221-244,289-304: arm the visual and apply its strength live.
            this.FindControl<CheckBox>("ChkEnable")!.IsChecked = CoreSettings.Current.BrainDrainEnabled;
            this.FindControl<Slider>("SliderBlurStrength")!.Value = CoreSettings.Current.BrainDrainBlurStrength;
            this.FindControl<CheckBox>("ChkEnable")!.IsCheckedChanged += (_, _) =>
            { if (_loading) return; CoreSettings.Current.BrainDrainEnabled = this.FindControl<CheckBox>("ChkEnable")!.IsChecked == true; CoreSettings.Save(); };
            this.FindControl<Slider>("SliderBlurStrength")!.PropertyChanged += (_, e) =>
            {
                if (!_loading && e.Property == Slider.ValueProperty)
                { CoreSettings.Current.BrainDrainBlurStrength = (int)this.FindControl<Slider>("SliderBlurStrength")!.Value; CoreSettings.Save(); }
            };

            _keepClear = this.FindControl<CheckBox>("ChkKeepClear")!;
            LoadKeepClear();
            _keepClear.IsCheckedChanged += (_, _) =>
            {
                var on = _keepClear.IsChecked ?? false;
                if (CoreSettings.Current.BrainDrainKeepPicturesClear == on) return;
                CoreSettings.Current.BrainDrainKeepPicturesClear = on;
                CoreSettings.Save();
            };
        }

        private readonly CheckBox _keepClear;
        private bool _loading;
        private ConditioningControlPanel.Models.AppSettings? _observed;
        private ConditioningControlPanel.Services.SettingsService? _service;

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _service = CoreSettings.Service; if (_service != null) _service.CurrentReplaced += QueueLoad;
            LoadKeepClear();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (_service != null) _service.CurrentReplaced -= QueueLoad;
            if (_observed != null) _observed.PropertyChanged -= SettingsChanged;
            _service = null; _observed = null;
            base.OnDetachedFromVisualTree(e);
        }

        // WPF BrainDrainFeatureControl.xaml.cs:213-235: external writers repaint the live card.
        private void SettingsChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        { if (e.PropertyName is nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainEnabled) or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainBlurStrength) or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainKeepPicturesClear)) QueueLoad(); }
        private void QueueLoad() => global::Avalonia.Threading.Dispatcher.UIThread.Post(() => { if (TopLevel.GetTopLevel(this) != null) LoadKeepClear(); });
        private void LoadKeepClear()
        {
            if (TopLevel.GetTopLevel(this) != null && !ReferenceEquals(_observed, CoreSettings.Current))
            {
                if (_observed != null) _observed.PropertyChanged -= SettingsChanged;
                _observed = CoreSettings.Current; _observed.PropertyChanged += SettingsChanged;
            }
            _loading = true;
            try
            {
                _keepClear.IsChecked = CoreSettings.Current.BrainDrainKeepPicturesClear;
                this.FindControl<CheckBox>("ChkEnable")!.IsChecked = CoreSettings.Current.BrainDrainEnabled;
                this.FindControl<Slider>("SliderBlurStrength")!.Value = CoreSettings.Current.BrainDrainBlurStrength;
            }
            finally { _loading = false; }
        }

        /// <summary>
        /// Repaint the clip-library readout. An empty pool makes the whole audio half a silent
        /// no-op, so the count is shown even when it is fine - "0 clips" is the answer to "why is
        /// nothing happening?". The resolved folder path is the service's to give; until then the
        /// line stays empty rather than printing a folder that may not exist.
        /// </summary>
        private void RefreshClipCount()
        {
            // ponytail: needs BrainDrainService.AudioFileCount, wired when it moves to Core.
            const int clips = 0;
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips == 0;
        }
    }
}
