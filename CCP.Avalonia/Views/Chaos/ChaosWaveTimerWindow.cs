using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosWaveTimerOverlay.cs: the Pocket Watch charm's
    /// wave countdown, a small click-through pill pinned to the top-right corner of the work area
    /// with the wave, the time left in it and the live score. Built in code like the original; the
    /// text and the warning states come from Core <see cref="ChaosWaveTimerReadout"/>.
    /// <para>Win32 ex-styles map as on the other chaos overlays: WS_EX_TOOLWINDOW -> ShowInTaskbar
    /// false, WS_EX_NOACTIVATE -> ShowActivated false, WS_EX_TRANSPARENT ->
    /// <see cref="X11Overlay.SetClickThrough"/>. UI thread only: <see cref="ChaosRunHost"/> calls it
    /// from its run tick and closes it on every teardown, including panic (PanicSurfaces "chaos").</para>
    /// <para>Draft clearing remains pending the run host draft lifecycle.</para>
    /// </summary>
    internal sealed class ChaosWaveTimerWindow : Window
    {
        private static readonly IBrush WaveBrush = new SolidColorBrush(Color.FromRgb(0xE8, 0x9B, 0xC8));
        private static readonly IBrush UrgentBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0x5A, 0x5A));
        private static readonly IBrush ScoreBrush = new SolidColorBrush(Color.FromRgb(0xFF, 0xD7, 0x00));

        internal static ChaosWaveTimerWindow? Active { get; private set; }

        /// <summary>Headless has no X server: tests that drive the pill skip the compositor check.</summary>
        internal static bool SkipPlatformChecksForTests;

        // Set when the platform refused click-through or transparency: no re-creation every tick
        // until the run ends (CloseActive).
        private static bool _refused;

        /// <summary>WPF EnsureCreated: birth the (empty, hidden-pill) window at run start.</summary>
        internal static void EnsureCreated()
        {
            if (Active != null || _refused) return;
            try
            {
                var w = new ChaosWaveTimerWindow(live: true);
                // WPF ChaosWindowZ.cs:37-56: above fullscreen video without activation.
                // Bypass the WM before mapping, otherwise KWin can keep fullscreen above us.
                if (w.Topmost && !X11Overlay.SetOverrideRedirect(w, passive: true) && !SkipPlatformChecksForTests)
                {
                    _refused = true;
                    w.Close();
                    return;
                }
                Active = w;
                w.Show();
            }
            catch (Exception ex) { Log.Debug("ChaosWaveTimer.EnsureCreated: {E}", ex.Message); }
        }

        /// <summary>WPF Update: refresh the readout, creating the window if needed.</summary>
        internal static void Update(int wave, int waveCount, double secLeftInWave, double score)
        {
            EnsureCreated();
            Active?.SetText(ChaosWaveTimerReadout.For(wave, waveCount, secLeftInWave, score));
        }

        /// <summary>WPF CloseActive: instant teardown (run end, panic, shutdown). Safe when idle.</summary>
        internal static void CloseActive()
        {
            var w = Active;
            Active = null;
            _refused = false;   // the next run may try again
            try { w?.Close(); } catch (Exception ex) { Log.Debug("ChaosWaveTimer close: {E}", ex.Message); }
        }

        // Native stacking is kept behind the existing platform seam; headless tests observe calls.
        internal static Action<Window> RaiseWindow = w => X11Overlay.Raise(w);

        /// <summary>WPF ChaosModeService.cs:745-750,766 and ChaosWaveTimerOverlay.cs:74.
        /// Re-stack without activation, only during a descending, pinned run.</summary>
        internal static void RaiseActive()
        {
            if (!ChaosRunHost.IsDescending || !CoreSettings.Current.ChaosPinOnTop) return;
            if (Active is { IsVisible: true } w) RaiseWindow(w);
        }

        internal readonly Border Pill;
        internal readonly TextBlock WaveText, ClockText, ScoreText;
        private readonly FrameClock _blink;
        private DateTimeOffset _blinkStart;
        internal TimeProvider Time { get; set; } = TimeProvider.System;
        internal bool Urgent { get; private set; }
        internal bool FinalRush { get; private set; }

        /// <summary>Render constructor (--render-all): a sample urgent readout.</summary>
        internal ChaosWaveTimerWindow() : this(live: false) =>
            SetText(ChaosWaveTimerReadout.For(3, 5, 7.4, 12345));   // urgent, not blinking

        private ChaosWaveTimerWindow(bool live)
        {
            WindowDecorations = global::Avalonia.Controls.WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current.ChaosPinOnTop;   // WPF ChaosWindowZ.BornTopmost
            ShowInTaskbar = false;
            ShowActivated = false;
            Focusable = false;
            IsHitTestVisible = false;
            CanResize = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            SizeToContent = SizeToContent.WidthAndHeight;
            Title = "Wave timer";

            WaveText = new TextBlock
            {
                Foreground = WaveBrush, FontSize = 13, FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0),
            };
            ClockText = new TextBlock
            {
                Foreground = Brushes.White, FontSize = 17, FontWeight = FontWeight.Bold,
                VerticalAlignment = VerticalAlignment.Center,
            };
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(WaveText);
            row.Children.Add(ClockText);
            ScoreText = new TextBlock
            {
                Foreground = ScoreBrush, FontSize = 13, FontWeight = FontWeight.Bold,
                HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 2, 0, 0),
            };
            var stack = new StackPanel();
            stack.Children.Add(row);
            stack.Children.Add(ScoreText);

            Pill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(170, 0x12, 0x0E, 0x1E)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(160, 0xE8, 0x43, 0x93)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(14, 5, 14, 5),
                IsVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right,   // SizeToContent makes these moot live;
                VerticalAlignment = VerticalAlignment.Top,         // a fixed-size render frame keeps the pill's shape
                Child = stack,
            };
            Content = Pill;

            _blink = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(33) };
            _blink.Tick += (_, _) => BlinkFrame();
            Closed += (_, _) => { _blink.Stop(); if (ReferenceEquals(Active, this)) Active = null; };

            if (!live) return;
            Opened += (_, _) =>
            {
                // No compositor / no input shape: an opaque box that eats clicks would sit top-right
                // (BlinkTrainerSession's guard). Refuse it for the rest of the run.
                if ((!X11Overlay.SetClickThrough(this, true) || ActualTransparencyLevel == WindowTransparencyLevel.None)
                    && !SkipPlatformChecksForTests)
                {
                    Log.Warning("ChaosWaveTimer: overlay refused (no click-through or no transparency)");
                    _refused = true;
                    if (ReferenceEquals(Active, this)) Active = null;
                    Close();
                    return;
                }
                PlaceTopRight();
            };
            SizeChanged += (_, _) => PlaceTopRight();
        }

        /// <summary>WPF: Top = work area top + 10, Left = right edge - width - 14 (DIPs).</summary>
        private void PlaceTopRight()
        {
            try
            {
                var screen = Screens?.Primary;
                if (screen is null) return;
                double s = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                var wa = screen.WorkingArea;
                Position = new PixelPoint((int)(wa.Right - (Bounds.Width + 14) * s), (int)(wa.Y + 10 * s));
            }
            catch (Exception ex) { Log.Debug("ChaosWaveTimer place: {E}", ex.Message); }
        }

        internal void SetText(ChaosWaveTimerReadout r)
        {
            Pill.IsVisible = true;
            WaveText.Text = r.Wave;
            ClockText.Text = r.Clock;
            ScoreText.Text = r.Score;

            // The last ten seconds of a wave run hot (red clock); calm white otherwise.
            if (r.Urgent != Urgent)
            {
                Urgent = r.Urgent;
                ClockText.Foreground = Urgent ? UrgentBrush : Brushes.White;
            }

            // The run's last ten seconds: the red clock also blinks (P82: a frame clock, not an
            // infinite Animation).
            if (r.FinalRush != FinalRush)
            {
                FinalRush = r.FinalRush;
                if (FinalRush) { _blinkStart = Time.GetUtcNow(); _blink.Start(); }
                else { _blink.Stop(); ClockText.Opacity = 1.0; }
            }
        }

        internal void BlinkFrame()
        {
            if (!FinalRush) return;
            ClockText.Opacity = ChaosWaveTimerReadout.BlinkOpacity(Time.GetUtcNow() - _blinkStart);
        }
    }
}
