using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>The calibration cursor's bounded pink comet; geometry only, no effects or render resources.</summary>
public class GazeCursorTrailControl : Control
{
    public GazeCursorTrail History { get; } = new();

    // Paints are cached per fade step (WPF caches its paints too): no allocation per segment or frame.
    private const int Steps = 32;
    private static readonly Pen[] Glow = Build(70, 2.5, 7.5), Core = Build(140, 1, 2.4);
    private static Pen[] Build(int alpha, double width, double grow)
    {
        var pens = new Pen[Steps + 1];
        for (int s = 0; s <= Steps; s++)
        {
            double ease = (double)s / Steps;
            pens[s] = new Pen(new SolidColorBrush(Color.FromArgb((byte)(alpha * ease), 255, 105, 180)), width + grow * ease, lineCap: PenLineCap.Round);
        }
        return pens;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        for (int i = 1; i < History.Count; i++)
        {
            var a = History[i - 1]; var b = History[i];
            double t = 1 - b.Age / GazeCursorTrail.LifeSeconds;
            int step = (int)Math.Round(Math.Clamp(t * t, 0, 1) * Steps);
            var from = new Point(a.X, a.Y); var to = new Point(b.X, b.Y);
            context.DrawLine(Glow[step], from, to);
            context.DrawLine(Core[step], from, to);
        }
    }
}
