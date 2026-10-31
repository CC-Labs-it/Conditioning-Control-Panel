// PORTED from ConditioningControlPanel/Controls/OnlineMeterRing.cs:55-81; Core supplies the sweep.
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Services.Fyp.Online;
namespace ConditioningControlPanel.Avalonia.Controls;
public sealed class OnlineMeterRing : Control
{
    internal static readonly Color Track = Color.FromRgb(0x3A, 0x3A, 0x5C);
    internal static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    internal static readonly Color Amber = Color.FromRgb(0xFF, 0xB3, 0x47);
    internal static readonly Color Grey = Color.FromRgb(0xA7, 0xA7, 0xC1);
    public static readonly StyledProperty<int> PercentProperty =
        AvaloniaProperty.Register<OnlineMeterRing, int>(nameof(Percent));
    internal static readonly StyledProperty<OnlineMeterTone> ToneProperty =
        AvaloniaProperty.Register<OnlineMeterRing, OnlineMeterTone>(nameof(Tone));
    static OnlineMeterRing() => AffectsRender<OnlineMeterRing>(PercentProperty, ToneProperty);
    public OnlineMeterRing()
    {
        Width = 18;
        Height = 18;
        IsHitTestVisible = false;
    }
    public int Percent
    {
        get => GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }
    internal OnlineMeterTone Tone
    {
        get => GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }
    internal static Color ColorFor(OnlineMeterTone tone) => tone switch
    {
        OnlineMeterTone.Amber => Amber,
        OnlineMeterTone.Spent => Grey,
        _ => Pink,
    };
    public override void Render(DrawingContext dc)
    {
        var w = Bounds.Width;
        var h = Bounds.Height;
        var size = Math.Min(w, h);
        if (size <= 0) return;
        var thick = Math.Max(2, size * 5 / 36);   // the mockup's 5 of 36
        var r = (size - thick) / 2;
        var c = new Point(w / 2, h / 2);
        var spent = Tone == OnlineMeterTone.Spent;
        dc.DrawEllipse(null, new Pen(new SolidColorBrush(spent ? Grey : Track), thick), c, r, r);
        var sweep = OnlineMeterRules.RingSweep(Percent, spent);
        if (sweep <= 0) return;
        var a = sweep * 2 * Math.PI;
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(c.X, c.Y - r), false);
            g.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, sweep > 0.5, SweepDirection.Clockwise);
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(ColorFor(Tone)), thick, lineCap: PenLineCap.Round), geo);
    }
}
