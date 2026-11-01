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

            // WPF .xaml.cs:88-106,221-263,291-319,346-380. These edit saved
            // preferences; the audio and visual hosts are still missing on this head.
            LoadEditors();
            this.FindControl<CheckBox>("ChkEnable")!.IsCheckedChanged += (_, _) =>
            {
                if (_loadingEditors) return;
                CoreSettings.Current.BrainDrainEnabled = this.FindControl<CheckBox>("ChkEnable")!.IsChecked ?? false;
                CoreSettings.Save();
            };
            this.FindControl<Slider>("SliderIntensity")!.ValueChanged += (_, e) =>
            {
                if (_loadingEditors) return;
                CoreSettings.Current.BrainDrainIntensity = (int)e.NewValue;
                CoreSettings.Save();
            };
            this.FindControl<Slider>("SliderBlurStrength")!.ValueChanged += (_, e) =>
            {
                if (_loadingEditors) return;
                CoreSettings.Current.BrainDrainBlurStrength = (int)e.NewValue;
                CoreSettings.Save();
            };
            this.FindControl<CheckBox>("ChkMelt")!.IsCheckedChanged += (_, _) =>
            {
                if (_loadingEditors) return;
                CoreSettings.Current.BrainDrainMeltEnabled = this.FindControl<CheckBox>("ChkMelt")!.IsChecked ?? false;
                CoreSettings.Save();
            };
            this.FindControl<CheckBox>("ChkHighRefresh")!.IsCheckedChanged += (_, _) =>
            {
                if (_loadingEditors) return;
                CoreSettings.Current.BrainDrainHighRefresh = this.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked ?? false;
                CoreSettings.Save();
            };
            this.FindControl<CheckBox>("ChkAllowCapture")!.IsCheckedChanged += (_, _) =>
            {
                if (_loadingEditors) return;
                CoreSettings.Current.AllowOverlayCapture = this.FindControl<CheckBox>("ChkAllowCapture")!.IsChecked ?? false;
                CoreSettings.Save();
            };
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();

            // WPF .xaml.cs:326-335 saves this preference; OverlayService re-sorts
            // the windows in response. The Avalonia visual consumer has not landed,
            // so the existing Core BrainDrainKeepClear rule cannot act here yet.
            // Keep this preference available for when that consumer is ported.
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
        private bool _loadingEditors;
        private SettingsModel? _settings;
        private SettingsService? _settingsService;

        // WPF .xaml.cs:68-110,207-218: follow both instance replacement and live edits.
        private void RebindSettings()
        {
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            _settings = CoreSettings.Current;
            _settings.PropertyChanged += OnSettingsChanged;
            LoadVolume();
            LoadEditors();
        }

        private void OnSettingsChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName is nameof(SettingsModel.BrainDrainVolume)
                or nameof(SettingsModel.BrainDrainEnabled) or nameof(SettingsModel.BrainDrainIntensity)
                or nameof(SettingsModel.BrainDrainBlurStrength) or nameof(SettingsModel.BrainDrainMeltEnabled)
                or nameof(SettingsModel.BrainDrainHighRefresh) or nameof(SettingsModel.AllowOverlayCapture))
                Dispatcher.UIThread.Post(() =>
                {
                    if (_settings != null && ReferenceEquals(sender, _settings))
                    {
                        LoadVolume();
                        LoadEditors();
                    }
                });
        }

        private void LoadEditors()
        {
            _loadingEditors = true;
            try
            {
                var s = CoreSettings.Current;
                this.FindControl<CheckBox>("ChkEnable")!.IsChecked = s.BrainDrainEnabled;
                this.FindControl<Slider>("SliderIntensity")!.Value = s.BrainDrainIntensity;
                this.FindControl<TextBlock>("TxtIntensity")!.Text = $"{s.BrainDrainIntensity}%";
                this.FindControl<Slider>("SliderBlurStrength")!.Value = s.BrainDrainBlurStrength;
                this.FindControl<TextBlock>("TxtBlurStrength")!.Text = $"{s.BrainDrainBlurStrength}%";
                this.FindControl<CheckBox>("ChkMelt")!.IsChecked = s.BrainDrainMeltEnabled;
                this.FindControl<CheckBox>("ChkHighRefresh")!.IsChecked = s.BrainDrainHighRefresh;
                this.FindControl<CheckBox>("ChkAllowCapture")!.IsChecked = s.AllowOverlayCapture;
            }
            finally { _loadingEditors = false; }
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
            _settingsService = CoreSettings.Service;
            if (_settingsService != null) _settingsService.CurrentReplaced += RebindSettings;
            RebindSettings();
            LoadKeepClear();
            RefreshClipCount(); // WPF .xaml.cs:40-43: re-scan whenever the rack is shown.
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            if (CoreSettings.Service is { } svc) svc.CurrentReplaced -= LoadKeepClear;
            if (_settingsService != null) _settingsService.CurrentReplaced -= RebindSettings;
            _settingsService = null;
            if (_settings != null) _settings.PropertyChanged -= OnSettingsChanged;
            _settings = null;
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
