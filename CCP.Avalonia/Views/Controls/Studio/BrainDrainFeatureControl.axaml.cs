using System;
using System.ComponentModel;
using System.IO;
using Avalonia.Threading;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using SettingsModel = ConditioningControlPanel.Models.AppSettings;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    // WPF BrainDrainFeatureControl.xaml.cs:37-48,88-106,121-198,200-380,417-429.
    // Clip library, art and every editor are live. The desktop blur and melt visual remains unported.
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
            { if (!_loading && CoreSettings.Current.BrainDrainIntensity != (int)e.NewValue) { CoreSettings.Current.BrainDrainIntensity = (int)e.NewValue; CoreSettings.Save(); } };
            _blur = SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");
            _enable = this.FindControl<CheckBox>("ChkEnable")!;
            _melt = this.FindControl<CheckBox>("ChkMelt")!;
            _highRefresh = this.FindControl<CheckBox>("ChkHighRefresh")!;
            _allowCapture = this.FindControl<CheckBox>("ChkAllowCapture")!;

            RefreshClipCount();

            // WPF ChkEnable_Changed (.xaml.cs:221-242): save, and start or stop the clips only
            // while the engine runs (CoreEngine.ApplyLive, the same path as the wall toggle).
            _enable.IsCheckedChanged += (_, _) =>
            {
                var on = _enable.IsChecked ?? false;
                if (_loading || CoreSettings.Current.BrainDrainEnabled == on) return;
                CoreSettings.Current.BrainDrainEnabled = on;
                try { CoreEngine.ApplyLive("braindrain", on); }
                catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain start/stop failed"); }
                CoreSettings.Save();
            };
            // WPF SliderBlurStrength_Changed / ChkMelt_Changed / ChkAllowCapture_Changed
            // (.xaml.cs:291-319,346-357): saved preferences. The desktop blur and melt visual has
            // not landed on this head, so nothing reads these three here yet.
            _blur.ValueChanged += (_, e) =>
            { if (!_loading && CoreSettings.Current.BrainDrainBlurStrength != (int)e.NewValue) { CoreSettings.Current.BrainDrainBlurStrength = (int)e.NewValue; CoreSettings.Save(); } };
            _melt.IsCheckedChanged += (_, _) =>
            {
                var on = _melt.IsChecked ?? false;
                if (_loading || CoreSettings.Current.BrainDrainMeltEnabled == on) return;
                CoreSettings.Current.BrainDrainMeltEnabled = on;
                CoreSettings.Save();
            };
            _allowCapture.IsCheckedChanged += (_, _) =>
            {
                var on = _allowCapture.IsChecked ?? false;
                if (_loading || CoreSettings.Current.AllowOverlayCapture == on) return;
                CoreSettings.Current.AllowOverlayCapture = on;
                CoreSettings.Save();
            };
            // WPF ChkHighRefresh_Changed (.xaml.cs:359-380): the tick interval is read at Start, so a
            // running player is bounced for the new interval to take effect.
            _highRefresh.IsCheckedChanged += (_, _) =>
            {
                var on = _highRefresh.IsChecked ?? false;
                if (_loading || CoreSettings.Current.BrainDrainHighRefresh == on) return;
                CoreSettings.Current.BrainDrainHighRefresh = on;
                if (CoreSession.IsEngineRunning && BrainDrainPlayer.Instance is { IsRunning: true } player)
                {
                    try { player.Stop(); player.Start(); }
                    catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain restart failed"); }
                }
                CoreSettings.Save();
            };
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
            LoadEditors();
            _volume.ValueChanged += (_, e) =>
            {
                if (_loading) return;
                var v = (int)e.NewValue;
                _volumeText.Text = $"{v}%";
                if (CoreSettings.Current.BrainDrainVolume == v) return;
                CoreSettings.Current.BrainDrainVolume = v;
                CoreSettings.Save();
            };
        }

        private readonly CheckBox _keepClear, _enable, _melt, _highRefresh, _allowCapture;
        private readonly Slider _volume, _intensity, _blur;
        private readonly TextBlock _volumeText;
        private bool _loading;
        private SettingsModel? _hooked;

        // WPF LoadFromSettings (.xaml.cs:88-106): every editor on the panel paints from the settings.
        private void LoadEditors()
        {
            var s = CoreSettings.Current;
            var v = s.BrainDrainVolume;
            _loading = true;
            try
            {
                _enable.IsChecked = s.BrainDrainEnabled;
                _volume.Value = v;
                _intensity.Value = s.BrainDrainIntensity;
                _volumeText.Text = $"{v}%";
                if (v != 100) this.FindControl<MoreFold>("FoldMore")!.IsOpen = true; // never hide a changed setting
                _highRefresh.IsChecked = s.BrainDrainHighRefresh;
                _blur.Value = s.BrainDrainBlurStrength;
                _melt.IsChecked = s.BrainDrainMeltEnabled;
                _allowCapture.IsChecked = s.AllowOverlayCapture;
            }
            finally { _loading = false; }
        }

        // WPF OnSettingsPropertyChanged (.xaml.cs:200-218): DTRH, presets, remote and the wall
        // toggle write these settings directly, so the panel follows live edits, on the instance it
        // is hooked to (a reset or cloud restore swaps it; see Rehook).
        private void Rehook()
        {
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsChanged;
            _hooked = CoreSettings.Current;
            _hooked.PropertyChanged += OnSettingsChanged;
            LoadEditors();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SettingsModel.BrainDrainEnabled) or nameof(SettingsModel.BrainDrainIntensity)
                or nameof(SettingsModel.BrainDrainVolume) or nameof(SettingsModel.BrainDrainHighRefresh)
                or nameof(SettingsModel.BrainDrainBlurStrength) or nameof(SettingsModel.BrainDrainMeltEnabled)
                or nameof(SettingsModel.AllowOverlayCapture))
                Dispatcher.UIThread.Post(() => { if (ReferenceEquals(sender, _hooked)) LoadEditors(); });
        }

        // WPF re-reads on Loaded and on its settings rebind hook; the Studio tab keeps this control
        // alive, so a Reset or cloud restore (CurrentReplaced) must repaint the toggle.
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            if (CoreSettings.Service is { } svc) { svc.CurrentReplaced += LoadKeepClear; svc.CurrentReplaced += Rehook; }
            LoadKeepClear();
            Rehook();
            RefreshClipCount(); // WPF .xaml.cs:40-43: re-scan whenever the rack is shown.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) { svc.CurrentReplaced -= LoadKeepClear; svc.CurrentReplaced -= Rehook; }
            if (_hooked != null) _hooked.PropertyChanged -= OnSettingsChanged;
            _hooked = null;
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
