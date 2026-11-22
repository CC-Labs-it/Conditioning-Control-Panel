using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;

namespace ConditioningControlPanel.Views.Controls
{
    /// <summary>
    /// The little "go" disc a quest card shows on hover when a click opens its feature (staff ask,
    /// 2026-11-18). Shared by the three daily seats and the weekly card so they move the same way.
    ///
    /// <para>IN and OUT: the disc fades up while sliding a few pixels right, and fades back out
    /// still moving right, as if the click had already gone through. Motion Off snaps it; Gentle visuals lengthens both legs to the rise floor.
    /// The hint is never hit-testable, so it cannot steal the card's click.</para>
    /// </summary>
    internal static class QuestGoHint
    {
        internal const int InMs = 140;
        internal const int OutMs = 120;
        internal const double ArrowTravelPx = 4;

        /// <summary>Show or hide <paramref name="hint"/>. Safe to call repeatedly.</summary>
        internal static void Set(FrameworkElement? hint, bool on)
        {
            if (hint == null) return;
            try
            {
                var slide = hint.RenderTransform as TranslateTransform;
                if (slide == null || slide.IsFrozen)
                {
                    slide = new TranslateTransform();
                    hint.RenderTransform = slide;
                }

                if (!MotionFx.AllowTransitions)
                {
                    hint.BeginAnimation(UIElement.OpacityProperty, null);
                    slide.BeginAnimation(TranslateTransform.XProperty, null);
                    hint.Opacity = on ? 1 : 0;
                    slide.X = 0;
                    hint.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                    return;
                }

                if (on)
                {
                    hint.Visibility = Visibility.Visible;
                    var ms = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(InMs));
                    var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                    hint.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(1, ms) { EasingFunction = ease });
                    slide.BeginAnimation(TranslateTransform.XProperty,
                        new DoubleAnimation(-ArrowTravelPx, 0, ms) { EasingFunction = ease });
                }
                else
                {
                    if (hint.Visibility != Visibility.Visible) return;
                    var ms = TimeSpan.FromMilliseconds(GentleVisuals.RiseMs(OutMs));
                    var fade = new DoubleAnimation(0, ms);
                    // Collapse only once it has actually faded: a hover back in replaces this clock
                    // and the disc is already climbing again.
                    fade.Completed += (_, _) =>
                    {
                        try { if (hint.Opacity <= 0.01) hint.Visibility = Visibility.Collapsed; } catch { }
                    };
                    hint.BeginAnimation(UIElement.OpacityProperty, fade);
                    slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(ArrowTravelPx, ms));
                }
            }
            catch (Exception ex) { App.Logger?.Debug("QuestGoHint: {E}", ex.Message); }
        }
    }
}
