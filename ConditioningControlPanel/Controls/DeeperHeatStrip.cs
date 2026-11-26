using System;
using System.Windows;
using System.Windows.Media;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// Read-only effect heat map for Play > Deeper: one column per bucket of
/// <see cref="Services.Deeper.EffectHeatMap.Compute"/>, the accent colour at an opacity that
/// follows the heat. No hit testing, no seeking, no editing: it only shows where the file
/// gets busy. Drawn once per change in OnRender, no animation.
/// </summary>
public sealed class DeeperHeatStrip : FrameworkElement
{
    public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
        nameof(Values), typeof(double[]), typeof(DeeperHeatStrip),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(DeeperHeatStrip),
        new FrameworkPropertyMetadata(Brushes.MediumPurple, FrameworkPropertyMetadataOptions.AffectsRender));

    /// <summary>Bucket values in 0..1; null or empty draws the bare track.</summary>
    public double[]? Values
    {
        get => (double[]?)GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public Brush Fill
    {
        get => (Brush)GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public DeeperHeatStrip()
    {
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w <= 0 || h <= 0) return;

        // The bare track, so an empty file still reads as "nothing happens here".
        dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), null,
            new Rect(0, 0, w, h), 4, 4);

        var values = Values;
        if (values == null || values.Length == 0) return;

        double colW = w / values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            double v = values[i];
            if (!(v > 0)) continue;
            v = Math.Min(1, v);
            // Height AND opacity follow the heat, so the strip reads on any theme.
            double barH = Math.Max(2, h * (0.35 + 0.65 * v));
            dc.PushOpacity(0.25 + 0.75 * v);
            dc.DrawRectangle(Fill, null, new Rect(i * colW, h - barH, Math.Max(1, colW - 0.5), barH));
            dc.Pop();
        }
    }
}
