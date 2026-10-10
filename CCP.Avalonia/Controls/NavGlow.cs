// PORTED from ConditioningControlPanel/Controls/NavRail/NavGlow.cs (213aff12e, ed48b817c): the one
// "look here" glow of the nav rework - a rounded ring plus a soft halo in the section accent, on the
// adorner layer over the target, so it shows on any element and never fights the element's own
// Effect. Full = 600 ms sheen, 2 s hold, 400 ms fade; Reduced = a static 2 s hold; Off = nothing.
// A one-shot cue, so the performance tier does not gate it.
using System;
using System.Threading;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Styling;
using ConditioningControlPanel.Models;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Controls
{
    internal static class NavGlow
    {
        public const int SheenMs = 600, HoldMs = 2000, FadeMs = 400;

        /// <summary>Total length at a motion level (0 = no glow). WPF NavGlow.TotalMs.</summary>
        public static int TotalMs(MotionLevel level) => level switch
        {
            MotionLevel.Off => 0,
            MotionLevel.Reduced => HoldMs,
            _ => SheenMs + HoldMs + FadeMs,
        };

        /// <summary>The section accent (Core NavStripTable) as a colour.</summary>
        public static Color Accent(string? section) => Color.FromUInt32(0xFF000000 | ConditioningControlPanel.Services.UI.NavStripTable.AccentRgb(section));

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
        private readonly Pen _ring;
        private readonly Pen[] _halo = new Pen[4];
        private readonly IBrush _fill;
        private readonly CancellationTokenSource _run = new();
        private bool _gone;

        internal NavGlowRing(Color accent)
        {
            IsHitTestVisible = false;
            _ring = new Pen(new SolidColorBrush(accent), 2.5);
            for (int i = 0; i < _halo.Length; i++)
                _halo[i] = new Pen(new SolidColorBrush(Color.FromArgb((byte)(90 - i * 20), accent.R, accent.G, accent.B)), 3);
            _fill = new SolidColorBrush(Color.FromArgb(0x26, accent.R, accent.G, accent.B));
        }

        internal async void Run(Control target, MotionLevel level)
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
            var anim = new Animation { FillMode = FillMode.Forward };
            if (level == MotionLevel.Full)
            {
                double total = NavGlow.TotalMs(level);
                anim.Duration = TimeSpan.FromMilliseconds(total);
                anim.Children.Add(Key(0, 0));
                anim.Children.Add(Key(NavGlow.SheenMs / 2 / total, 1.0));
                anim.Children.Add(Key(NavGlow.SheenMs / total, 0.8));
                anim.Children.Add(Key((NavGlow.SheenMs + NavGlow.HoldMs) / total, 0.8));
                anim.Children.Add(Key(1, 0));
            }
            else
            {
                anim.Duration = TimeSpan.FromMilliseconds(NavGlow.HoldMs);
                anim.Children.Add(Key(0, 0.85));
                anim.Children.Add(Key(1, 0.85));
            }
            try { await anim.RunAsync(this, _run.Token); } catch (OperationCanceledException) { }
            target.PropertyChanged -= Gone;
            target.DetachedFromVisualTree -= Detached;
            Remove();
        }

        // Linear between cues (WPF eases the sheen with SineEase; Avalonia keyframes carry no per-frame easing).
        private static KeyFrame Key(double cue, double value) =>
            new() { Cue = new Cue(cue), Setters = { new Setter(OpacityProperty, value) } };

        internal void Remove()
        {
            if (_gone) return;
            _gone = true;
            _run.Cancel();
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
