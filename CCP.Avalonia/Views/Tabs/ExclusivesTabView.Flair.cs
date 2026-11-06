using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Services.UI;

namespace ConditioningControlPanel.Avalonia.Views.Tabs
{
    public partial class ExclusivesTabView
    {
        internal Action<TimeSpan, Action> VaultClickDelay { get; set; } =
            (delay, action) => DispatcherTimer.RunOnce(action, delay);

        // WPF MainWindow.Exclusives.Flair.cs:242: delay the door only when a burst is visible.
        internal void OnVaultCardClicked(Control card, ExclusiveFeature feature, Action open)
        {
            var at = card.TranslatePoint(new Point(card.Bounds.Width / 2, card.Bounds.Height / 2), _ambientFx);
            if (!IsVisible || !AmbientFxCanvas.Env.AllowFeedbackParticles || card.Bounds.Width <= 0 ||   // a click: kept on the CPU path (fix3 pop 1b)
                _ambientFx.Bounds.Width <= 1 || _ambientFx.Bounds.Height <= 1 || at == null)
            {
                open();
                return;
            }

            var color = PremiumShelfOrder.GroupOf(feature.Key, feature.Tier) switch
            {
                PremiumGroup.Basic => Color.FromRgb(0xFF, 0xC8, 0x5A),
                PremiumGroup.Prime => Color.FromRgb(0x6F, 0xE8, 0xFF),
                _ => Color.FromRgb(0xB7, 0x9C, 0xFF),
            };
            _ambientFx.Burst(at.Value.X, at.Value.Y, color, 90);
            var scale = card.RenderTransform as ScaleTransform;
            if (scale == null && card.RenderTransform is TransformGroup group)
                foreach (var transform in group.Children)
                    if (transform is ScaleTransform existing) { scale = existing; break; }
            if (scale == null && (card.RenderTransform == null || card.RenderTransform.Value.IsIdentity))
            {
                card.RenderTransformOrigin = RelativePoint.Center;
                card.RenderTransform = scale = new ScaleTransform(1, 1);
            }
            if (scale != null)
            {
                var duration = TimeSpan.FromMilliseconds(InteractionPressMotion.DurationMs);
                // The hover lift (CardHover) leaves its own 150 ms transitions on the same transform;
                // the press runs at the WPF press time (MotionFx.cs:88, 80 ms), so it sets its own.
                scale.Transitions = AmbientFxCanvas.Env.AllowTransitions ? new Transitions
                {
                    new DoubleTransition { Property = ScaleTransform.ScaleXProperty, Duration = duration, Easing = new QuadraticEaseOut() },
                    new DoubleTransition { Property = ScaleTransform.ScaleYProperty, Duration = duration, Easing = new QuadraticEaseOut() },
                } : null;
                scale.ScaleX = scale.ScaleY = InteractionPressMotion.ScaleFor(true);
            }
            VaultClickDelay(TimeSpan.FromMilliseconds(140), () =>
            {
                if (scale != null) scale.ScaleX = scale.ScaleY = InteractionPressMotion.ScaleFor(false);
                open();
            });
        }
    }
}
