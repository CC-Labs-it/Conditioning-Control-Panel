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
    /// Core GazeEngine.MedianAfterSaccadeSettle, matching WPF's local median. The target is the
    /// current window's DIP center, where the dot is displayed, as in WPF. Deviations: DialogResult
    /// becomes Close(x) (ShowDialog&lt;bool?&gt;); the hotkey hint is hidden (below).
    /// </summary>
    public partial class WebcamQuickRecalWindow : Window
    {
        private const int ReadyMs = 600;
        private const int SampleMs = 2000;
        private const int FinishHoldMs = 350;

        private readonly List<(double X, double Y)> _samples = new();
        private bool _collecting, _cancelled, _completedOk, _offsetParked;
        private static readonly HashSet<WebcamQuickRecalWindow> Active = new();

        internal static void CloseAllForPanic()
        {
            foreach (var window in new List<WebcamQuickRecalWindow>(Active)) window.Close(false);
        }
        private RuntimeOffsetData? _savedOffset;
        private static WebcamTracker Tracker => WebcamTracker.Instance;

        private readonly Ellipse _dot;
        private readonly TextBlock _txtStatus, _txtHotkeyHint, _txtErrorDetail;
        private readonly Border _errorPanel;

        public WebcamQuickRecalWindow()
        {
            AvaloniaXamlLoader.Load(this);

            _dot = this.FindControl<Ellipse>("Dot")!;
            _txtStatus = this.FindControl<TextBlock>("TxtStatus")!;
            _txtHotkeyHint = this.FindControl<TextBlock>("TxtHotkeyHint")!;
            _txtErrorDetail = this.FindControl<TextBlock>("TxtErrorDetail")!;
            _errorPanel = this.FindControl<Border>("ErrorPanel")!;

            // ponytail: the full localized hint also teaches the camera start/stop chord, which
            // is not ported. Keep it hidden until both shortcuts work on this head; Windows Quick
            // Recal is now armed by MainShellWindow.QuickRecal, but Linux/Wayland still need a lane.
            _txtHotkeyHint.IsVisible = false;
            // WPF openers call App.ApplyCalibrationScreenPlacement: the dot must sit on the calibrated monitor.
            WebcamCalibrationWindow.PlaceOnCalibratedScreen(this);

            this.FindControl<Button>("BtnErrorClose")!.Click += (_, _) => Close(_completedOk);
            KeyDown += (_, e) => { if (e.Key == Key.Escape) { _cancelled = true; _collecting = false; Close(false); } };
        }

        protected override async void OnOpened(EventArgs e)
        {
            base.OnOpened(e);
            Active.Add(this);
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
            _offsetParked = true;
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
            _cancelled = true;
            _collecting = false;
            Active.Remove(this);
            base.OnClosed(e);
            Tracker.OnGazeMove -= OnGazeMove;
            Tracker.StateChanged -= OnTrackerStateChanged;
            if (!_completedOk && _offsetParked) Tracker.SetRuntimeOffset(_savedOffset, persist: false);
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
            var (dx, dy) = OffsetFor(Bounds.Size, _samples);
            Tracker.SetRuntimeOffset(new RuntimeOffsetData { Dx = dx, Dy = dy, CapturedAt = DateTime.UtcNow }, persist: true);
            Log.Information("WebcamQuickRecalWindow: offset captured dx={Dx:F1} dy={Dy:F1} from {N} samples", dx, dy, _samples.Count);

            _completedOk = true;
            Play("chime3.mp3", 0.55f);   // CalibrationSoundService.QuickRecalComplete
            _txtStatus.Text = $"Done. Cursor nudged by ({dx:F0}, {dy:F0}) px.";
            await Task.Delay(FinishHoldMs);
            Close(true);
        }

        /// <summary>WPF WebcamQuickRecalWindow.xaml.cs:168-176: target the displayed dot at the
        /// current window's DIP center, even when saved monitor dimensions are stale.</summary>
        internal static (double Dx, double Dy) OffsetFor(global::Avalonia.Size window, IReadOnlyList<(double X, double Y)> samples)
        {
            var (mx, my) = GazeEngine.MedianAfterSaccadeSettle(samples, dropFirst: 10);
            return (window.Width / 2.0 - mx, window.Height / 2.0 - my);
        }

        /// <summary>CalibrationSoundService.Play: master volume on the ^1.5 curve, silence when missing.</summary>
        internal static void Play(string file, float multiplier)
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
