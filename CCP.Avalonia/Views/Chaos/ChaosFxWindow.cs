using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The Avalonia twin of WPF <c>ChaosFxWindow</c> (ConditioningControlPanel/Chaos/ChaosFxWindow.cs):
    /// a full-screen, click-through colour vignette on the PRIMARY screen (WPF sizes it to
    /// <c>SystemParameters.PrimaryScreen*</c> at 0,0, so one monitor, as there). Three layers, back to
    /// front: the warm heat tint, the held edge glow, the impact pulse. Same brushes and stops as WPF.
    ///
    /// <para>The opacity animations are <see cref="ChaosFxRamp"/>s from Core (WPF's key frames and
    /// durations), sampled on real elapsed time (<see cref="Clock"/>, P40) every composed frame (a
    /// <see cref="FrameClock"/>, WPF animations run at render rate) only while a ramp is moving. Code-only like <c>TintOverlayWindow</c>.</para>
    ///
    /// <para>Click-through is <c>X11Overlay.SetClickThrough</c> (WPF's <c>WS_EX_TRANSPARENT</c>);
    /// <see cref="Open"/> refuses the window, as <c>PinkFilterOverlay.Accept</c> does, when the
    /// platform cannot make it click-through or denies transparency - a topmost full-screen window
    /// that swallows clicks or paints opaque would lock the desktop.</para>
    /// </summary>
    internal sealed class ChaosFxWindow : Window
    {
        internal static TimeProvider Clock = TimeProvider.System;

        /// <summary>The two safety checks <see cref="Open"/> runs after Show. Tests replace it:
        /// headless has no X11 window, so the real check always refuses there.</summary>
        internal static Func<Window, bool> Accept = DefaultAccept;

        private sealed class Layer
        {
            public readonly Border Border = new() { Opacity = 0, IsHitTestVisible = false };
            public ChaosFxRamp? Ramp;
            public long Start;
        }

        private readonly Layer _heat = new(), _edge = new(), _vignette = new();
        private readonly FrameClock _frames;

        internal Border HeatLayer => _heat.Border;
        internal Border EdgeLayer => _edge.Border;
        internal Border VignetteLayer => _vignette.Border;
        internal bool IsAnimating => _frames.IsEnabled;

        public ChaosFxWindow()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current.ChaosPinOnTop;   // WPF ChaosWindowZ.BornTopmost
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 640;
            Height = 400;
            var root = new Grid { IsHitTestVisible = false };
            root.Children.Add(_heat.Border);       // warm temperature tint sits at the very back
            root.Children.Add(_edge.Border);       // held glow sits under the impact pulses
            root.Children.Add(_vignette.Border);
            Content = root;
            _frames = new FrameClock(this) { Interval = TimeSpan.Zero };   // every frame, like a WPF animation
            _frames.Tick += (_, _) => Frame();
            Closed += (_, _) => _frames.Stop();
        }

        /// <summary>WPF BeginRun (ChaosModeService.cs:500): one FX window per run. Null when the
        /// platform cannot show it safely (see the class notes).</summary>
        internal static ChaosFxWindow? Open()
        {
            var w = new ChaosFxWindow();
            try
            {
                w.SizeToPrimaryScreen();
                w.Show();
                if (Accept(w)) return w;
            }
            catch (Exception ex) { Log.Debug("Chaos FX init: {E}", ex.Message); }
            try { w.Close(); } catch (Exception ex) { Log.Debug("Chaos FX close: {E}", ex.Message); }
            return null;
        }

        private static bool DefaultAccept(Window w)
        {
            if (!X11Overlay.SetClickThrough(w, true))
            {
                Log.Warning("Chaos FX: this platform cannot make the vignette click-through, so it is not shown");
                return false;
            }
            if (w.ActualTransparencyLevel == WindowTransparencyLevel.None)
            {
                Log.Warning("Chaos FX: no per-pixel transparency (no compositor), so the vignette is not shown");
                return false;
            }
            return true;
        }

        private void SizeToPrimaryScreen()
        {
            var screen = Screens?.Primary ?? Screens?.All?.FirstOrDefault();
            if (screen is null) return;
            var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
            Position = screen.Bounds.Position;          // physical
            Width = screen.Bounds.Width / scale;        // DIPs
            Height = screen.Bounds.Height / scale;
        }

        /// <summary>WPF BeginEdgeHold: a sustained coloured edge glow held until <see cref="EndEdgeHold"/>.</summary>
        public void BeginEdgeHold(Color color, double strength)
        {
            _edge.Border.Background = Edge(color, 0.95, 1.05, 0.55);
            Run(_edge, ChaosFxEnvelope.EdgeHoldIn(_edge.Border.Opacity, strength));
        }

        public void EndEdgeHold() => Run(_edge, ChaosFxEnvelope.EdgeHoldOut(_edge.Border.Opacity));

        /// <summary>WPF SetHeatTint: the brush is built once per window (first colour wins, as there).</summary>
        public void SetHeatTint(Color color, double opacity)
        {
            _heat.Border.Background ??= Edge(color, 1.0, 1.1, 0.40);
            Run(_heat, ChaosFxEnvelope.HeatIn(_heat.Border.Opacity, opacity));
        }

        public void EndHeatTint() => Run(_heat, ChaosFxEnvelope.HeatOut(_heat.Border.Opacity));

        /// <summary>WPF FreezeBurst: the icy full-screen frost flash on the pulse layer.</summary>
        public void FreezeBurst(Color color)
        {
            _vignette.Border.Background = Radial(1.1, 1.2,
                (Color.FromArgb(235, 255, 255, 255), 0.0),
                (Color.FromArgb(170, color.R, color.G, color.B), 0.45),
                (Color.FromArgb(90, color.R, color.G, color.B), 1.0));
            Run(_vignette, ChaosFxEnvelope.FreezeBurst(_vignette.Border.Opacity));
        }

        /// <summary>WPF Pulse: flash a coloured edge vignette.</summary>
        public void Pulse(Color color, double strength)
        {
            _vignette.Border.Background = Edge(color, 0.9, 1.0, 0.45);
            Run(_vignette, ChaosFxEnvelope.Pulse(_vignette.Border.Opacity, strength,
                ConditioningControlPanel.Services.Safety.GentleVisuals.RiseMs(40)));
        }

        private void Run(Layer layer, ChaosFxRamp ramp)
        {
            layer.Ramp = ramp;
            layer.Start = Clock.GetTimestamp();
            if (!_frames.IsEnabled) _frames.Start();
        }

        /// <summary>One frame: every moving layer to its ramp at real elapsed time; the timer stops
        /// once all of them have reached their hold value.</summary>
        internal void Frame()
        {
            var now = Clock.GetTimestamp();
            bool moving = Step(_heat, now) | Step(_edge, now) | Step(_vignette, now);
            if (!moving) _frames.Stop();
        }

        private static bool Step(Layer layer, long now)
        {
            if (layer.Ramp is not { } ramp) return false;
            var ms = Clock.GetElapsedTime(layer.Start, now).TotalMilliseconds;
            layer.Border.Opacity = ramp.Sample(ms);
            if (ms < ramp.DurationMs) return true;
            layer.Ramp = null;
            return false;
        }

        /// <summary>Transparent centre out to <paramref name="clearTo"/>, full colour at the rim.</summary>
        private static IBrush Edge(Color c, double rx, double ry, double clearTo) => Radial(rx, ry,
            (Color.FromArgb(0, c.R, c.G, c.B), 0.0),
            (Color.FromArgb(0, c.R, c.G, c.B), clearTo),
            (Color.FromArgb(255, c.R, c.G, c.B), 1.0));

        private static IBrush Radial(double rx, double ry, params (Color C, double At)[] stops)
        {
            var brush = new RadialGradientBrush
            {
                Center = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(rx, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(ry, RelativeUnit.Relative),
            };
            foreach (var (c, at) in stops) brush.GradientStops.Add(new GradientStop(c, at));
            return brush.ToImmutable();   // WPF Freeze()
        }
    }
}
