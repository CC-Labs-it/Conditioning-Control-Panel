using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    // Audio host is real; blur/melt/capture remain a separate visual lane.
    public partial class BrainDrainFeatureControl : UserControl
    {
        public BrainDrainFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            RefreshClipCount();

            WireAudio();
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) =>
            {
                CoreBrainDrain.ReloadClips();
                RefreshClipCount();
            };

            // Keep pictures clear is a pure preference (WPF ChkKeepClear_Changed writes and saves;
            // OverlayService re-sorts on the settings hook), so unlike the rows above it shows no
            // false "running" state and is wired now. This head has no Brain Drain blur overlay
            // yet, so the z-order rule (Core BrainDrainKeepClear) has nothing to act on here.
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

        private bool _loadingAudio;
        private void ReloadSettings() { LoadKeepClear(); LoadAudio(); }

        private void LoadAudio()
        {
            _loadingAudio = true;
            var s = CoreSettings.Current;
            this.FindControl<CheckBox>("ChkEnable")!.IsChecked = s.BrainDrainEnabled;
            this.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked = s.BrainDrainHighRefresh;
            this.FindControl<Slider>("SliderIntensity")!.Value = s.BrainDrainIntensity;
            _loadingAudio = false;
        }

        // ponytail: blur, melt and capture controls await the visual host; only audio is wired here.
        private void WireAudio()
        {
            LoadAudio();
            this.FindControl<CheckBox>("ChkEnable")!.IsCheckedChanged += (_, _) =>
            {
                if (_loadingAudio) return;
                CoreSettings.Current.BrainDrainEnabled = this.FindControl<CheckBox>("ChkEnable")!.IsChecked == true;
                if (!CoreSettings.Current.BrainDrainEnabled) CoreBrainDrain.Stop();
                else if (CoreEngine.IsRunning) CoreBrainDrain.Start();
                CoreSettings.Save();
            };
            this.FindControl<CheckBox>("ChkHighRefresh")!.IsCheckedChanged += (_, _) =>
            {
                if (_loadingAudio) return;
                CoreSettings.Current.BrainDrainHighRefresh = this.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked == true;
                if (CoreBrainDrain.IsRunning) { CoreBrainDrain.Stop(); CoreBrainDrain.Start(); }
                CoreSettings.Save();
            };
            this.FindControl<Slider>("SliderIntensity")!.ValueChanged += (_, e) =>
            {
                if (_loadingAudio) return;
                CoreSettings.Current.BrainDrainIntensity = (int)e.NewValue;
                CoreBrainDrain.SetIntensity(e.NewValue);
                CoreSettings.Save();
            };
            foreach (var name in new[] { "BtnOpenAudioFolder", "BtnOpenAudioFolderEmpty" })
                this.FindControl<Button>(name)!.Click += (_, _) =>
                {
                    try
                    {
                        System.IO.Directory.CreateDirectory(BrainDrainSchedule.AudioFolderPath);
                        Platform.ExternalOpener.Open(BrainDrainSchedule.AudioFolderPath);
                    }
                    catch (System.Exception ex) { Serilog.Log.Warning(ex, "BrainDrain: could not open audio folder"); }
                };
            this.FindControl<TextBlock>("TxtAudioFolderPath")!.Text = BrainDrainSchedule.AudioFolderPath;
        }

        private readonly CheckBox _keepClear;

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += ReloadSettings;
            LoadKeepClear();
            LoadAudio();
            // WPF OnLoaded:43: the Studio tab keeps this card alive, so re-scan or the count goes stale.
            CoreBrainDrain.ReloadClips();
            RefreshClipCount();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= ReloadSettings;
            base.OnDetachedFromVisualTree(e);
        }

        private void LoadKeepClear() => _keepClear.IsChecked = CoreSettings.Current.BrainDrainKeepPicturesClear;

        private void RefreshClipCount()
        {
            var clips = CoreBrainDrain.ClipCount;
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips == 0;
        }
    }
}
