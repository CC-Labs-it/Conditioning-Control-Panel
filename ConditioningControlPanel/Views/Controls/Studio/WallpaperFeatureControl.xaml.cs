using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ConditioningControlPanel.Views.Controls.Studio
{
    /// <summary>
    /// STUDIO RACK · WALLPAPER. Master toggle + a mutually-exclusive Static / Dynamic / Own image mode (three
    /// buttons, the selected one filled), plus Rotate and the 10-300s interval - those two apply to Static
    /// and Dynamic only, and the collage layout always rolls at random. Static drives the composed
    /// collage; Dynamic the moving one (<c>App.DynamicWallpaper</c>); Own image the one picture the user
    /// picked. All run only during a session (or the whole time CCP is open with "Run with CCP") and only
    /// one ever runs - <c>App.Wallpaper.StartCollage</c>
    /// routes to the chosen mode. Dynamic is not available in performance mode. Same settings keys as the
    /// old Monitors collage block, plus <c>CollageMode</c> / <c>CollageOwnImagePath</c>.
    /// </summary>
    public partial class WallpaperFeatureControl : UserControl, Features.ISettingsRebindable
    {
        private const string FeatureArtPath = "features/wallpaper.png";

        private bool _isLoading = true;
        private bool _syncingInterval;
        private Models.WallpaperMode _mode;

        // Tracks WHICH AppSettings instance the hook is attached to: the rack hosts this panel for
        // good, and a cloud restore swaps the instance. See ISettingsRebindable.
        private Features.SettingsHook? _settingsHook;

        public WallpaperFeatureControl()
        {
            InitializeComponent();
            Loaded += OnLoaded;
            Unloaded += OnUnloaded;
            // The rack keeps this mounted, so "too few images" can go stale while it sits hidden.
            IsVisibleChanged += (_, e) => { if (e.NewValue is true) RefreshHint(); };
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            RebindToCurrentSettings();
            ApplyFeatureArt();
            if (App.Mods != null) App.Mods.ModChanged += OnModChanged;
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _settingsHook?.Unhook();
            if (App.Mods != null) App.Mods.ModChanged -= OnModChanged;
        }

        private void OnModChanged(object? sender, Models.ModPackage mod) =>
            Dispatcher.BeginInvoke(new Action(ApplyFeatureArt));

        /// <inheritdoc/>
        public void RebindToCurrentSettings()
        {
            (_settingsHook ??= new Features.SettingsHook(OnSettingsPropertyChanged)).Rebind();
            LoadFromSettings();
        }

        /// <summary>A mod that ships features/wallpaper.png repaints both plates, in place.</summary>
        private void ApplyFeatureArt()
        {
            try
            {
                var hero = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 480);
                if (hero != null && HeroArtBrush is { IsFrozen: false }) HeroArtBrush.ImageSource = hero;

                var side = Services.ModResourceResolver.ResolveImageDecoded(FeatureArtPath, 800);
                if (side != null && SideArtBrush is { IsFrozen: false }) SideArtBrush.ImageSource = side;
            }
            catch (Exception ex)
            {
                App.Logger?.Debug("WallpaperFeatureControl.ApplyFeatureArt: {E}", ex.Message);
            }
        }

        /// <summary>
        /// The rack's right-click: OFF ONLY. Switching this on can put pictures on the desktop at once
        /// (Run with CCP, or a running session), so on is the panel's own toggle and nothing else.
        /// Writes the setting rather than flipping the box: a panel never shown has not loaded yet, and
        /// its handler would swallow the flip. A box greyed by the session lock refuses here too.
        /// </summary>
        internal void SwitchOffFromRack()
        {
            var s = App.Settings?.Current;
            if (s == null || !s.CollageWallpaperEnabled || !ChkEnable.IsEnabled) return;
            s.CollageWallpaperEnabled = false;
            App.Settings?.Save();
            LoadFromSettings();
            ApplyWallpaperEngine();
        }

        internal void LoadFromSettings()
        {
            var s = App.Settings?.Current;
            if (s == null) return;
            _isLoading = true;
            try
            {
                ChkEnable.IsChecked = s.CollageWallpaperEnabled;
                _mode = s.CollageMode;
                UpdateModeButtons();
                UpdateOwnImageLabel(s);
                ChkWpRotate.IsChecked = s.CollageWallpaperRotate;
                ChkWpRunWithCcp.IsChecked = s.CollageRunWithCcp;
                ApplyIntervalToUi(s.CollageWallpaperIntervalSeconds);
                UpdateVisibility();
            }
            finally { _isLoading = false; }
            RefreshHint();
        }

        /// <summary>Say why the wallpaper will not run as picked, instead of it silently doing nothing.
        /// The folder scan runs off the UI thread: an assets folder can sit on a slow drive.</summary>
        private void RefreshHint()
        {
            var s = App.Settings?.Current;
            if (s == null || !s.CollageWallpaperEnabled) { ShowHint(null); return; }

            if (s.CollageMode == Models.WallpaperMode.Dynamic && App.DynamicWallpaper?.IsUnavailable == true)
            {
                ShowHint(Localization.Loc.Get("hint_wallpaper_dynamic_unavailable"));
                return;
            }
            if (s.CollageMode != Models.WallpaperMode.Dynamic && Services.WallpaperService.WriteWouldLoseDesktop())
            {
                ShowHint(Localization.Loc.Get("hint_wallpaper_desktop_kept"));
                return;
            }
            if (s.CollageMode == Models.WallpaperMode.OwnImage) { ShowHint(null); return; }

            var selection = Services.CollageSources.Snapshot();
            _ = System.Threading.Tasks.Task.Run(() =>
            {
                bool enough = false;
                try { enough = Services.CollageSources.HasEnough(selection); }
                catch (Exception ex) { Diag.Swallowed(ex, "wallpaper hint scan"); enough = true; }
                Dispatcher.BeginInvoke(new Action(() =>
                    ShowHint(enough ? null : Localization.Loc.Get("hint_wallpaper_needs_images"))));
            });
        }

        private void ShowHint(string? text)
        {
            TxtWpHint.Text = text ?? "";
            TxtWpHint.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void OnSettingsPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(Models.AppSettings.CollageWallpaperEnabled) ||
                e.PropertyName == nameof(Models.AppSettings.CollageMode) ||
                e.PropertyName == nameof(Models.AppSettings.CollageOwnImagePath) ||
                e.PropertyName == nameof(Models.AppSettings.CollageWallpaperRotate) ||
                e.PropertyName == nameof(Models.AppSettings.CollageWallpaperIntervalSeconds) ||
                e.PropertyName == nameof(Models.AppSettings.CollageRunWithCcp))
            {
                Dispatcher.BeginInvoke(new Action(LoadFromSettings));
            }
        }

        private void ChkEnable_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.CollageWallpaperEnabled = ChkEnable.IsChecked ?? false;
            App.Settings?.Save();
            UpdateVisibility();
            ApplyWallpaperEngine();
        }

        private void BtnWpMode_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            var mode = ReferenceEquals(sender, BtnWpDynamic) ? Models.WallpaperMode.Dynamic
                     : ReferenceEquals(sender, BtnWpOwn) ? Models.WallpaperMode.OwnImage
                     : Models.WallpaperMode.Static;
            if (mode == _mode) return;

            // Dynamic is not available in performance mode: the hint says why; the highlight stays.
            if (mode == Models.WallpaperMode.Dynamic && s.PerformanceMode)
            {
                ShowHint(Localization.Loc.Get("hint_wallpaper_dynamic_perf"));
                return;
            }

            // Own image with nothing (usable) picked yet: the picker opens straight away; cancel keeps the
            // current mode.
            if (mode == Models.WallpaperMode.OwnImage && !File.Exists(s.CollageOwnImagePath) && !PickOwnImage(s)) return;

            s.CollageMode = mode;
            App.Settings?.Save();
            _mode = mode;
            UpdateModeButtons();
            UpdateVisibility();
            ApplyWallpaperEngine();
        }

        private void BtnWpOwnBrowse_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null || !PickOwnImage(s)) return;
            // Picked while the wallpaper is up (a session, or "Run with CCP") in Own image mode: it shows straight away.
            if (Services.WallpaperService.ShouldBeUp(s, App.IsEngineRunning))
                App.Wallpaper?.ApplyOwnImageNow();   // no-op unless Own image is the mode
        }

        /// <summary>Let the user pick their own wallpaper image (jpg / png / bmp - what Windows sets
        /// reliably). Saves the path; false when cancelled.</summary>
        private bool PickOwnImage(Models.AppSettings s)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = Localization.Loc.Get("title_wallpaper_own_pick"),
                Filter = Localization.Loc.Get("filter_wallpaper_own_images") + "|*.jpg;*.jpeg;*.png;*.bmp",
                CheckFileExists = true,
                Multiselect = false,
            };
            try
            {
                var dir = Path.GetDirectoryName(s.CollageOwnImagePath);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) dialog.InitialDirectory = dir;
            }
            catch (Exception ex) { Diag.Swallowed(ex, "own image start folder"); }

            var owner = Window.GetWindow(this);
            bool? picked = owner != null ? dialog.ShowDialog(owner) : dialog.ShowDialog();
            if (picked != true) return false;

            s.CollageOwnImagePath = dialog.FileName;
            App.Settings?.Save();
            UpdateOwnImageLabel(s);
            return true;
        }

        /// <summary>The selected mode's button is filled pink, the other two outlined - the Assets-tab
        /// preset look (SmallPinkButton / OutlineButton).</summary>
        private void UpdateModeButtons()
        {
            if (TryFindResource("SmallPinkButton") is not Style selected ||
                TryFindResource("OutlineButton") is not Style other) return;
            BtnWpStatic.Style = _mode == Models.WallpaperMode.Static ? selected : other;
            BtnWpDynamic.Style = _mode == Models.WallpaperMode.Dynamic ? selected : other;
            BtnWpOwn.Style = _mode == Models.WallpaperMode.OwnImage ? selected : other;
        }

        private void UpdateOwnImageLabel(Models.AppSettings s)
        {
            var path = s.CollageOwnImagePath;
            if (string.IsNullOrWhiteSpace(path))
            {
                TxtWpOwnImage.Text = Localization.Loc.Get("setting_wallpaper_own_none");
                TxtWpOwnImage.ToolTip = null;
                return;
            }
            var name = Path.GetFileName(path);
            TxtWpOwnImage.Text = File.Exists(path)
                ? name
                : string.Format(Localization.Loc.Get("setting_wallpaper_own_missing"), name);
            TxtWpOwnImage.ToolTip = path;
        }

        /// <summary>"Run with CCP": ON needs the warning window; Cancel snaps the
        /// toggle back OFF and nothing is saved. OFF needs no dialog. Applies right away either way.</summary>
        private void ChkWpRunWithCcp_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            bool on = ChkWpRunWithCcp.IsChecked == true;

            if (on && !s.CollageRunWithCcp)
            {
                var feature = Localization.Loc.Get("warning_wallpaper_run_with_ccp_feature");
                var dlg = new WarningDialog(
                    Localization.Loc.GetF("warning_enable_feature_title", feature),
                    Localization.Loc.Get("warning_wallpaper_run_with_ccp_body"),
                    Localization.Loc.GetF("warning_enable_feature_confirm", feature))
                {
                    Owner = Window.GetWindow(this) ?? Application.Current?.MainWindow,
                    Topmost = true,
                };
                if (dlg.ShowDialog() != true || !dlg.Confirmed)
                {
                    // Deferred so the toggle animation doesn't stick ON (same as ChkNoPanic_Changed).
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        _isLoading = true;
                        ChkWpRunWithCcp.IsChecked = false;
                        _isLoading = false;
                    }));
                    return;
                }
            }

            s.CollageRunWithCcp = on;
            App.Settings?.Save();
            try { App.Wallpaper?.ApplyRunPolicy(); }
            catch (Exception ex) { App.Logger?.Warning("[Wallpaper] run-with-CCP apply failed: {E}", ex.Message); }
        }

        private void ChkWpRotate_Changed(object sender, RoutedEventArgs e)
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.CollageWallpaperRotate = ChkWpRotate.IsChecked ?? true;
            App.Settings?.Save();
            UpdateVisibility();
            App.Wallpaper?.RefreshCollageSchedule();
        }

        private void SliderWpInterval_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isLoading || _syncingInterval) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            s.CollageWallpaperIntervalSeconds = (int)Math.Round(e.NewValue);
            _syncingInterval = true;
            try
            {
                TxtWpInterval.Text = s.CollageWallpaperIntervalSeconds.ToString();
                TxtWpIntervalPretty.Text = FormatInterval(s.CollageWallpaperIntervalSeconds);
            }
            finally { _syncingInterval = false; }
            App.Settings?.Save();
            App.Wallpaper?.RefreshCollageSchedule();
        }

        private void TxtWpInterval_LostFocus(object sender, RoutedEventArgs e) => CommitTypedInterval();

        private void TxtWpInterval_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter) { CommitTypedInterval(); e.Handled = true; }
        }

        private void CommitTypedInterval()
        {
            if (_isLoading) return;
            var s = App.Settings?.Current;
            if (s == null) return;
            if (int.TryParse(TxtWpInterval.Text?.Trim(), out var typed))
                s.CollageWallpaperIntervalSeconds = typed;   // setter clamps to [10, 86400]
            ApplyIntervalToUi(s.CollageWallpaperIntervalSeconds);
            App.Settings?.Save();
            App.Wallpaper?.RefreshCollageSchedule();
        }

        private void ApplyIntervalToUi(int seconds)
        {
            _syncingInterval = true;
            try
            {
                SliderWpInterval.Value = Math.Clamp(seconds, 10, 300);   // slider spans 10-300; the box can go higher
                TxtWpInterval.Text = seconds.ToString();
                TxtWpIntervalPretty.Text = FormatInterval(seconds);
            }
            finally { _syncingInterval = false; }
        }

        private void UpdateVisibility()
        {
            bool own = _mode == Models.WallpaperMode.OwnImage;
            bool rotate = ChkWpRotate.IsChecked == true;

            // Own image: just the picked file + Browse - no collage, so none of the collage rows.
            RowWpOwnImage.Visibility = own ? Visibility.Visible : Visibility.Collapsed;
            RowWpRotate.Visibility = !own ? Visibility.Visible : Visibility.Collapsed;
            RowWpInterval.Visibility = (!own && rotate) ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>Apply an on/off or mode change. Outside a session nothing runs; the next session start
        /// picks the setting up.</summary>
        private void ApplyWallpaperEngine()
        {
            try { App.Wallpaper?.RestartCollage(); }
            catch (Exception ex) { App.Logger?.Warning("[Wallpaper] engine apply failed: {E}", ex.Message); }
            RefreshHint();
        }

        private static string FormatInterval(int seconds)
        {
            if (seconds < 60) return seconds + "s";
            if (seconds < 3600)
            {
                int m = seconds / 60, sec = seconds % 60;
                return sec == 0 ? m + "m" : m + "m " + sec + "s";
            }
            int h = seconds / 3600, mm = (seconds % 3600) / 60;
            return mm == 0 ? h + "h" : h + "h " + mm + "m";
        }
    }
}
