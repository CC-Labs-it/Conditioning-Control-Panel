using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>The calibration cursor's bounded pink comet; geometry only, no effects or render resources.</summary>
public sealed class GazeCursorTrailControl : Control
{
    public GazeCursorTrail History { get; } = new();
    public override void Render(DrawingContext context)
    {
        base.Render(context);
        for (int i = 1; i < History.Count; i++)
        {
            var a = History[i - 1]; var b = History[i];
            double t = 1 - b.Age / GazeCursorTrail.LifeSeconds, ease = t * t;
            var from = new Point(a.X, a.Y); var to = new Point(b.X, b.Y);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(70 * ease), 255, 105, 180)), 2.5 + 7.5 * ease, lineCap: PenLineCap.Round), from, to);
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb((byte)(140 * ease), 255, 105, 180)), 1 + 2.4 * ease, lineCap: PenLineCap.Round), from, to);
        }
    }
}
