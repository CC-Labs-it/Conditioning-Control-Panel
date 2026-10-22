using System;
using System.Linq;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>WPF Inputs.xaml/InputFx:200ms focus fade on the TextBox itself, safe before template
/// application. A BoxShadow replaces its DropShadowEffect; the editing template stays Fluent.</summary>
public static class InputFx
{
    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<Control, bool>("IsEnabled", typeof(InputFx));
    public static readonly AttachedProperty<Color> GlowColorProperty =
        AvaloniaProperty.RegisterAttached<Control, Color>("GlowColor", typeof(InputFx));
    public static readonly AttachedProperty<double> GlowLevelProperty =
        AvaloniaProperty.RegisterAttached<Control, double>("GlowLevel", typeof(InputFx));

    public static bool GetIsEnabled(Control c) => c.GetValue(IsEnabledProperty);
    public static void SetIsEnabled(Control c, bool v) => c.SetValue(IsEnabledProperty, v);
    public static Color GetGlowColor(Control c) => c.GetValue(GlowColorProperty);
    public static void SetGlowColor(Control c, Color v) => c.SetValue(GlowColorProperty, v);
    public static double GetGlowLevel(Control c) => c.GetValue(GlowLevelProperty);
    public static void SetGlowLevel(Control c, double v) => c.SetValue(GlowLevelProperty, v);

    static InputFx()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((box, e) =>
        {
            box.PropertyChanged -= OnBoxChanged;
            box.TemplateApplied -= OnTemplateApplied;
            if (e.NewValue is true)
            {
                box.Transitions ??= new Transitions();
                if (!box.Transitions.OfType<DoubleTransition>().Any(t => t.Property == GlowLevelProperty))
                    box.Transitions.Add(new DoubleTransition { Property = GlowLevelProperty, Duration = TimeSpan.FromMilliseconds(200) });
                box.PropertyChanged += OnBoxChanged;
                box.TemplateApplied += OnTemplateApplied;
            }
            SetGlowLevel(box, GetIsEnabled(box) && box.IsKeyboardFocusWithin ? 0.45 : 0);
            if (GetIsEnabled(box)) Paint(box);
            else if (e.OldValue is true)
                FindBorder(box)?.ClearValue(Border.BoxShadowProperty); // let the replacement theme own its chrome
        });
        GlowLevelProperty.Changed.AddClassHandler<TextBox>((box, _) => Paint(box));
        GlowColorProperty.Changed.AddClassHandler<TextBox>((box, _) => Paint(box));
    }

    private static void OnBoxChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (sender is TextBox box && e.Property == InputElement.IsKeyboardFocusWithinProperty)
            SetGlowLevel(box, box.IsKeyboardFocusWithin ? 0.45 : 0);
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs e)
    {
        if (sender is TextBox box) Paint(box);
    }

    private static Border? FindBorder(TextBox box) =>
        box.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "PART_BorderElement");

    private static void Paint(TextBox box)
    {
        if (!GetIsEnabled(box)) return;
        var border = FindBorder(box);
        if (border == null) return; // WPF InputFx also permits Focus() before ApplyTemplate().
        // Borderless (.transparent) boxes: WPF's glow falls on the text, not on a box (MantraWindow.xaml:164).
        if (border.BorderThickness == default) { border.ClearValue(Border.BoxShadowProperty); return; }
        var c = GetGlowColor(box);
        var alpha = (byte)Math.Round(Math.Clamp(GetGlowLevel(box), 0, 0.45) * 255);
        border.BoxShadow = new BoxShadows(new BoxShadow { Blur = 8, Color = Color.FromArgb(alpha, c.R, c.G, c.B) });
    }
}
