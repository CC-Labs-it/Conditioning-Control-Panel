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

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += LoadKeepClear;
            LoadKeepClear();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= LoadKeepClear;
            base.OnDetachedFromVisualTree(e);
        }

        private void LoadKeepClear()
        {
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
