using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Media.Transformation;

namespace ConditioningControlPanel.Avalonia.Theme;

/// <summary>WPF Controls.xaml:257-267,295-303: SnapshotAndReplace reverses from the visible
/// position. Keep one transition per knob: replacing its collection cancels the animated value.</summary>
public static class ToggleKnob
{
    public static readonly AttachedProperty<bool?> IsOnProperty =
        AvaloniaProperty.RegisterAttached<Border, bool?>("IsOn", typeof(ToggleKnob));

    private static readonly AttachedProperty<TransformOperationsTransition?> TransitionProperty =
        AvaloniaProperty.RegisterAttached<Border, TransformOperationsTransition?>("Transition", typeof(ToggleKnob));

    public static bool? GetIsOn(Border thumb) => thumb.GetValue(IsOnProperty);
    public static void SetIsOn(Border thumb, bool? value) => thumb.SetValue(IsOnProperty, value);

    static ToggleKnob()
    {
        IsOnProperty.Changed.AddClassHandler<Border>((thumb, _) =>
        {
            var transition = thumb.GetValue(TransitionProperty);
            if (transition is null)
            {
                transition = new TransformOperationsTransition { Property = Visual.RenderTransformProperty };
                thumb.SetValue(TransitionProperty, transition);
                thumb.Transitions = new Transitions { transition };
            }
            var on = GetIsOn(thumb) == true;
            // Change timing before the target, without disposing the in-flight transition.
            transition.Duration = TimeSpan.FromMilliseconds(on ? 340 : 300);
            transition.Easing = new BackEaseOutAmplitude { Amplitude = on ? 0.6 : 0.35 };
            thumb.RenderTransform = TransformOperations.Parse(on ? "translateX(22px)" : "translateX(0px)");
        });
    }
}
