using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Safety;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// Port of WPF <c>ConditioningControlPanel/Chaos/ChaosFxWindow.cs</c>, the <see cref="Pulse"/>
    /// half: a full-screen, click-through colour edge-vignette for Chaos impact juice (red =
    /// detonation, green = defuse, gold = combo, blue = shield save) that snaps up and fades fast.
    ///
    /// <para>Same brush (radial, transparent to 45 %, the colour at the rim, radius 0.9 x 1.0) and
    /// envelope (peak 0.22 + 0.5 x strength clamped 0.15-0.72; rise 40 ms floored by Gentle
    /// Visuals, then a 260 ms fade) as WPF. The envelope is computed per frame on a
    /// <see cref="FrameClock"/> from <see cref="Clock"/> (P82: no Animation on a brush), and the
    /// clock runs only while a pulse is fading (P07).</para>
    ///
    /// <para>Click-through is <c>X11Overlay.SetClickThrough</c> (WPF WS_EX_TRANSPARENT) on an
    /// override-redirect window over the primary screen (WPF PrimaryScreenWidth/Height). A platform
    /// without a compositor or that refuses either gets no vignette rather than an opaque
    /// full-screen window that eats clicks.</para>
    ///
    /// <para>ponytail: the freeze edge-hold, the freeze burst, the Heat tint and the re-raise over a
    /// mid-run video (WPF BeginEdgeHold / FreezeBurst / SetHeatTint / RaiseToTopmost) wait for the freeze pickup and Heat tint, which are not ported
    /// (chaos-payout-fallin-d-next).</para>
    /// </summary>
    internal sealed class ChaosFxWindow : Window
    {
        internal const int FADE_MS = 260;
        internal static TimeProvider Clock = TimeProvider.System;
        internal static bool SkipPlatformChecksForTests;

        private readonly Border _vignette;
        private readonly FrameClock _frames;
        private DateTimeOffset _pulseStart;
        private double _from, _peak;
        private int _riseMs;

        internal Border Vignette => _vignette;
        internal bool IsAnimating => _frames.IsEnabled;

        public ChaosFxWindow()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 1280;
            Height = 720;
            _vignette = new Border { Opacity = 0, IsHitTestVisible = false };
            Content = _vignette;
            _frames = new FrameClock(this) { Interval = TimeSpan.FromMilliseconds(16) };
            _frames.Tick += (_, _) => Step();
            if (RenderProof.Rendering)   // --render-all: a settled red detonation pulse at its peak
            {
                _vignette.Background = Rim(Color.FromRgb(255, 50, 50));
                _vignette.Opacity = PeakFor(0.6);
            }
        }

        /// <summary>WPF StartRun (:500) <c>new ChaosFxWindow().Show()</c>: null when the platform
        /// cannot give a composited click-through window.</summary>
        internal static ChaosFxWindow? TryCreate()
        {
            try
            {
                if (!SkipPlatformChecksForTests && (!X11Overlay.IsAvailable || !X11Overlay.IsCompositing))
                {
                    Log.Warning("ChaosFx: no composited click-through overlay on this platform, so no colour pulses");
                    return null;
                }
                var w = new ChaosFxWindow();
                if ((!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOverrideRedirect(w, w.ScreenBounds()))
                    && !SkipPlatformChecksForTests)
                {
                    Log.Warning("ChaosFx: the platform refused a click-through topmost window, so no colour pulses");
                    w.Close();
                    return null;
                }
                w.Show();
                return w;
            }
            catch (Exception ex)
            {
                Log.Debug("Chaos FX init: {E}", ex.Message);
                return null;
            }
        }

        /// <summary>WPF: Left = 0, Top = 0, PrimaryScreenWidth x PrimaryScreenHeight.</summary>
        private PixelRect ScreenBounds()
        {
            var screen = Screens?.Primary ?? Screens?.All.FirstOrDefault();
            return screen?.Bounds ?? new PixelRect(0, 0, 1280, 720);
        }

        /// <summary>WPF Pulse peak: 0.22 + strength x 0.5, clamped to 0.15-0.72.</summary>
        internal static double PeakFor(double strength) => Math.Clamp(0.22 + strength * 0.5, 0.15, 0.72);

        /// <summary>WPF Pulse's frozen RadialGradientBrush: clear centre, the colour at the rim.</summary>
        private static IBrush Rim(Color c) => new RadialGradientBrush
        {
            GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
            RadiusX = new RelativeScalar(0.9, RelativeUnit.Relative),
            RadiusY = new RelativeScalar(1.0, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0.0),
                new GradientStop(Color.FromArgb(0, c.R, c.G, c.B), 0.45),
                new GradientStop(Color.FromArgb(255, c.R, c.G, c.B), 1.0),
            },
        };

        /// <summary>Flash a coloured edge-vignette. <paramref name="strength"/> 0..1 scales peak opacity.
        /// A new pulse takes over from the current opacity, as WPF's BeginAnimation hand-off does.</summary>
        public void Pulse(Color color, double strength)
        {
            try
            {
                _vignette.Background = Rim(color);
                _from = _vignette.Opacity;
                _peak = PeakFor(strength);
                _riseMs = GentleVisuals.RiseMs(40);
                _pulseStart = Clock.GetUtcNow();
                _frames.Start();
                Step();
            }
            catch (Exception ex) { Log.Debug("ChaosFx.Pulse: {E}", ex.Message); }
        }

        /// <summary>One frame of WPF's key frames: linear to the peak over the rise, linear to 0 over 260 ms.</summary>
        internal void Step()
        {
            double t = (Clock.GetUtcNow() - _pulseStart).TotalMilliseconds;
            if (t < _riseMs) _vignette.Opacity = _from + (_peak - _from) * (t / _riseMs);
            else if (t < _riseMs + FADE_MS) _vignette.Opacity = _peak * (1 - (t - _riseMs) / FADE_MS);
            else
            {
                _vignette.Opacity = 0;
                _frames.Stop();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _frames.Stop();
            base.OnClosed(e);
        }
    }
}
