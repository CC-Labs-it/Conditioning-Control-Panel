using System;
using System.Windows;
using System.Windows.Media;
using ConditioningControlPanel.Services.Fyp.Online;

namespace ConditioningControlPanel.Controls;

/// <summary>
/// The small "Online media today" ring (title bar pill, corner toast): a track circle and an arc
/// from twelve o'clock, clockwise. Pink, amber from 80%, and a plain grey circle once the day's
/// share is used up. Drawn once per change in OnRender, no animation.
/// </summary>
internal sealed class OnlineMeterRing : FrameworkElement
{
    internal static readonly Color Track = Color.FromRgb(0x3A, 0x3A, 0x5C);
    internal static readonly Color Pink = Color.FromRgb(0xFF, 0x69, 0xB4);
    internal static readonly Color Amber = Color.FromRgb(0xFF, 0xB3, 0x47);
    internal static readonly Color Grey = Color.FromRgb(0xA7, 0xA7, 0xC1);

    public static readonly DependencyProperty PercentProperty = DependencyProperty.Register(
        nameof(Percent), typeof(int), typeof(OnlineMeterRing),
        new FrameworkPropertyMetadata(0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToneProperty = DependencyProperty.Register(
        nameof(Tone), typeof(OnlineMeterTone), typeof(OnlineMeterRing),
        new FrameworkPropertyMetadata(OnlineMeterTone.Pink, FrameworkPropertyMetadataOptions.AffectsRender));

    public int Percent
    {
        get => (int)GetValue(PercentProperty);
        set => SetValue(PercentProperty, value);
    }

    public OnlineMeterTone Tone
    {
        get => (OnlineMeterTone)GetValue(ToneProperty);
        set => SetValue(ToneProperty, value);
    }

    public OnlineMeterRing()
    {
        Width = 18;
        Height = 18;
        IsHitTestVisible = false;
        SnapsToDevicePixels = true;
    }

    internal static Color ColorFor(OnlineMeterTone tone) => tone switch
    {
        OnlineMeterTone.Amber => Amber,
        OnlineMeterTone.Spent => Grey,
        _ => Pink,
    };

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;
        var thick = Math.Max(2, size * 5 / 36);   // the mockup's 5 of 36
        var r = (size - thick) / 2;
        var c = new Point(ActualWidth / 2, ActualHeight / 2);
        var spent = Tone == OnlineMeterTone.Spent;

        var track = new Pen(new SolidColorBrush(spent ? Grey : Track), thick);
        dc.DrawEllipse(null, track, c, r, r);

        var sweep = OnlineMeterRules.RingSweep(Percent, spent);
        if (sweep <= 0) return;
        var a = sweep * 2 * Math.PI;
        var start = new Point(c.X, c.Y - r);
        var end = new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a));
        var fig = new PathFigure { StartPoint = start, IsClosed = false, IsFilled = false };
        fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, sweep > 0.5, SweepDirection.Clockwise, true));
        var geo = new PathGeometry();
        geo.Figures.Add(fig);
        var arc = new Pen(new SolidColorBrush(ColorFor(Tone)), thick)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
        };
        dc.DrawGeometry(null, arc, geo);
    }
}
