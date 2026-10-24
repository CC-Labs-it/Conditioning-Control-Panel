using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class AvailableSubjectsTabView
{
    // WPF MainWindow.SubjectsFx StaggerSubjectCards + MotionFx.StaggerIn: interaction clock, including under Reduced.
    // No Connect press-squish: WPF OnSubjectConnectPress (MainWindow.SubjectsFx.cs:136) has no caller.
    internal static TimeProvider FxTime = TimeProvider.System;
    private readonly List<Entrance> _entrances = new();
    // The shared window beat, not a private 16 ms timer (AmbientBeatLoopsTests ratchet); stopped when done.
    private BeatLoop? _subjectsFxBeat;
    private int _arrival;
    internal bool SubjectsFxRunning => _subjectsFxBeat?.IsRunning == true;

    private sealed record Entrance(Control Container, TranslateTransform Slide, long Start, int Delay);

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
        // Animate the container, never the bound inner row card (its own opacity stays authored).
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

    private void StartSubjectsFxClock()
    {
        _subjectsFxBeat ??= new BeatLoop(this, _ => StepSubjectsFx());
        _subjectsFxBeat.Start();
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
        if (_entrances.Count == 0) _subjectsFxBeat?.Stop();
    }

    private void FinishSubjectsFx()
    {
        ++_arrival; // invalidate pending first-bind retries when leaving before their dispatch
        _subjectsFxBeat?.Stop();
        foreach (var e in _entrances) { e.Container.Opacity = 1; e.Slide.Y = 0; }
        _entrances.Clear();
    }
}
