using System;
using System.IO;
using System.ComponentModel;
using Avalonia.Threading;
using SettingsModel = ConditioningControlPanel.Models.AppSettings;
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

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            LoadVolume();
            this.FindControl<Slider>("SliderVolume")!.ValueChanged += (_, e) =>
            {
                if (_loadingVolume) return;
                // WPF .xaml.cs:269-282. Playback gain belongs to the pending audio service port.
                CoreSettings.Current.BrainDrainVolume = (int)e.NewValue;
                this.FindControl<TextBlock>("TxtVolume")!.Text = $"{(int)e.NewValue}%";
                CoreSettings.Save();
            };
            RefreshClipCount();

            // Playback and visual controls stay inert until their hosts land.
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
        }

        private readonly CheckBox _keepClear;
        private bool _loadingVolume;
        private SettingsModel? _volumeSettings;
        private SettingsService? _volumeService;

        // WPF .xaml.cs:68-110,207-218: follow both instance replacement and live edits.
        private void RebindVolume()
        {
            if (_volumeSettings != null) _volumeSettings.PropertyChanged -= OnVolumeChanged;
            _volumeSettings = CoreSettings.Current;
            _volumeSettings.PropertyChanged += OnVolumeChanged;
            LoadVolume();
        }

        private void OnVolumeChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(SettingsModel.BrainDrainVolume))
                Dispatcher.UIThread.Post(() =>
                {
                    if (_volumeSettings != null && ReferenceEquals(sender, _volumeSettings)) LoadVolume();
                });
        }

        private void LoadVolume()
        {
            _loadingVolume = true;
            try
            {
                var volume = CoreSettings.Current.BrainDrainVolume;
                this.FindControl<Slider>("SliderVolume")!.Value = volume;
                this.FindControl<TextBlock>("TxtVolume")!.Text = $"{volume}%";
                // WPF .xaml.cs:100: reveal changed values, never force a user's open fold shut.
                if (volume != 100) this.FindControl<MoreFold>("FoldMore")!.IsOpen = true;
            }
            finally { _loadingVolume = false; }
        }

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced += LoadKeepClear;
            _volumeService = CoreSettings.Service;
            if (_volumeService != null) _volumeService.CurrentReplaced += RebindVolume;
            RebindVolume();
            LoadKeepClear();
            RefreshClipCount(); // WPF .xaml.cs:40-43: re-scan whenever the rack is shown.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= LoadKeepClear;
            if (_volumeService != null) _volumeService.CurrentReplaced -= RebindVolume;
            _volumeService = null;
            if (_volumeSettings != null) _volumeSettings.PropertyChanged -= OnVolumeChanged;
            _volumeSettings = null;
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
