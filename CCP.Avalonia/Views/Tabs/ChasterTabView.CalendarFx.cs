// WPF ChasterTabView.Fx.cs:418-471: staggered marker strokes and splat.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    // The marker strokes ride the calendar row's shared beat (BeatLoop), not an own frame timer.
    private BeatLoop? _calendarDrawBeat;
    private Canvas[] _calendarCrosses = Array.Empty<Canvas>();

    private readonly DispatcherTimer _calendarCometTimer = new() { Interval = TimeSpan.FromMilliseconds(6500) };
    private readonly Canvas _calendarCometCanvas = new() { IsHitTestVisible = false };
    private BeatLoop? _calendarBeat;
    private Border? _calendarTonight, _calendarKeyCell;
    private Ellipse? _calendarRing, _calendarComet;
    private double _calendarNow, _swingStart = -1, _swingTo, _swingDuration, _cometStart = -1;
    private Point _cometFrom, _cometTo;
    private Visual[] _calendarAncestors = Array.Empty<Visual>();
    internal bool CalendarCometRunning => _calendarCometTimer.IsEnabled;
    internal bool CalendarCometVisible => _calendarComet != null;
    internal double CalendarSheetAngle => ((RotateTransform)Sheet.RenderTransform!).Angle;
    internal double CalendarRingScale => (_calendarRing?.RenderTransform as ScaleTransform)?.ScaleX ?? 1;

    private void InitializeCalendarFx()
    {
        Sheet.RenderTransformOrigin = new RelativePoint(0.5, 0, RelativeUnit.Relative);
        Sheet.RenderTransform = new RotateTransform(-1.5);
        CalendarRow.Children.Add(_calendarCometCanvas);
        Sheet.PointerEntered += (_, _) => FlutterCalendarSheet();
        _calendarCometTimer.Tick += (_, _) => CalendarComet();
        AttachedToVisualTree += (_, _) =>
        {
            AmbientFxCanvas.Env.MotionGateChanged += RefreshCalendarLoops;
            _calendarAncestors = this.GetVisualAncestors().ToArray();
            foreach (var ancestor in _calendarAncestors) ancestor.PropertyChanged += CalendarAncestorChanged;
        };
        DetachedFromVisualTree += (_, _) =>
        {
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshCalendarLoops;
            foreach (var ancestor in _calendarAncestors) ancestor.PropertyChanged -= CalendarAncestorChanged;
            _calendarAncestors = Array.Empty<Visual>();
        };
    }

    private void CalendarAncestorChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty) return;
        if (!IsEffectivelyVisible) StopCalendarFx();
        else RefreshCalendarLoops();
    }

    private void RefreshCalendarLoops()
    {
        StopCalendarFx();
        _calendarTonight = _calendarCells.FirstOrDefault(c => c.Day.Today && !c.Day.IsKey).Square;
        _calendarKeyCell = _calendarCells.FirstOrDefault(c => c.Day.IsKey).Square;
        _calendarRing = (_calendarTonight?.Child as Panel)?.Children.OfType<Ellipse>()
            .FirstOrDefault(e => Equals(e.Tag, "tonight-ring"));
        if (!_attached || !IsEffectivelyVisible || !CalendarRow.IsVisible || !AmbientFxCanvas.Env.AllowAmbientLoops || _calendarRing == null) return;
        _calendarBeat ??= new BeatLoop(CalendarRow, AdvanceCalendarMotion);
        _calendarBeat.Start();
        if (_calendarKeyCell != null) _calendarCometTimer.Start();
    }

    private void StopCalendarFx()
    {
        StopCalendarDraw();
        _calendarCometTimer.Stop();
        _calendarBeat?.Stop();
        _calendarCometCanvas.Children.Clear();
        _calendarComet = null;
        _cometStart = _swingStart = -1;
        if (Sheet.RenderTransform is RotateTransform tilt) tilt.Angle = -1.5;
        if (_calendarRing?.RenderTransform is ScaleTransform ring) ring.ScaleX = ring.ScaleY = 1;
    }

    internal void FlutterCalendarSheet() => SwingCalendarSheet(-0.5, 0.3);
    internal void NudgeCalendarSheet() => SwingCalendarSheet(-2.7, 0.32);
    private void SwingCalendarSheet(double to, double duration)
    {
        if (!_attached || !IsEffectivelyVisible || !CalendarRow.IsVisible || AmbientFxCanvas.Env.Level != MotionLevel.Full) return;
        _calendarBeat ??= new BeatLoop(CalendarRow, AdvanceCalendarMotion);
        _swingTo = to; _swingDuration = duration; _swingStart = _calendarBeat.IsRunning ? _calendarNow : 0;
        _calendarBeat.Start();
    }

    internal void CalendarComet()
    {
        if (!CalendarCometRunning || _calendarTonight == null || _calendarKeyCell == null) return;
        var from = _calendarKeyCell.TranslatePoint(new Point(_calendarKeyCell.Bounds.Width / 2, _calendarKeyCell.Bounds.Height / 2), CalendarRow);
        var to = _calendarTonight.TranslatePoint(new Point(_calendarTonight.Bounds.Width * 0.3, _calendarTonight.Bounds.Height * 0.25), CalendarRow);
        if (from == null || to == null) return;
        _cometFrom = from.Value; _cometTo = to.Value; _cometStart = _calendarNow;
        _calendarCometCanvas.Children.Clear();
        _calendarComet = new Ellipse { Width = 8, Height = 8, Fill = Brushes.White, IsHitTestVisible = false };
        _calendarCometCanvas.Children.Add(_calendarComet);
    }

    internal void AdvanceCalendarMotion(double seconds)
    {
        _calendarNow = seconds;
        if (_swingStart >= 0 && Sheet.RenderTransform is RotateTransform tilt)
        {
            var t = Math.Clamp((seconds - _swingStart) / _swingDuration, 0, 1);
            var phase = t < 0.45 ? t / 0.45 : (1 - t) / 0.55;
            tilt.Angle = -1.5 + (_swingTo + 1.5) * Math.Clamp(phase, 0, 1);
            if (t >= 1) _swingStart = -1;
        }
        var pop = 0.0;
        if (_calendarComet != null && _cometStart >= 0)
        {
            var t = Math.Clamp((seconds - _cometStart) / 1.6, 0, 1);
            Canvas.SetLeft(_calendarComet, _cometFrom.X + (_cometTo.X - _cometFrom.X) * t - 4);
            Canvas.SetTop(_calendarComet, _cometFrom.Y + (_cometTo.Y - _cometFrom.Y) * t - 4);
            _calendarComet.Opacity = Math.Min(1, Math.Min(t * 10, (1 - t) * 12.5));
            if (t >= 1) { _calendarCometCanvas.Children.Clear(); _calendarComet = null; }
        }
        if (_cometStart >= 0 && seconds >= _cometStart + 1.6)
        {
            pop = 0.25 * Math.Max(0, 1 - (seconds - _cometStart - 1.6) / 0.38);
            if (pop == 0) _cometStart = -1;
        }
        if (_calendarRing?.RenderTransform is ScaleTransform ring && AmbientFxCanvas.Env.AllowAmbientLoops)
            ring.ScaleX = ring.ScaleY = 1 + 0.02 * (1 - Math.Cos(Math.PI * seconds / 2.2)) + pop;
        if (_swingStart < 0 && (_calendarRing == null || !AmbientFxCanvas.Env.AllowAmbientLoops)) _calendarBeat?.Stop();
    }

    private void StartCalendarDraw()
    {
        if (!_attached || !IsEffectivelyVisible || AmbientFxCanvas.Env.Level != MotionLevel.Full) return;
        _calendarCrosses = _calendarCells.Select(c => c.Square.Child).OfType<Panel>()
            .SelectMany(p => p.Children.OfType<Canvas>()).ToArray();
        if (_calendarCrosses.Length == 0) return;
        foreach (var cross in _calendarCrosses)
            foreach (var line in cross.Children.OfType<Line>())
            {
                var length = Math.Sqrt(Math.Pow(line.EndPoint.X - line.StartPoint.X, 2) + Math.Pow(line.EndPoint.Y - line.StartPoint.Y, 2)) / line.StrokeThickness;
                line.StrokeDashArray = new global::Avalonia.Collections.AvaloniaList<double> { length, length };
                line.StrokeDashOffset = length;
            }
        _calendarDrawBeat?.Stop();
        _calendarDrawBeat = new BeatLoop(CalendarRow, seconds => AdvanceCalendarDraw(seconds * 1000));
        _calendarDrawBeat.Start();   // steps 0 at once, as the old AdvanceCalendarDraw(0)
    }

    internal void AdvanceCalendarDraw(double ms)
    {
        for (var i = 0; i < _calendarCrosses.Length; i++)
        {
            var cross = _calendarCrosses[i];
            var strokes = cross.Children.OfType<Line>().ToArray();
            for (var j = 0; j < strokes.Length; j++)
            {
                var line = strokes[j];
                var length = Math.Sqrt(Math.Pow(line.EndPoint.X - line.StartPoint.X, 2) + Math.Pow(line.EndPoint.Y - line.StartPoint.Y, 2)) / line.StrokeThickness;
                var t = Math.Clamp((ms - 45 * i - 180 * j) / 200, 0, 1);
                line.StrokeDashOffset = length * (1 - t) * (1 - t);
            }
            if (cross.Children.OfType<Ellipse>().FirstOrDefault() is { } splat)
                splat.Opacity = 0.9 * Math.Clamp((ms - 45 * i - 340) / 90, 0, 1);
        }
        if (_calendarCrosses.Length > 0 && ms >= 45 * (_calendarCrosses.Length - 1) + 430) StopCalendarDraw();
    }

    private void StopCalendarDraw()
    {
        _calendarDrawBeat?.Stop();
        _calendarDrawBeat = null;
        foreach (var cross in _calendarCrosses)
        {
            foreach (var line in cross.Children.OfType<Line>()) line.StrokeDashOffset = 0;
            foreach (var splat in cross.Children.OfType<Ellipse>()) splat.Opacity = 0.9;
        }
        _calendarCrosses = Array.Empty<Canvas>();
    }
}
