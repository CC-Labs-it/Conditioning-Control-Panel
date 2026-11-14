using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
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
    private sealed record Pop(ScaleTransform Card, Rectangle? Ring, ScaleTransform? RingScale, long Start);
    private readonly List<Pop> _pops = new();
    private readonly HashSet<string> _seenRows = new();
    private sealed record Hover(ScaleTransform Scale, double From, double Target, long Start);
    private readonly Dictionary<ScaleTransform, Hover> _hovers = new();

    private void LobbyCardEnter(object? sender, PointerEventArgs e) => SetCardHover((Control)sender!, true);
    private void LobbyCardLeave(object? sender, PointerEventArgs e) => SetCardHover((Control)sender!, false);
    private void LobbyCardDetached(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (sender is Control { RenderTransform: ScaleTransform scale }) _hovers.Remove(scale);
    }

    // The pop and lift write one transform. The hover factor is independent of the pop clock.
    private double HoverValue(ScaleTransform scale)
    {
        if (!_hovers.TryGetValue(scale, out var h)) return 1;
        double t = Math.Clamp(FxTime.GetElapsedTime(h.Start).TotalMilliseconds / 150, 0, 1);
        return h.From + (h.Target - h.From) * (1 - (1 - t) * (1 - t));
    }

    private double PopValue(ScaleTransform scale)
    {
        var p = _pops.FirstOrDefault(p => p.Card == scale);
        if (p == null) return 1;
        return 0.9 + 0.1 * BackOut(Math.Clamp(FxTime.GetElapsedTime(p.Start).TotalMilliseconds / 320, 0, 1));
    }

    private void SetCardHover(Control card, bool on)
    {
        var parts = card.GetVisualDescendants().OfType<Control>().ToList();
        if (parts.FirstOrDefault(c => Equals(c.Tag, "lobby-sheen")) is Border sheen) sheen.Opacity = on ? 1 : 0;
        if (parts.FirstOrDefault(c => Equals(c.Tag, "lobby-card-face")) is Border face && card.DataContext is LobbyCardView row)
            face.SetCurrentValue(Border.BorderBrushProperty, on ? new SolidColorBrush(Color.Parse("#66FF8CC0")) : row.CardRim);
        var scale = card.RenderTransform as ScaleTransform ?? new ScaleTransform();
        card.RenderTransform = scale;
        double from = HoverValue(scale), target = on ? 1.02 : 1;
        if (!Env.AllowTransitions)
        {
            foreach (var p in _pops.Where(p => p.Card == scale)) if (p.Ring != null) p.Ring.Opacity = 0;
            _pops.RemoveAll(p => p.Card == scale);
        }
        _hovers[scale] = new Hover(scale, Env.AllowTransitions ? from : target, target, FxTime.GetTimestamp());
        scale.ScaleX = scale.ScaleY = PopValue(scale) * (Env.AllowTransitions ? from : target);
        if (Env.AllowTransitions) StartSubjectsFxClock();
        else if (!on) _hovers.Remove(scale);
    }

    // WPF MainWindow.Lobby.cs:146-155: the first fill staggers every column in; after that only a new table pops.
    private void QueueLobbyEntrance(List<string> ids)
    {
        var fresh = ids.Where(id => !_seenRows.Contains(id)).ToHashSet();
        bool firstFill = _seenRows.Count == 0;
        _seenRows.UnionWith(ids);
        if (ids.Count > 0 && (firstFill || fresh.Count > 0))
            Dispatcher.UIThread.Post(() => LobbyEntrance(firstFill ? null : fresh), DispatcherPriority.Normal);
    }

    /// <summary>WPF MainWindow.Lobby.cs:172-209 LobbyEntrance: Off moves nothing; Reduced keeps the
    /// stagger and the pop but drops the ring (MotionFx.AllowParticles).</summary>
    internal void LobbyEntrance(ISet<string>? fresh)
    {
        if (!Env.AllowTransitions || !IsEffectivelyVisible) return;
        TopLevel.GetTopLevel(this)?.UpdateLayout();
        long start = FxTime.GetTimestamp();
        foreach (var list in new[] { AvailableSubjectsList, LobbyPlayingList, LobbyFriendsList })
        {
            list.UpdateLayout();
            int slot = 0;
            for (int i = 0; i < list.ItemCount; i++)
            {
                if (list.Items[i] is not LobbyCardView view || list.ContainerFromIndex(i) is not { } card) continue;
                if (fresh == null) { if (AddEntrance(card, start, slot)) slot++; }
                else if (fresh.Contains(view.Id)) PopCard(card, start);
            }
        }
        if (_entrances.Count > 0 || _pops.Count > 0) StartSubjectsFxClock();
    }

    private void PopCard(Control container, long start)
    {
        var parts = container.GetVisualDescendants().OfType<Control>().ToList();
        if (parts.FirstOrDefault(c => Equals(c.Tag, "lobby-card")) is not { } card) return;
        var scale = card.RenderTransform as ScaleTransform ?? new ScaleTransform();
        card.RenderTransform = scale;
        scale.ScaleX = scale.ScaleY = 0.9 * HoverValue(scale);
        var ring = Env.AllowParticles ? parts.OfType<Rectangle>().FirstOrDefault(c => Equals(c.Tag, "lobby-ring")) : null;
        ScaleTransform? spread = null;
        if (ring != null) { ring.RenderTransform = spread = new ScaleTransform(); ring.Opacity = 0.9; }
        _pops.RemoveAll(p => p.Card == scale);
        _pops.Add(new Pop(scale, ring, spread, start));
    }

    // WPF BackEase { Amplitude = 0.5, EaseOut }.
    private static double BackOut(double t) { double u = 1 - t; return 1 - (u * u * u - u * 0.5 * Math.Sin(Math.PI * u)); }

    private bool AddEntrance(Control card, long start, int slot)
    {
        if (card.RenderTransform is not TranslateTransform slide)
        {
            if (card.RenderTransform != null) return false; // leave authored transforms intact
            card.RenderTransform = slide = new TranslateTransform();
        }
        _entrances.RemoveAll(e => e.Container == card);
        card.Opacity = 0;
        slide.Y = 10;
        _entrances.Add(new Entrance(card, slide, start, 40 * Math.Min(slot, 6)));
        return true;
    }

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
        long start = FxTime.GetTimestamp();
        int slot = 0;
        for (int i = 0; i < list.ItemCount; i++)
            if (list.ContainerFromIndex(i) is { } card && AddEntrance(card, start, slot)) slot++;
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
        for (int i = _pops.Count - 1; i >= 0; i--)
        {
            var p = _pops[i];
            double ms = FxTime.GetElapsedTime(p.Start).TotalMilliseconds;
            double grow = Math.Clamp(ms / 320, 0, 1), spread = Math.Clamp(ms / 520, 0, 1);
            p.Card.ScaleX = p.Card.ScaleY = (0.9 + 0.1 * BackOut(grow)) * HoverValue(p.Card);
            if (p.Ring != null)
            {
                p.RingScale!.ScaleX = p.RingScale.ScaleY = 1 + 0.08 * (1 - (1 - spread) * (1 - spread));
                p.Ring.Opacity = 0.9 * (1 - spread);
            }
            if (p.Ring == null ? grow == 1 : spread == 1) _pops.RemoveAt(i);
        }
        bool hoverRunning = false;
        foreach (var h in _hovers.Values.ToArray())
        {
            double value = HoverValue(h.Scale);
            h.Scale.ScaleX = h.Scale.ScaleY = PopValue(h.Scale) * value;
            if (FxTime.GetElapsedTime(h.Start).TotalMilliseconds < 150) hoverRunning = true;
            else if (h.Target == 1) _hovers.Remove(h.Scale);
        }
        if (_entrances.Count == 0 && _pops.Count == 0 && !hoverRunning) _subjectsFxBeat?.Stop();
    }

    private void FinishSubjectsFx()
    {
        ++_arrival; // invalidate pending first-bind retries when leaving before their dispatch
        _subjectsFxBeat?.Stop();
        foreach (var e in _entrances) { e.Container.Opacity = 1; e.Slide.Y = 0; }
        _entrances.Clear();
        foreach (var p in _pops) { p.Card.ScaleX = p.Card.ScaleY = 1; if (p.Ring != null) p.Ring.Opacity = 0; }
        _pops.Clear();
        foreach (var h in _hovers.Values) h.Scale.ScaleX = h.Scale.ScaleY = 1;
        _hovers.Clear();
    }
}
