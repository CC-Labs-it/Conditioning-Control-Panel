// PORTED from ConditioningControlPanel/Controls/NavRail/NavGlow.cs (213aff12e, ed48b817c): the one
// "look here" glow of the nav rework - a rounded ring plus a soft halo in the section accent, on the
// adorner layer over the target, so it shows on any element and never fights the element's own
// Effect. Full = 600 ms sheen, 2 s hold, 400 ms fade; Reduced = a static 2 s hold; Off = nothing.
// A one-shot cue, so the performance tier does not gate it.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    internal static class NavGlow
    {
        public const int SheenMs = 600, HoldMs = 2000, FadeMs = 400;

        /// <summary>Test seam: the glow's elapsed time is read from this clock.</summary>
        internal static TimeProvider Time = TimeProvider.System;

        /// <summary>Total length at a motion level (0 = no glow). WPF NavGlow.TotalMs.</summary>
        public static int TotalMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => HoldMs,
            _ => SheenMs + HoldMs + FadeMs,
        };

        /// <summary>Glow <paramref name="target"/> once. False (with a Debug line) when it cannot.</summary>
        public static bool Once(Control? target, Color accent, MotionLevel? levelOverride = null, string? why = null)
        {
            var level = levelOverride ?? AmbientFxCanvas.Env.Level;
            if (target == null) { Log.Debug("NavGlow({Why}): no target", why); return false; }
            if (level == MotionLevel.Off) { Log.Debug("NavGlow({Why}): motion off", why); return false; }
            if (!target.IsEffectivelyVisible) { Log.Debug("NavGlow({Why}): {T} hidden", why, target.Name); return false; }
            if (AdornerLayer.GetAdornerLayer(target) is not { } layer)
            {
                Log.Debug("NavGlow({Why}): no adorner layer on {T}", why, target.Name);
                return false;
            }
            var ring = new NavGlowRing(accent) { Opacity = 0 };
            AdornerLayer.SetAdornedElement(ring, target);
            AdornerLayer.SetIsClipEnabled(ring, false);   // the halo spills past the target
            layer.Children.Add(ring);
            ring.Run(target, level);
            Log.Debug("NavGlow({Why}): on {T}", why, target.Name);
            return true;
        }
    }

    /// <summary>The ring and its four fading halo strokes (WPF GlowAdorner.OnRender).</summary>
    internal sealed class NavGlowRing : Control
    {
        /// <summary>True while its clock rides the window's frames (tests).</summary>
        internal bool OnFrameBeat => _clock is { IsEnabled: true, IsFrameLocked: true };

        private readonly Pen _ring;
        private readonly Pen[] _halo = new Pen[4];
        private readonly IBrush _fill;
        private FrameClock? _clock;
        private Action? _unhook;
        private MotionLevel _level;
        private long _start;
        private bool _gone;

        internal NavGlowRing(Color accent)
        {
            IsHitTestVisible = false;
            _ring = new Pen(new SolidColorBrush(accent), 2.5);
            for (int i = 0; i < _halo.Length; i++)
                _halo[i] = new Pen(new SolidColorBrush(Color.FromArgb((byte)(90 - i * 20), accent.R, accent.G, accent.B)), 3);
            _fill = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B));
        }

        internal void Run(Control target, MotionLevel level)
        {
            // An adorner outlives its element's collapse at the last laid-out spot (the lilac ring
            // left over the dashboard, WPF polish 12): the glow leaves with its target.
            void Gone(object? s, AvaloniaPropertyChangedEventArgs e)
            {
                if (e.Property == IsVisibleProperty && e.NewValue is false) Remove();
            }
            void Detached(object? s, VisualTreeAttachmentEventArgs e) => Remove();
            target.PropertyChanged += Gone;
            target.DetachedFromVisualTree += Detached;
            _unhook = () => { target.PropertyChanged -= Gone; target.DetachedFromVisualTree -= Detached; };
            _level = level;
            _start = NavGlow.Time.GetTimestamp();
            Tick();
            if (_gone) return;
            // Elapsed time on NavGlow.Time, not Avalonia's animation clock (real time, no seam):
            // a slow frame cannot stretch it (P40) and a test steps it (P08). Ticks on the window's
            // shared frame beat at 60 fps (WPF default rate), not a private timer.
            _clock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 60) };
            _clock.Tick += (_, _) => Tick();
            _clock.Start();
        }

        /// <summary>One frame: opacity at the elapsed time, or leave once the glow is over.</summary>
        internal void Tick()
        {
            if (_gone) return;
            double ms = NavGlow.Time.GetElapsedTime(_start).TotalMilliseconds;
            if (ms >= NavGlow.TotalMs(_level)) { Remove(); return; }
            Opacity = OpacityAt(_level, ms);
        }

        /// <summary>WPF's cues: Full = 0 -> 1 over half the sheen, -> 0.8 at its end, hold, fade to 0;
        /// Reduced = a static 0.85. Linear between cues (WPF eases the sheen with SineEase).</summary>
        internal static double OpacityAt(MotionLevel level, double ms)
        {
            if (level != MotionLevel.Full) return 0.85;
            const double half = NavGlow.SheenMs / 2.0;
            if (ms < half) return ms / half;
            if (ms < NavGlow.SheenMs) return 1 - 0.2 * (ms - half) / half;
            if (ms < NavGlow.SheenMs + NavGlow.HoldMs) return 0.8;
            return Math.Max(0, 0.8 * (1 - (ms - NavGlow.SheenMs - NavGlow.HoldMs) / NavGlow.FadeMs));
        }

        internal void Remove()
        {
            if (_gone) return;
            _gone = true;
            _clock?.Stop();
            _unhook?.Invoke();
            (Parent as Panel)?.Children.Remove(this);
        }

        public override void Render(DrawingContext dc)
        {
            var size = Bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            var r = new Rect(-3, -3, size.Width + 6, size.Height + 6);
            double radius = Math.Min(14, r.Height / 2);
            for (int i = _halo.Length - 1; i >= 0; i--)
            {
                double d = 3 + i * 3;
                dc.DrawRectangle(null, _halo[i], r.Inflate(d), radius + d, radius + d);
            }
            dc.DrawRectangle(_fill, _ring, r, radius, radius);
        }
    }
}
