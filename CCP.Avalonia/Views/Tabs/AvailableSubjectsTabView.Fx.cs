using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.VisualTree;
using ConditioningControlPanel.Services.Lobby;
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
    private bool _lobbyHasFilled;
    private readonly HashSet<string> _seenRows = new();
    private readonly List<PopEntrance> _pops = new();
    private sealed record PopEntrance(Control Card, ScaleTransform Scale, Control? Ring, ScaleTransform? RingScale, long Start);
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
        FinishCurrentEntrances();
        StaggerList(list);
        if (_entrances.Count > 0) StartSubjectsFxClock();
    }

    private void LobbyRowsArrived(LobbySnapshot snapshot)
    {
        var ids = snapshot.Open.Concat(snapshot.Playing).Concat(snapshot.Friends).Select(LobbyPageRules.Id).ToList();
        var fresh = ids.Where(id => !_seenRows.Contains(id)).ToHashSet();
        foreach (var id in ids) _seenRows.Add(id);
        if (!_lobbyHasFilled) { LobbyFirstFill(ids.Count); return; }
        if (fresh.Count == 0 || !IsEffectivelyVisible || !Env.AllowTransitions) return;
        int arrival = _arrival;
        Dispatcher.UIThread.Post(() =>
        {
            if (arrival != _arrival || !IsEffectivelyVisible || !Env.AllowTransitions) return;
            TopLevel.GetTopLevel(this)?.UpdateLayout();
            foreach (var list in new[] { AvailableSubjectsList, LobbyPlayingList, LobbyFriendsList })
            {
                list.UpdateLayout();
                for (int i = 0; i < list.ItemCount; i++)
                {
                    if (list.Items[i] is not LobbyCardView row || !fresh.Contains(row.Id) || list.ContainerFromIndex(i) is not Control container) continue;
                    var children = container.GetVisualDescendants().OfType<Control>().ToList();
                    var card = children.FirstOrDefault(c => Equals(c.Tag, "lobby-card"));
                    var ring = children.FirstOrDefault(c => Equals(c.Tag, "lobby-ring"));
                    if (card == null) continue;
                    var scale = new ScaleTransform(0.9, 0.9);
                    card.RenderTransform = scale;
                    ScaleTransform? ringScale = null;
                    if (ring != null && Env.AllowParticles)
                    {
                        ring.RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative);
                        ring.RenderTransform = ringScale = new ScaleTransform(1, 1);
                        ring.Opacity = 0.9;
                    }
                    _pops.Add(new PopEntrance(card, scale, ringScale == null ? null : ring, ringScale, FxTime.GetTimestamp()));
                }
            }
            if (_pops.Count > 0) StartSubjectsFxClock();
        }, DispatcherPriority.Normal);
    }

    // WPF MainWindow.Lobby.cs:146-156,166-184: the first live fill staggers each column.
    private void LobbyFirstFill(int count)
    {
        if (count == 0 || _lobbyHasFilled) return;
        _lobbyHasFilled = true;
        if (!IsEffectivelyVisible) return;
        int arrival = _arrival;
        Dispatcher.UIThread.Post(() =>
        {
            if (arrival != _arrival || !IsEffectivelyVisible || !Env.AllowTransitions) return;
            FinishCurrentEntrances();
            StaggerList(AvailableSubjectsList);
            StaggerList(LobbyPlayingList);
            StaggerList(LobbyFriendsList);
            if (_entrances.Count > 0) StartSubjectsFxClock();
        }, DispatcherPriority.Normal);
    }

    private void StaggerList(ItemsControl list)
    {
        TopLevel.GetTopLevel(this)?.UpdateLayout();
        list.UpdateLayout();
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
        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var p = _pops[i];
            double ms = FxTime.GetElapsedTime(p.Start).TotalMilliseconds;
            // WPF BackEase amplitude 0.5, EaseOut (MainWindow.Lobby.cs:196).
            double progress = Math.Clamp(ms / 320, 0, 1), u = 1 - progress;
            double eased = 1 - (u * u * u - 0.5 * u * Math.Sin(Math.PI * u));
            p.Scale.ScaleX = p.Scale.ScaleY = 0.9 + 0.1 * eased;
            double r = Math.Clamp(ms / 520, 0, 1);
            if (p.Ring != null && p.RingScale != null)
            {
                p.RingScale.ScaleX = p.RingScale.ScaleY = 1 + 0.08 * (1 - (1 - r) * (1 - r));
                p.Ring.Opacity = 0.9 * (1 - r);
            }
            if (ms >= (p.Ring == null ? 320 : 520)) _pops.RemoveAt(i);
        }
        if (_entrances.Count == 0 && _pops.Count == 0) _subjectsFxBeat?.Stop();
    }

    private void FinishSubjectsFx()
    {
        ++_arrival; // invalidate pending first-bind retries when leaving before their dispatch
        _subjectsFxBeat?.Stop();
        FinishCurrentEntrances();
    }

    private void FinishCurrentEntrances()
    {
        foreach (var e in _entrances) { e.Container.Opacity = 1; e.Slide.Y = 0; }
        _entrances.Clear();
        foreach (var p in _pops) { p.Scale.ScaleX = p.Scale.ScaleY = 1; if (p.Ring != null) p.Ring.Opacity = 0; }
        _pops.Clear();
    }
}
