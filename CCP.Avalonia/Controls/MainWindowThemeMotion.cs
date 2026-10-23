using System;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Transformation;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>WPF SmallPinkButton's release BackEase, amplitude 0.3 (Avalonia's built-in
/// BackEaseOut has no amplitude parameter). Used only for scale, never opacity.</summary>
public sealed class SmallButtonReleaseEase : Easing
{
    public override double Ease(double progress)
    {
        double x = 1 - progress;
        return 1 - (x * x * x - x * 0.3 * Math.Sin(Math.PI * x));
    }
}

/// <summary>The two fixed WPF shadow recipes and the ComboBox popup's one-shot slide.
/// BoxShadow avoids an Effect on card content; transforms are transitioned on the Visual.</summary>
public sealed class MainWindowThemeMotion : AvaloniaObject
{
    public static readonly AttachedProperty<Color> ComboGlowProperty =
        AvaloniaProperty.RegisterAttached<MainWindowThemeMotion, Border, Color>("ComboGlow");
    public static Color GetComboGlow(Border border) => border.GetValue(ComboGlowProperty);
    public static void SetComboGlow(Border border, Color value) => border.SetValue(ComboGlowProperty, value);
    public static readonly AttachedProperty<Color> CardGlowProperty =
        AvaloniaProperty.RegisterAttached<MainWindowThemeMotion, Border, Color>("CardGlow");
    public static Color GetCardGlow(Border border) => border.GetValue(CardGlowProperty);
    public static void SetCardGlow(Border border, Color value) => border.SetValue(CardGlowProperty, value);
    public static readonly AttachedProperty<bool> SlideOnLoadProperty =
        AvaloniaProperty.RegisterAttached<MainWindowThemeMotion, Border, bool>("SlideOnLoad");
    public static bool GetSlideOnLoad(Border border) => border.GetValue(SlideOnLoadProperty);
    public static void SetSlideOnLoad(Border border, bool value) => border.SetValue(SlideOnLoadProperty, value);

    static MainWindowThemeMotion()
    {
        ComboGlowProperty.Changed.AddClassHandler<Border>((b, _) => b.BoxShadow = Shadow(GetComboGlow(b), 10, 0.35));
        CardGlowProperty.Changed.AddClassHandler<Border>((b, _) => b.BoxShadow = Shadow(GetCardGlow(b), 15, 0.15));
        Control.LoadedEvent.AddClassHandler<Border>((b, _) =>
        {
            if (!GetSlideOnLoad(b)) return;
            b.Transitions = null;
            b.RenderTransform = TransformOperations.Parse(FormattableString.Invariant($"translate(0px,{-b.Bounds.Height}px)"));
            // WPF PopupAnimation.Slide's 150 ms reveal; one finite transition, no idle clock.
            b.Transitions = new Transitions { new TransformOperationsTransition
                { Property = Visual.RenderTransformProperty, Duration = TimeSpan.FromMilliseconds(150) } };
            b.RenderTransform = TransformOperations.Parse("translate(0px,0px)");
        });
        Control.UnloadedEvent.AddClassHandler<Border>((b, _) =>
        {
            if (!GetSlideOnLoad(b)) return;
            b.Transitions = null;
            b.RenderTransform = null;
        });
    }

    private static BoxShadows Shadow(Color color, double blur, double opacity) =>
        new(new BoxShadow { Blur = blur, Color = Color.FromArgb((byte)Math.Round(color.A * opacity), color.R, color.G, color.B) });
}
