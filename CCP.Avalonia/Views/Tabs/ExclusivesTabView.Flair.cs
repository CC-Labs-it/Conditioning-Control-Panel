using System;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ExclusivesTabView
{
        /// <summary>WPF OnExclusiveCardHover: lift to 1.02, a bloom in the theme glow, the shared hover pop on the art.</summary>
        internal static void HoverCard(Control? card, bool on)
        {
            if (card is not Border border) return;
            Lift(border, on ? 1.02 : 1.0, 150);
            var tier = AmbientFxCanvas.Env.CurrentTier;
            // The bloom re-reads the glow colour every hover, so a mod switch never leaves an old colour behind.
            border.BoxShadow = on && AmbientFxCanvas.Env.AllowGlow(tier)
                ? Glow(AmbientFxCanvas.Env.GlowColor, 0.55, Math.Min(24, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)))
                : default;
            if (on) HoverPop.Enter(CardArt(card)); else HoverPop.Leave(CardArt(card));
        }

        /// <summary>WPF MotionFx.HoverLift / PressSquish: the card's own scale, quadratic ease-out; snapped at Off.</summary>
        internal static void Lift(Control card, double to, int ms)
        {
            if (card.RenderTransform is not ScaleTransform scale) return;
            scale.Transitions = AmbientFxCanvas.Env.AllowTransitions
                ? new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = TimeSpan.FromMilliseconds(ms), Easing = new QuadraticEaseOut() },
                }
                : null;
            scale.ScaleX = scale.ScaleY = to;
        }

}
