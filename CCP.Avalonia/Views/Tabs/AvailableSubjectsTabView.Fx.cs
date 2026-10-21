using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AvailableSubjectsTabView
{
    // WPF MainWindow.SubjectsFx + MotionFx: interaction clock, including under Reduced.
    internal static TimeProvider FxTime = TimeProvider.System;
    private readonly List<Entrance> _entrances = new();
    private readonly Dictionary<Control, Press> _presses = new();
    private readonly HashSet<ScaleTransform> _heldConnectScales = new();
    private DispatcherTimer? _subjectsFxTimer;
    private int _arrival;
    internal bool SubjectsFxRunning => _subjectsFxTimer?.IsEnabled == true;

    private sealed record Entrance(Control Container, TranslateTransform Slide, long Start, int Delay);
    private sealed record Press(ScaleTransform Scale, long Start, double From, double To);

    private void InitializeSubjectsMotion()
    {
        PropertyChanged += (_, e) =>
        {
            if (e.Property != IsVisibleProperty) return;
            if (IsVisible) QueueSubjectEntrance();
            else FinishSubjectsFx();
        };
        Loaded += (_, _) => { if (IsVisible) QueueSubjectEntrance(); };
        Unloaded += (_, _) => FinishSubjectsFx();
    }

    private void QueueSubjectEntrance()
    {
        int arrival = ++_arrival;
        // Like WPF's one Normal-priority retry: ShowTab may make the view visible before binding.
        Dispatcher.UIThread.Post(() =>
        {
            if (arrival == _arrival && IsEffectivelyVisible) StaggerSubjectCards(arrival, allowRetry: true);
        }, DispatcherPriority.Normal);
    }

    private void StaggerSubjectCards(int arrival, bool allowRetry)
    {
        if (!Env.AllowTransitions) return;
        var list = this.FindControl<ItemsControl>("AvailableSubjectsList");
        if (list == null) return;
        if (list.ItemCount == 0)
        {
            if (allowRetry) Dispatcher.UIThread.Post(() =>
            {
                if (arrival == _arrival && IsEffectivelyVisible) StaggerSubjectCards(arrival, allowRetry: false);
            }, DispatcherPriority.Normal);
            return;
        }
        // Hidden tabs have never measured their ItemsPresenter; realize from the window root.
        TopLevel.GetTopLevel(this)?.UpdateLayout();
        list.UpdateLayout();
        // Animate the container, never the bound inner card: Taken must retain CardOpacity=0.6.
        _entrances.Clear();
        long start = FxTime.GetTimestamp();
        int slot = 0;
        for (int i = 0; i < list.ItemCount; i++)
        {
            if (list.ContainerFromIndex(i) is not { } card) continue;
            if (card.RenderTransform is not TranslateTransform slide)
            {
                if (card.RenderTransform != null) continue; // leave authored transforms intact
                card.RenderTransform = slide = new TranslateTransform();
            }
            card.Opacity = 0;
            slide.Y = 10;
            _entrances.Add(new Entrance(card, slide, start, 40 * Math.Min(slot++, 6)));
        }
        if (_entrances.Count > 0) StartSubjectsFxClock();
    }

    private void SubjectConnect_Loaded(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        // Buttons handle pointer events themselves; WPF's PreviewMouse hooks run in the tunnel.
        button.RemoveHandler(PointerPressedEvent, SubjectConnect_PointerPressed);
        button.RemoveHandler(PointerReleasedEvent, SubjectConnect_PointerReleased);
        button.AddHandler(PointerPressedEvent, SubjectConnect_PointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        button.AddHandler(PointerReleasedEvent, SubjectConnect_PointerReleased, RoutingStrategies.Tunnel, handledEventsToo: true);
    }

    private void SubjectConnect_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is Control button && e.GetCurrentPoint(button).Properties.IsLeftButtonPressed)
            PressSubjectConnect(button, true);
    }

    private void SubjectConnect_PointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && sender is Control button)
            PressSubjectConnect(button, false);
    }

    private void SubjectConnect_PointerExited(object? sender, PointerEventArgs e)
    {
        if (sender is Control button) PressSubjectConnect(button, false);
    }

    private void SubjectConnect_PointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (sender is Control button) PressSubjectConnect(button, false);
    }

    private void PressSubjectConnect(Control button, bool down)
    {
        if (button.RenderTransform is not ScaleTransform scale)
        {
            if (button.RenderTransform != null) return;
            button.RenderTransform = scale = new ScaleTransform();
            button.RenderTransformOrigin = RelativePoint.Center;
        }
        double to = down && IsEffectivelyVisible ? 0.97 : 1;
        if (to < 1) _heldConnectScales.Add(scale);
        if (scale.ScaleX == to && scale.ScaleY == to)
        {
            _presses.Remove(button);
            if (to == 1) _heldConnectScales.Remove(scale);
            return;
        }
        if (!Env.AllowTransitions)
        {
            _presses.Remove(button);
            scale.ScaleX = scale.ScaleY = to;
            if (to == 1) _heldConnectScales.Remove(scale);
            return;
        }
        _presses[button] = new Press(scale, FxTime.GetTimestamp(), scale.ScaleX, to);
        StartSubjectsFxClock();
    }

    private void StartSubjectsFxClock()
    {
        _subjectsFxTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render,
            (_, _) => StepSubjectsFx());
        _subjectsFxTimer.Start();
    }

    internal void StepSubjectsFx()
    {
        if (!IsEffectivelyVisible) { FinishSubjectsFx(); return; }
        for (int i = _entrances.Count - 1; i >= 0; i--)
        {
            var e = _entrances[i];
            double ms = FxTime.GetElapsedTime(e.Start).TotalMilliseconds - e.Delay;
            double fade = Math.Clamp(ms / 220, 0, 1), rise = Math.Clamp(ms / 260, 0, 1);
            e.Container.Opacity = 1 - (1 - fade) * (1 - fade);
            e.Slide.Y = 10 * (1 - rise) * (1 - rise);
            if (rise == 1) _entrances.RemoveAt(i);
        }
        foreach (var (button, p) in new List<KeyValuePair<Control, Press>>(_presses))
        {
            double t = Math.Clamp(FxTime.GetElapsedTime(p.Start).TotalMilliseconds / 80, 0, 1);
            p.Scale.ScaleX = p.Scale.ScaleY = p.From + (p.To - p.From) * (1 - (1 - t) * (1 - t));
            if (t == 1)
            {
                _presses.Remove(button);
                if (p.To == 1) _heldConnectScales.Remove(p.Scale);
            }
        }
        if (_entrances.Count == 0 && _presses.Count == 0) _subjectsFxTimer?.Stop();
    }

    private void FinishSubjectsFx()
    {
        ++_arrival; // invalidate pending first-bind retries when leaving before their dispatch
        _subjectsFxTimer?.Stop();
        foreach (var e in _entrances) { e.Container.Opacity = 1; e.Slide.Y = 0; }
        _entrances.Clear();
        // A held-down button may have finished its tween: only reset scales this view owns.
        foreach (var scale in _heldConnectScales) scale.ScaleX = scale.ScaleY = 1;
        _heldConnectScales.Clear();
        _presses.Clear();
    }
}
