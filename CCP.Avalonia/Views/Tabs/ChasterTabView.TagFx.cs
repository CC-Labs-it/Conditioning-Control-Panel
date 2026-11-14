// WPF ChasterTabView.Fx.cs:362-379: paper tag swings on its string.
using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ChasterTabView
{
    private BeatLoop? _tagSwing;
    private BeatLoop? _tagTug, _calendarBob;
    private readonly TranslateTransform _tagDrop = new(), _calendarDrop = new();
    internal double TagDrop => _tagDrop.Y;
    internal double CalendarBob => _calendarDrop.Y;
    internal bool CalendarBobRunning => _calendarBob?.IsTicking == true;
    private IDisposable? _tagVisibility, _calendarVisibility;
    private readonly RotateTransform _tagRotation = new(-5);
    internal bool TagSwingRunning => _tagSwing?.IsTicking == true;
    internal double TagSwingAngle => _tagRotation.Angle;

    private void InitializeTagSwing()
    {
        PaperTag.RenderTransform = new TransformGroup { Children = { _tagRotation, _tagDrop } };
        CalendarTag.RenderTransform = _calendarDrop;
        CalendarTag.PropertyChanged += CalendarVisibilityChanged;
        PropertyChanged += TagVisibilityChanged;
        PaperTag.PropertyChanged += TagVisibilityChanged;
        AttachedToVisualTree += (_, _) =>
        {
            _tagVisibility = EffectiveVisibility.Watch(PaperTag, () => Dispatcher.UIThread.Post(RefreshTagSwing));
            _calendarVisibility = EffectiveVisibility.Watch(CalendarTag, () => Dispatcher.UIThread.Post(RefreshCalendarBob));
            AmbientFxCanvas.Env.MotionGateChanged += RefreshTagSwing;
            AmbientFxCanvas.Env.MotionGateChanged += RefreshCalendarBob;
            AmbientFxCanvas.Env.MotionGateChanged += GateTagTug;
            RefreshCalendarBob();
            RefreshTagSwing();
        };
        DetachedFromVisualTree += (_, _) =>
        {
            _tagVisibility?.Dispose();
            _calendarVisibility?.Dispose();
            _calendarVisibility = null;
            _tagVisibility = null;
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshTagSwing;
            AmbientFxCanvas.Env.MotionGateChanged -= RefreshCalendarBob;
            AmbientFxCanvas.Env.MotionGateChanged -= GateTagTug;
            StopCalendarBob();
            StopTagTug();
            StopTagSwing();
        };
    }

    private void TagVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property != IsVisibleProperty) return;
        if (e.NewValue is false) { StopTagSwing(); StopTagTug(); }
        else Dispatcher.UIThread.Post(RefreshTagSwing);
    }

    private void RefreshTagSwing()
    {
        if (VisualRoot == null || PaperTag.GetSelfAndVisualAncestors().Any(v => !v.IsVisible) || !AmbientFxCanvas.Env.AllowAmbientLoops)
        {
            StopTagSwing();
            return;
        }
        if (_tagSwing != null) return;
        _tagSwing = new BeatLoop(PaperTag, t => _tagRotation.Angle = -7 + 4 * BeatLoop.Breath(t, 3.6));
        _tagSwing.Start();
    }

    private void StopTagSwing()
    {
        _tagSwing?.Stop();
        _tagSwing = null;
        _tagRotation.Angle = -5;
    }
    private void GateTagTug()
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) StopTagTug();
    }

    private void StopTagTug()
    {
        _tagTug?.Stop();
        _tagTug = null;
        _tagDrop.Y = 0;
    }

    private void FxTagTug()
    {
        StopTagTug();
        if (!PaperTag.IsVisible || !AmbientFxCanvas.Env.AllowTransitions) return;
        _tagTug = new BeatLoop(PaperTag, t =>
        {
            if (t >= .34) { StopTagTug(); return; }
            if (t <= .102) { var p = t / .102; _tagDrop.Y = 8 * (1 - (1 - p) * (1 - p)); }
            else { var p = (t - .102) / .238 - 1; _tagDrop.Y = -8 * (p * p * (1.6 * p + .6)); }
        });
        _tagTug.OnParked = StopTagTug;
        _tagTug.Start();
    }

    private void CalendarVisibilityChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty) RefreshCalendarBob();
    }

    private void RefreshCalendarBob()
    {
        if (VisualRoot == null || CalendarTag.GetSelfAndVisualAncestors().Any(v => !v.IsVisible) || !AmbientFxCanvas.Env.AllowAmbientLoops)
        {
            StopCalendarBob();
            return;
        }
        if (_calendarBob != null) return;
        _calendarBob = new BeatLoop(CalendarTag, t => _calendarDrop.Y = 3 * BeatLoop.Breath(t, 1.1));
        _calendarBob.Start();
    }

    private void StopCalendarBob()
    {
        _calendarBob?.Stop();
        _calendarBob = null;
        _calendarDrop.Y = 0;
    }

}
