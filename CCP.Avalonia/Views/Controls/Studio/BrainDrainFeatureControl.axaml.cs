using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    // Audio path: WPF BrainDrainFeatureControl.xaml.cs:211-297. Visual blur remains unported.
    public partial class BrainDrainFeatureControl : UserControl
    {
        public BrainDrainFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            RefreshClipCount();

            var enable = this.FindControl<CheckBox>("ChkEnable")!;
            var intensity = this.FindControl<Slider>("SliderIntensity")!;
            var refresh = this.FindControl<CheckBox>("ChkHighRefresh")!;
            void LoadAudio()
            {
                _loading = true;
                enable.IsChecked = CoreSettings.Current.BrainDrainEnabled;
                intensity.Value = CoreSettings.Current.BrainDrainIntensity;
                refresh.IsChecked = CoreSettings.Current.BrainDrainHighRefresh;
                _loading = false;
            }
            _loadAudio = LoadAudio;
            LoadAudio();
            enable.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainEnabled = enable.IsChecked == true;
                CoreEngine.ApplyLive("braindrain", enable.IsChecked == true);
                CoreSettings.Save();
            };
            intensity.PropertyChanged += (_, e) =>
            {
                if (_loading || e.Property != Slider.ValueProperty) return;
                CoreSettings.Current.BrainDrainIntensity = (int)intensity.Value;
                CoreSettings.Save();
            };
            refresh.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.BrainDrainHighRefresh = refresh.IsChecked == true;
                // WPF BrainDrainFeatureControl.xaml.cs:369-377: changing cadence stops the clip too.
                if (CoreEngine.IsRunning && Platform.BrainDrainPlayer.Instance is { IsRunning: true } player)
                { player.Stop(); player.Start(); }
                CoreSettings.Save();
            };
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();
            foreach (var name in new[] { "BtnOpenAudioFolder", "BtnOpenAudioFolderEmpty" })
                this.FindControl<Button>(name)!.Click += (_, _) =>
                {
                    try { Platform.ExternalOpener.Open(Platform.BrainDrainPlayer.EnsureFolder()); }
                    catch { }
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

        private readonly CheckBox _keepClear;
        private bool _loading;
        private readonly System.Action _loadAudio;

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += LoadKeepClear;
            LoadKeepClear();
            _loadAudio();
            RefreshClipCount();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= LoadKeepClear;
            base.OnDetachedFromVisualTree(e);
        }

        private void LoadKeepClear()
        {
            _keepClear.IsChecked = CoreSettings.Current.BrainDrainKeepPicturesClear;
            _loadAudio?.Invoke();
        }

        private void RefreshClipCount()
        {
            Platform.BrainDrainPlayer.Instance?.Reload();
            var clips = Platform.BrainDrainPlayer.Instance?.ClipCount ?? 0;
            this.FindControl<TextBlock>("TxtAudioFolderPath")!.Text = Platform.BrainDrainPlayer.Folder;
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips == 0;
        }
    }
}
