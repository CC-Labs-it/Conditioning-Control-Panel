using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Emblems;

namespace ConditioningControlPanel.Avalonia.Controls.Rows;

/// <summary>The inherited section colour used by readable row parts.</summary>
public static class RowHue
{
    public static readonly AttachedProperty<Color> HueProperty =
        AvaloniaProperty.RegisterAttached<Control, Color>("Hue", typeof(RowHue), Colors.Transparent, inherits: true);

    public static Color GetHue(AvaloniaObject target) => target.GetValue(HueProperty);
    public static void SetHue(AvaloniaObject target, Color value) => target.SetValue(HueProperty, value);

    internal static Color Resolve(AvaloniaObject target, IBrush? fallback)
    {
        var hue = GetHue(target);
        if (hue.A != 0) return hue;
        return fallback is ISolidColorBrush solid ? solid.Color : ToColor(EmblemPalette.Pink);
    }

    internal static uint ToArgb(Color c) => ((uint)c.A << 24) | ((uint)c.R << 16) | ((uint)c.G << 8) | c.B;
    internal static Color ToColor(uint c) => Color.FromArgb((byte)(c >> 24), (byte)(c >> 16), (byte)(c >> 8), (byte)c);
}
