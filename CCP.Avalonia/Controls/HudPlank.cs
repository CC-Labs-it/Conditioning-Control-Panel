using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>How a HUD button stands on the sheet (WPF Controls/Depth/HudPlank.cs:17).</summary>
    public enum HudPlankKind
    {
        /// <summary>The style draws exactly what it drew before the depth pass.</summary>
        None = 0,
        /// <summary>An ordinary raised plank: DepthRaisedBevel + DepthRaisedSheen, RaisedPx drop.</summary>
        Plank = 1,
        /// <summary>START only: DepthPlankBevel + DepthPlankSheen, a StartPx drop.</summary>
        Chunky = 2,
    }

    /// <summary>
    /// Port of WPF Controls/Depth/HudPlank.cs: the press for the START row planks (favourite star,
    /// START, the options caret, Save, Exit). Opt-in through <c>fx:HudPlank.Kind</c>, so every other
    /// PinkButton / SecondaryButton draws as before. The template names <c>DepthFace</c> (the face
    /// that travels: hover lifts, press drops, release springs back through a 1px overshoot) and
    /// <c>DepthDrop</c> (the drop band under it, scaled to ShadowFor). Motion Off jumps.
    /// The visibility of the depth parts is the theme's job (Styles.xaml Kind selectors), as in WPF.
    /// </summary>
    public static class HudPlank
    {
        public static readonly AttachedProperty<HudPlankKind> KindProperty =
            AvaloniaProperty.RegisterAttached<Button, HudPlankKind>("Kind", typeof(HudPlank));

        public static HudPlankKind GetKind(Button b) => b.GetValue(KindProperty);
        public static void SetKind(Button b, HudPlankKind v) => b.SetValue(KindProperty, v);

        static HudPlank() => KindProperty.Changed.AddClassHandler<Button>(OnKindChanged);

        // ---- pure numbers (WPF HudPlank.cs:52-73, DepthLaw in Core) ------------------------------

        /// <summary>The drop band's authored height: its longest shadow (hovered).</summary>
        internal static double DropBaseHeight(HudPlankKind kind) =>
            (kind == HudPlankKind.Chunky ? DepthLaw.StartPx : DepthLaw.RaisedPx) + DepthLaw.HoverLiftPx;

        /// <summary>The drop length for a state: ShadowFor, stretched to StartPx for the chunky plank.</summary>
        internal static double DropLength(HudPlankKind kind, bool enabled, bool pressed, bool hovered)
        {
            if (kind == HudPlankKind.None) return 0;
            double len = DepthLaw.ShadowFor(enabled, pressed, active: false, hovered);
            if (len <= 0) return 0;
            return kind == HudPlankKind.Chunky ? len - DepthLaw.RaisedPx + DepthLaw.StartPx : len;
        }

        /// <summary>The face's travel: a HUD plank is never "lit", so only hover / press move it.</summary>
        internal static double Travel(bool enabled, bool pressed, bool hovered) =>
            DepthLaw.TravelFor(enabled, pressed, active: false, hovered);

        /// <summary>Which clock a change rides: press is fast, release springs, hover glides.</summary>
        internal static int DurationFor(bool pressed, bool releasing, MotionLevel level) =>
            DepthLaw.Ms(pressed ? DepthLaw.PressMs : releasing ? DepthLaw.ReleaseMs : DepthLaw.HoverMs, level);

        // ---- wiring -------------------------------------------------------------------------------

        private static void OnKindChanged(Button b, AvaloniaPropertyChangedEventArgs e)
        {
            var was = e.GetOldValue<HudPlankKind>();
            var now = e.GetNewValue<HudPlankKind>();
            if (was == HudPlankKind.None && now != HudPlankKind.None)
            {
                b.PropertyChanged += OnProperty;
                b.TemplateApplied += OnTemplate;
            }
            else if (was != HudPlankKind.None && now == HudPlankKind.None)
            {
                b.PropertyChanged -= OnProperty;
                b.TemplateApplied -= OnTemplate;
            }
            Update(b, releasing: false, animate: false);
        }

        private static void OnTemplate(object? s, global::Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
        {
            if (s is Button b) Update(b, releasing: false, animate: false);
        }

        private static void OnProperty(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            if (s is not Button b) return;
            if (e.Property == Button.IsPressedProperty)
                Update(b, releasing: e.GetOldValue<bool>() && !e.GetNewValue<bool>(), animate: true);
            else if (e.Property == InputElement.IsPointerOverProperty || e.Property == InputElement.IsEffectivelyEnabledProperty)
                Update(b, releasing: false, animate: true);
        }

        internal static MotionLevel Level
        {
            get
            {
                try
                {
                    return Platform.UiMotionPreference.ResolveLevel(CoreSettings.Current.MotionLevel,
                        Platform.UiMotionPreference.AnimationsEnabled, CoreSettings.Current.GentleVisuals);
                }
                catch { return MotionLevel.Full; }   // settings not up yet: motion on, as WPF
            }
        }

        private static Control? Part(Button b, string name) =>
            b.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Name == name && c.TemplatedParent == b);

        internal static void Update(Button b, bool releasing, bool animate)
        {
            var kind = GetKind(b);
            if (kind == HudPlankKind.None) return;
            bool enabled = b.IsEffectivelyEnabled, pressed = b.IsPressed, hovered = b.IsPointerOver;
            int ms = animate ? DurationFor(pressed, releasing, Level) : 0;

            if (Part(b, "DepthFace") is { } face)
            {
                if (face.RenderTransform is not TranslateTransform slide)
                    face.RenderTransform = slide = new TranslateTransform();
                double from = slide.Y, to = Travel(enabled, pressed, hovered);
                // WPF: release springs past rest by the overshoot (upward) at 60%, then settles;
                // press and hover glide with QuadraticEase EaseOut. A fresh set replaces the running one,
                // which would snap back to the old target, so pin the mid-glide value first.
                slide.Transitions = null; slide.Y = from;
                slide.Transitions = ms <= 0 || Math.Abs(to - from) < 0.01 ? null : new Transitions
                {
                    new DoubleTransition
                    {
                        Property = TranslateTransform.YProperty,
                        Duration = TimeSpan.FromMilliseconds(ms),
                        Easing = releasing
                            ? new ReleaseSpring((to - DepthLaw.ReleaseOvershootPx - from) / (to - from))
                            : new QuadraticEaseOut(),
                    },
                };
                slide.Y = to;
            }

            if (Part(b, "DepthDrop") is { } drop)
            {
                double baseH = DropBaseHeight(kind);
                if (!(Math.Abs(drop.Height - baseH) <= 0.01))
                {
                    // The band hangs entirely below the plate: its top meets the plate's foot.
                    drop.Height = baseH;
                    drop.Margin = new Thickness(drop.Margin.Left, 0, drop.Margin.Right, -baseH);
                }
                drop.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
                if (drop.RenderTransform is not ScaleTransform scale)
                    drop.RenderTransform = scale = new ScaleTransform();
                double scaleFrom = scale.ScaleY;
                scale.Transitions = null; scale.ScaleY = scaleFrom;
                scale.Transitions = ms <= 0 ? null : new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(ms) },
                };
                scale.ScaleY = DropLength(kind, enabled, pressed, hovered) / baseH;
            }
        }

        /// <summary>WPF's release keyframes as one easing: QuadraticEase EaseOut to <c>peak</c> at 60%,
        /// then QuadraticEase EaseInOut to 1 (HudPlank.cs:146-151).</summary>
        internal sealed class ReleaseSpring(double peak) : Easing
        {
            public override double Ease(double t)
            {
                if (t < 0.6) { double u = 1 - t / 0.6; return peak * (1 - u * u); }
                double v = (t - 0.6) / 0.4;
                double io = v < 0.5 ? 2 * v * v : 1 - 2 * (1 - v) * (1 - v);
                return peak + (1 - peak) * io;
            }
        }
    }
}
