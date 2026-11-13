using System;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    // WPF BrainDrainFeatureControl.xaml.cs:37-48,121-198,417-429.
    // Clip library and art are live. Audio playback and desktop visuals remain unported.
    public partial class BrainDrainFeatureControl : UserControl
    {
        public BrainDrainFeatureControl()
        {
            AvaloniaXamlLoader.Load(this);

            ModArt.BindFeaturePlates(this, "features/brain_drain.png",
                this.FindControl<Border>("HeroArt"), this.FindControl<Border>("SideArt"));
            // WPF .xaml.cs:161-175: both buttons create and open the primary folder.
            foreach (var name in new[] { "BtnOpenAudioFolder", "BtnOpenAudioFolderEmpty" })
                this.FindControl<Button>(name)!.Click += async (_, _) =>
                {
                    try
                    {
                        Directory.CreateDirectory(BrainDrainAudioPolicy.AudioFolder);
                        await ExternalOpener.OpenAsync(TopLevel.GetTopLevel(this), BrainDrainAudioPolicy.AudioFolder);
                    }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain: open clip folder failed"); }
                };

            // WPF SliderIntensity_Changed (.xaml.cs:246-262): saves the clip rate BrainDrainPlayer.Tick reads.
            _intensity = SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            _intensity.ValueChanged += (_, e) =>
            { if (!_loadingVolume && CoreSettings.Current.BrainDrainIntensity != (int)e.NewValue) { CoreSettings.Current.BrainDrainIntensity = (int)e.NewValue; CoreSettings.Save(); } };
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            RefreshClipCount();

            // Audio and visual controls stay inert until their hosts land.
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();

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

            // WPF SliderVolume_Changed (.xaml.cs:269-282): Brain Drain's own clip volume. BrainDrainPlayer
            // follows BrainDrainVolume itself, so a playing clip picks the change up too.
            _volume = this.FindControl<Slider>("SliderVolume")!;
            _volumeText = this.FindControl<TextBlock>("TxtVolume")!;
            LoadVolume();
            _volume.ValueChanged += (_, e) =>
            {
                if (_loadingVolume) return;
                var v = (int)e.NewValue;
                _volumeText.Text = $"{v}%";
                if (CoreSettings.Current.BrainDrainVolume == v) return;
                CoreSettings.Current.BrainDrainVolume = v;
                CoreSettings.Save();
            };
        }

        private readonly CheckBox _keepClear;
        private readonly Slider _volume, _intensity;
        private readonly TextBlock _volumeText;
        private bool _loadingVolume;

        private void LoadVolume()
        {
            var v = CoreSettings.Current.BrainDrainVolume;
            _loadingVolume = true;
            try
            {
                _volume.Value = v;
                _intensity.Value = CoreSettings.Current.BrainDrainIntensity; // WPF .xaml.cs:96-97
                _volumeText.Text = $"{v}%";
                if (v != 100) this.FindControl<MoreFold>("FoldMore")!.IsOpen = true; // never hide a changed setting
            }
            finally { _loadingVolume = false; }
        }

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) { svc.CurrentReplaced += LoadKeepClear; svc.CurrentReplaced += LoadVolume; }
            LoadKeepClear();
            LoadVolume();
            RefreshClipCount(); // WPF .xaml.cs:40-43: re-scan whenever the rack is shown.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) { svc.CurrentReplaced -= LoadKeepClear; svc.CurrentReplaced -= LoadVolume; }
            base.OnDetachedFromVisualTree(e);
        }

        private void LoadKeepClear() => _keepClear.IsChecked = CoreSettings.Current.BrainDrainKeepPicturesClear;

        private void RefreshClipCount()
        {
            // Reused from the old lane. WPF .xaml.cs:121-151,186-198.
            try { Directory.CreateDirectory(BrainDrainAudioPolicy.AudioFolder); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain: could not create clip folder"); }
            var clips = BrainDrainAudioPolicy.Discover(BrainDrainAudioPolicy.AudioFolder, BrainDrainAudioPolicy.LegacyFolder).Length;
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips == 0;
            this.FindControl<TextBlock>("TxtAudioFolderPath")!.Text = BrainDrainAudioPolicy.AudioFolder;
            var legacy = BrainDrainAudioPolicy.Enumerate(BrainDrainAudioPolicy.LegacyFolder).Length > 0;
            this.FindControl<TextBlock>("TxtLegacyFolderNote")!.IsVisible = legacy;
            this.FindControl<TextBlock>("TxtLegacyFolderPath")!.IsVisible = legacy;
            this.FindControl<TextBlock>("TxtLegacyFolderPath")!.Text = legacy ? BrainDrainAudioPolicy.LegacyFolder : "";
        }
    }
}
