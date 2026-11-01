using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Views.Features;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Views.Controls.Studio
{
    /// <summary>Visual settings drive BrainDrainOverlay while the engine runs.
    /// Audio playback and capture-based melt remain unported.</summary>
    public partial class BrainDrainFeatureControl : UserControl
    {
        private readonly string? _legacyFolder;
        public BrainDrainFeatureControl() : this(null) { }
        internal BrainDrainFeatureControl(string? legacyFolder)
        {
            _legacyFolder = legacyFolder;
            AvaloniaXamlLoader.Load(this);

            SliderLabel.Wire(this, "SliderIntensity", "TxtIntensity", v => $"{(int)v}%");
            SliderLabel.Wire(this, "SliderBlurStrength", "TxtBlurStrength", v => $"{(int)v}%");

            // WPF .xaml.cs:161-170,186-191: both buttons open the same primary folder.
            this.FindControl<Button>("BtnRefreshAudio")!.Click += (_, _) => RefreshClipCount();
            this.FindControl<Button>("BtnOpenAudioFolder")!.Click += (_, _) => OpenAudioFolder();
            this.FindControl<Button>("BtnOpenAudioFolderEmpty")!.Click += (_, _) => OpenAudioFolder();

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

            // WPF .xaml.cs:106,213-217,346-355: capture visibility is a live comfort setting.
            // X11 has no capture-exclusion equivalent (Win32Overlay.cs:50-54).
            this.FindControl<Grid>("CaptureRow")!.IsVisible = System.OperatingSystem.IsWindows();
            var capture = this.FindControl<CheckBox>("ChkAllowCapture")!;
            capture.IsCheckedChanged += (_, _) =>
            {
                if (_loading) return;
                CoreSettings.Current.AllowOverlayCapture = capture.IsChecked ?? false;
                CoreSettings.Save();
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
            LoadKeepClear(); // WPF .xaml.cs:37-44: rescan when the persistent panel is opened.
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
        { if (e.PropertyName is nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainEnabled) or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainBlurStrength) or nameof(ConditioningControlPanel.Models.AppSettings.BrainDrainKeepPicturesClear) or nameof(ConditioningControlPanel.Models.AppSettings.AllowOverlayCapture)) QueueLoad(); }
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
                this.FindControl<CheckBox>("ChkAllowCapture")!.IsChecked = CoreSettings.Current.AllowOverlayCapture;
                this.FindControl<CheckBox>("ChkEnable")!.IsChecked = CoreSettings.Current.BrainDrainEnabled;
                this.FindControl<Slider>("SliderBlurStrength")!.Value = CoreSettings.Current.BrainDrainBlurStrength;
            }
            finally { _loading = false; }
            RefreshClipCount(); // WPF .xaml.cs:88-108: repaint the library after a settings rebind.
        }

        // WPF .xaml.cs:121-151: merged count, primary path, empty hint and legacy note.
        private void RefreshClipCount()
        {
            var path = BrainDrainClips.AudioFolderPath;
            var legacy = _legacyFolder ?? BrainDrainClips.LegacyFolderPath;
            var (clips, legacyCount) = BrainDrainClips.Scan(path, legacy);
            this.FindControl<TextBlock>("TxtClipCount")!.Text = Loc.GetF("st4_braindrain_clips_loaded_0", clips.Length);
            this.FindControl<Border>("NoAudioHint")!.IsVisible = clips.Length == 0;
            this.FindControl<TextBlock>("TxtAudioFolderPath")!.Text = path;
            var note = this.FindControl<TextBlock>("TxtLegacyFolderNote")!;
            var oldPath = this.FindControl<TextBlock>("TxtLegacyFolderPath")!;
            note.IsVisible = oldPath.IsVisible = legacyCount > 0;
            oldPath.Text = legacyCount > 0 ? legacy : string.Empty;
        }

        private void OpenAudioFolder()
        {
            try { Platform.ExternalOpener.Open(BrainDrainClips.EnsureFolder(BrainDrainClips.AudioFolderPath)); }
            catch (Exception ex) { Serilog.Log.Warning(ex, "Brain Drain: open clip folder failed"); }
        }
    }
}
