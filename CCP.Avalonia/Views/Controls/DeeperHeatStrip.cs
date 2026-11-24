using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

/// <summary>A read-only display of effect activity across a file.</summary>
public sealed class DeeperHeatStrip : Control
{
    public static readonly StyledProperty<double[]?> ValuesProperty =
        AvaloniaProperty.Register<DeeperHeatStrip, double[]?>(nameof(Values));
    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<DeeperHeatStrip, IBrush>(nameof(Fill), Brushes.MediumPurple);

    static DeeperHeatStrip() => AffectsRender<DeeperHeatStrip>(ValuesProperty, FillProperty);

    public double[]? Values { get => GetValue(ValuesProperty); set => SetValue(ValuesProperty, value); }
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }

    public DeeperHeatStrip() => IsHitTestVisible = false;

    public override void Render(DrawingContext dc)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), null,
            new RoundedRect(new Rect(0, 0, w, h), 4));
        var values = Values;
        if (values == null || values.Length == 0) return;
        double colW = w / values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            if (!(v > 0)) continue;
            v = Math.Min(1, v);
            double barH = Math.Max(2, h * (0.35 + 0.65 * v));
            using (dc.PushOpacity(0.25 + 0.75 * v))
                dc.DrawRectangle(Fill, null, new Rect(i * colW, h - barH, Math.Max(1, colW - 0.5), barH));
        }
    }
}
