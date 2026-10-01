using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Windows
{
    /// <summary>
    /// One-dot quick recal: shows a center pink dot, samples ~2 s of gaze projections while the
    /// user stares at it, computes the mean drift from screen center and persists it as the
    /// webcam calibration's runtime offset. At runtime the offset is added after the polynomial
    /// projection so the cursor lands where the user is actually looking - no full 16-point
    /// recalibration needed.
    ///
    /// PORTED from ConditioningControlPanel/Windows/WebcamQuickRecalWindow.xaml.cs over
    /// Platform/WebcamTracker (OnGazeMove / Calibration / SetRuntimeOffset); the median maths is
    /// Core GazeEngine.MedianAfterSaccadeSettle (WPF's window calls it too). The target is the
    /// centre of the calibrated monitor (Calibration.MonitorBounds, the DIP frame OnGazeMove emits
    /// in), else this window's own centre as WPF; the window opens on that monitor when it is
    /// connected (WPF App.ApplyCalibrationScreenPlacement). Deviations: DialogResult becomes
    /// Close(x) (ShowDialog&lt;bool?&gt;); the hotkey hint is hidden (below).
    /// </summary>
    public partial class WebcamQuickRecalWindow : Window
    {
        private const int ReadyMs = 600;
        private const int SampleMs = 2000;
        private const int FinishHoldMs = 350;

        private readonly List<(double X, double Y)> _samples = new();
        private bool _collecting, _cancelled, _completedOk;
        private RuntimeOffsetData? _savedOffset;
        private static WebcamTracker Tracker => WebcamTracker.Instance;

        private readonly Ellipse _dot;
        private readonly TextBlock _txtStatus, _txtHotkeyHint, _txtErrorDetail;
        private readonly Border _errorPanel;

        public WebcamQuickRecalWindow()
        {
            AvaloniaXamlLoader.Load(this);
            PlaceOnCalibratedScreen(this);

            _dot = this.FindControl<Ellipse>("Dot")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtHotkeyHint = this.FindControl<TextBlock>("TxtHotkeyHint")!;
            _txtErrorDetail = this.FindControl<TextBlock>("TxtErrorDetail")!;
            _errorPanel = this.FindControl<Border>("ErrorPanel")!;

            // Discoverability: this window is the one place every user of Quick Recal provably
            // reaches, so it is where the global chord gets taught. The real line quotes the
            // user's own (rebindable) camera shortcut alongside it.
            // The tip is HIDDEN on this head rather than worded, and the old note named the wrong
            // blocker. MainWindow.QuickRecalHotkeyHint (MainWindow/MainWindow.xaml.cs:1263) is just
            // Loc.GetF("webcam_quick_recal_hotkey_hint", QuickRecalHotkeyChord, CameraShortcutChord),
            // the key is in CCP.Core/Localization/Languages/*.json, and both chords are derivable
            // here (the quick-recal one is a constant, the camera one reads
            // CoreSettings.Current.CompanionPrompt.CameraShortcut*). So it is writable today.
            // What stops it is that BOTH chords are Win32 RegisterHotKey registrations in
            // ConditioningControlPanel/Services/Input/GlobalHotkeyService.cs, and this head installs no
            // global hotkey of any kind - "runs from anywhere with Ctrl+Alt+G" would be teaching a
            // key that does nothing. An empty hint is a gap; a taught dead key is a lie.
            _txtHotkeyHint.IsVisible = false;

            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => Close(_completedOk);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { _cancelled = true; _collecting = false; Close(false); } };
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            if (!Tracker.IsRunning)
            {
                ShowError("Webcam tracking is not running. Start tracking before quick-recalibrating.");
                return;
            }
            if (Tracker.Calibration == null)
            {
                ShowError("No calibration loaded. Run the full Calibrate (16-point) flow first — quick recal only nudges an existing calibration.");
                return;
            }
            // Sample the raw projection: park the old offset, restore it unless we finish.
            _savedOffset = Tracker.Calibration.RuntimeOffset;
            Tracker.SetRuntimeOffset(null, persist: false);
            Tracker.OnGazeMove += OnGazeMove;
            Tracker.StateChanged += OnTrackerStateChanged;
            try { await RunSequenceAsync(); }
            catch (Exception ex)
            {
                Log.Warning(ex, "WebcamQuickRecalWindow: quick-recal sequence threw");
                ShowError("Quick recal failed unexpectedly. See logs/app.log for details.");
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Tracker.OnGazeMove -= OnGazeMove;
            Tracker.StateChanged -= OnTrackerStateChanged;
            if (!_completedOk && _savedOffset != null) Tracker.SetRuntimeOffset(_savedOffset, persist: false);
        }

        private void OnTrackerStateChanged()
        {
            if (Tracker.IsRunning) return;
            _cancelled = true;
            _collecting = false;
            Close(false);
        }

        private void OnGazeMove(global::Avalonia.Point p)
        {
            if (_collecting) _samples.Add((p.X, p.Y));
        }

        private async Task RunSequenceAsync()
        {
            _dot.IsVisible = true;
            _txtStatus.Text = "Get comfortable, then look at the pink dot.";
            await Task.Delay(ReadyMs);
            if (_cancelled) return;

            _txtStatus.Text = "Hold your gaze on the dot…";
            _samples.Clear();
            Play("lvup.mp3", 0.25f);   // CalibrationSoundService.DotSampleStart
            _collecting = true;
            await Task.Delay(SampleMs);
            _collecting = false;
            if (_cancelled) return;

            if (_samples.Count < 15)
            {
                ShowError($"Didn't capture enough gaze samples ({_samples.Count}). Make sure your face is visible and try again.");
                return;
            }
            var (dx, dy) = OffsetFor(Tracker.Calibration, Bounds.Size, _samples);
            Tracker.SetRuntimeOffset(new RuntimeOffsetData { Dx = dx, Dy = dy, CapturedAt = DateTime.UtcNow }, persist: true);
            Log.Information("WebcamQuickRecalWindow: offset captured dx={Dx:F1} dy={Dy:F1} from {N} samples", dx, dy, _samples.Count);

            _completedOk = true;
            Play("chime3.mp3", 0.55f);   // CalibrationSoundService.QuickRecalComplete
            _txtStatus.Text = $"Done. Cursor nudged by ({dx:F0}, {dy:F0}) px.";
            await Task.Delay(FinishHoldMs);
            Close(true);
        }

        /// <summary>Drift of the settled gaze from the centre of the calibrated monitor (falling back to
        /// <paramref name="window"/>'s centre when the calibration predates MonitorBounds).</summary>
        internal static (double Dx, double Dy) OffsetFor(WebcamCalibrationData? cal, global::Avalonia.Size window, IReadOnlyList<(double X, double Y)> samples)
        {
            var (mx, my) = GazeEngine.MedianAfterSaccadeSettle(samples, dropFirst: 10);
            var (w, h) = cal?.MonitorBounds is { Width: > 0, Height: > 0 } b ? ((double)b.Width, (double)b.Height) : (window.Width, window.Height);
            return (w / 2.0 - mx, h / 2.0 - my);
        }

        /// <summary>WPF App.ApplyCalibrationScreenPlacement: start on the monitor the calibration ran on
        /// (matched by its pixel origin), so a Maximized window maximizes there. Unknown or
        /// disconnected monitor: leave placement alone.</summary>
        internal static void PlaceOnCalibratedScreen(Window window)
        {
            try
            {
                if (WebcamTracker.Instance.Calibration?.MonitorBounds is not { } mb || window.Screens is not { } screens) return;
                foreach (var sc in screens.All)
                {
                    if (sc.Bounds.X != mb.X || sc.Bounds.Y != mb.Y) continue;
                    window.WindowStartupLocation = WindowStartupLocation.Manual;
                    window.Position = sc.Bounds.Position;
                    return;
                }
            }
            catch (Exception ex) { Log.Debug("Calibrated-screen placement failed: {Error}", ex.Message); }
        }

        /// <summary>CalibrationSoundService.Play: master volume on the ^1.5 curve, silence when missing.</summary>
        private static void Play(string file, float multiplier)
        {
            try
            {
                float v = (float)Math.Pow(CoreSettings.Current.MasterVolume / 100f * multiplier, 1.5);
                var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", file);
                if (v > 0.001f && System.IO.File.Exists(path)) CoreAudio.PlayOneShot(path, v, "calibration");
            }
            catch (Exception ex) { Log.Debug("Quick recal sound {File} failed: {Error}", file, ex.Message); }
        }

        /// <summary>The WPF original's error path: no tracking, or no calibration to nudge.</summary>
        private void ShowError(string detail)
        {
            _dot.IsVisible = false;
            _txtStatus.IsVisible = false;
            _txtErrorDetail.Text = detail;
            _errorPanel.IsVisible = true;
        }
    }
}
