using System;
using System.ComponentModel;
using System.IO;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    // WPF BrainDrainFeatureControl.xaml.cs:37-48,121-198,207-301,358-377,417-429.
    // Clip library, art and the audio rows are live. Desktop visuals (blur, melt) remain unported;
    // their dials are saved preferences only.
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
            {
                if (_loadingVolume || CoreSettings.Current.BrainDrainIntensity == (int)e.NewValue) return;
                CoreSettings.Current.BrainDrainIntensity = (int)e.NewValue;
                // WPF .xaml.cs:252-256: a running player reads the saved rate live (BrainDrainPlayer.Tick).
                CoreSettings.Save();
            };
            // WPF SliderBlurStrength_Changed (.xaml.cs:287-301): the visual half's dial, a saved preference here.
            _blur = SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");
            _blur.ValueChanged += (_, e) =>
            {
                if (_loadingVolume || CoreSettings.Current.BrainDrainBlurStrength == (int)e.NewValue) return;
                CoreSettings.Current.BrainDrainBlurStrength = (int)e.NewValue;
                CoreSettings.Save();
            };

            // WPF ChkEnable_Changed (.xaml.cs:221-241): the switch starts or stops the player only while the engine runs.
            _enable = this.FindControl<CheckBox>("ChkEnable")!;
            _enable.IsCheckedChanged += (_, _) =>
            {
                var on = _enable.IsChecked == true;
                if (_loadingVolume || CoreSettings.Current.BrainDrainEnabled == on) return;
                CoreSettings.Current.BrainDrainEnabled = on;
                CoreEngine.ApplyLive("braindrain", on);
                CoreSettings.Save();
            };
            // WPF ChkHighRefresh_Changed (.xaml.cs:358-377): the cadence is read at Start, so a running player is bounced.
            _highRefresh = this.FindControl<CheckBox>("ChkHighRefresh")!;
            _highRefresh.IsCheckedChanged += (_, _) =>
            {
                var on = _highRefresh.IsChecked == true;
                if (_loadingVolume || CoreSettings.Current.BrainDrainHighRefresh == on) return;
                CoreSettings.Current.BrainDrainHighRefresh = on;
                if (CoreEngine.IsRunning && BrainDrainPlayer.Instance is { IsRunning: true } player) { player.Stop(); player.Start(); }
                CoreSettings.Save();
            };

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

        private readonly CheckBox _keepClear, _enable, _highRefresh;
        private readonly Slider _volume, _intensity, _blur;
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
                _blur.Value = CoreSettings.Current.BrainDrainBlurStrength;
                _enable.IsChecked = CoreSettings.Current.BrainDrainEnabled;
                _highRefresh.IsChecked = CoreSettings.Current.BrainDrainHighRefresh;
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
            if (CoreSettings.Service is { } svc) { svc.CurrentReplaced += Observe; svc.CurrentReplaced += LoadKeepClear; svc.CurrentReplaced += LoadVolume; }
            Observe();
            LoadKeepClear();
            LoadVolume();
            RefreshClipCount(); // WPF .xaml.cs:40-43: re-scan whenever the rack is shown.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) { svc.CurrentReplaced -= Observe; svc.CurrentReplaced -= LoadKeepClear; svc.CurrentReplaced -= LoadVolume; }
            if (_observed != null) _observed.PropertyChanged -= OnSettingChanged;
            _observed = null;
            base.OnDetachedFromVisualTree(e);
        }

        // WPF OnSettingsPropertyChanged (.xaml.cs:207-219): a session, a preset, the rack or a voice
        // command that writes these rows repaints them here; the load guard keeps it from saving again.
        private ConditioningControlPanel.Models.AppSettings? _observed;
        private void Observe()
        {
            if (_observed != null) _observed.PropertyChanged -= OnSettingChanged;
            _observed = CoreSettings.Current;
            _observed.PropertyChanged += OnSettingChanged;
        }

        private void OnSettingChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is not (nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainEnabled)
                or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainIntensity)
                or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainVolume)
                or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainHighRefresh)
                or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainBlurStrength))) return;
            void Paint() { if (_observed != null && ReferenceEquals(sender, _observed)) LoadVolume(); }
            if (Dispatcher.UIThread.CheckAccess()) Paint(); else Dispatcher.UIThread.Post(Paint);
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
