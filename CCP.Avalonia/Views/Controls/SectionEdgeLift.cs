// ADAPTED from parity/trunk 1eaffbafab94d59f997bb1ae622055b5cc06ff7b,
// Controls/NavRail/SectionEdgeLift.cs; WPF MainWindow.SectionEdge.cs BuildLiftLeg.
// Four clipped 3 px strips share the existing frame source. No infinite compositor animation.
using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Views.Controls;

public sealed class SectionEdgeLift : Grid
{
    private readonly Panel[] _tracks = new Panel[4];
    private readonly Border[] _bands = new Border[4];
    private readonly TranslateTransform[] _moves = new TranslateTransform[4];
    private readonly double[] _offsets = new double[4];
    private readonly FrameClock _clock;
    private readonly Stopwatch _watch = new();
    private MotionLevel _level;
    private bool _spin, _run, _attached;
    private double _lap, _lastSeconds;
    internal bool IsSpinning => _clock.IsEnabled;
    internal double OffsetOf(int side) => _offsets[side];

    public SectionEdgeLift()
    {
        IsHitTestVisible = false;
        Focusable = false;
        for (int i = 0; i < 4; i++)
        {
            bool horizontal = i % 2 == 0; // clockwise: top, right, bottom, left
            var move = new TranslateTransform();
            var band = new Border { RenderTransform = move, IsHitTestVisible = false };
            var track = new Panel { ClipToBounds = true, Children = { band } };
            if (horizontal)
            {
                track.Height = 3;
                track.Margin = new Thickness(8, 0, 8, 0);
                track.VerticalAlignment = i == 0 ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            }
            else
            {
                track.Width = 3;
                track.Margin = new Thickness(0, 8, 0, 8);
                track.HorizontalAlignment = i == 1 ? HorizontalAlignment.Right : HorizontalAlignment.Left;
            }
            int side = i;
            track.SizeChanged += (_, _) => Place(side);
            Children.Add(track);
            _tracks[i] = track;
            _bands[i] = band;
            _moves[i] = move;
        }
        _clock = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 24) };
        _clock.Tick += (_, _) =>
        {
            double now = _watch.Elapsed.TotalSeconds;
            Step(Math.Clamp(now - _lastSeconds, 0, 0.25));
            _lastSeconds = now;
        };
    }

    internal void Paint(Color color, int ms)
    {
        for (int i = 0; i < 4; i++)
        {
            _bands[i].Transitions = ms <= 0 ? null : new global::Avalonia.Animation.Transitions
            {
                new global::Avalonia.Animation.BrushTransition { Property = Border.BackgroundProperty,
                    Duration = TimeSpan.FromMilliseconds(ms), Easing = new global::Avalonia.Animation.Easings.QuadraticEaseOut() },
            };
            _bands[i].Background = new SolidColorBrush(color);
            _bands[i].OpacityMask = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(i % 2 == 0 ? 1 : 0, i % 2 == 0 ? 0 : 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(Colors.Transparent, 0.4), new GradientStop(Colors.Black, 0.5), new GradientStop(Colors.Transparent, 0.6) },
            };
        }
    }

    // WPF: a 12 s clockwise lap; each side waits, crosses in 3 s, then parks off-strip.
    internal static double LegAt(int side, double seconds)
    {
        double t = (seconds % 12 + 12) % 12;
        double from = side < 2 ? -1 : 1;
        return from * (1 - 2 * Math.Clamp((t - side * 3) / 3, 0, 1));
    }

    internal void Apply(MotionLevel level, bool ambient, bool run)
    {
        bool spin = level == MotionLevel.Full && ambient;
        if (spin != _spin || level != _level) _lap = 0;
        _level = level;
        _spin = spin;
        _run = run;
        IsVisible = level != MotionLevel.Off;
        if (!spin)
        {
            for (int i = 0; i < 4; i++) _offsets[i] = i == 0 ? 0 : i < 2 ? -1 : 1;
            for (int i = 0; i < 4; i++) Place(i);
        }
        else Step(0);
        UpdateClock();
    }

    private void UpdateClock()
    {
        bool run = _spin && _run && _attached && IsVisible;
        if (run && !_clock.IsEnabled)
        {
            _lastSeconds = 0;
            _watch.Restart();
            _clock.Start();
        }
        else if (!run) { _clock.Stop(); _watch.Reset(); }
    }

    internal void Step(double seconds)
    {
        _lap = (_lap + seconds) % 12;
        for (int i = 0; i < 4; i++) { _offsets[i] = LegAt(i, _lap); Place(i); }
    }

    private void Place(int side)
    {
        if (side % 2 == 0) _moves[side].X = _offsets[side] * _tracks[side].Bounds.Width;
        else _moves[side].Y = _offsets[side] * _tracks[side].Bounds.Height;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _attached = true;
        UpdateClock();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _attached = false;
        UpdateClock();
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == IsVisibleProperty && _clock != null) UpdateClock();
    }
}
