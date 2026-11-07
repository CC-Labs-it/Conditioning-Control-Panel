using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Avalonia.Controls;

/// <summary>The day's share: an 18 px track and clockwise arc from twelve o'clock.</summary>
internal sealed class OnlineMeterRing : Control
{
    public static readonly StyledProperty<int> PercentProperty =
        AvaloniaProperty.Register<OnlineMeterRing, int>(nameof(Percent));
    public static readonly StyledProperty<OnlineMeterTone> ToneProperty =
        AvaloniaProperty.Register<OnlineMeterRing, OnlineMeterTone>(nameof(Tone));

    static OnlineMeterRing() => AffectsRender<OnlineMeterRing>(PercentProperty, ToneProperty);

    public int Percent { get => GetValue(PercentProperty); set => SetValue(PercentProperty, value); }
    public OnlineMeterTone Tone { get => GetValue(ToneProperty); set => SetValue(ToneProperty, value); }

    public OnlineMeterRing()
    {
        Width = Height = 18;
        IsHitTestVisible = false;
    }

    internal static Color ColorFor(OnlineMeterTone tone) => tone switch
    {
        OnlineMeterTone.Amber => Color.FromRgb(0xFF, 0xB3, 0x47),
        OnlineMeterTone.Spent => Color.FromRgb(0xA7, 0xA7, 0xC1),
        _ => Color.FromRgb(0xFF, 0x69, 0xB4),
    };

    public override void Render(DrawingContext dc)
    {
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0) return;
        var thick = Math.Max(2, size * 5 / 36);
        var radius = (size - thick) / 2;
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2);
        var spent = Tone == OnlineMeterTone.Spent;
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(spent ? ColorFor(Tone) : Color.FromRgb(0x3A, 0x3A, 0x5C)), thick),
            center, radius, radius);
        var sweep = OnlineMeterRules.RingSweep(Percent, spent);
        if (sweep <= 0) return;
        var angle = sweep * 2 * Math.PI;
        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(new Point(center.X, center.Y - radius), false);
            ctx.ArcTo(new Point(center.X + radius * Math.Sin(angle), center.Y - radius * Math.Cos(angle)),
                new Size(radius, radius), 0, sweep > 0.5, SweepDirection.Clockwise);
            ctx.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(ColorFor(Tone)), thick, lineCap: PenLineCap.Round), geometry);
    }
}
