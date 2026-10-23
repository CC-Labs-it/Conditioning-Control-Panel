using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Helpers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    private readonly EventBurstLayer _programBurst = new();
    private readonly HashSet<string> _tasksSeenIncomplete = new();
    private readonly List<(BeatLoop Loop, Action Finish)> _programMoments = new();
    private readonly List<(BeatLoop Loop, Ellipse Glow)> _pipBreaths = new();
    private int _pipEpoch;
    private bool _programFxSuppressed;

    internal int ProgramBursts => _programBurst.Count;

    // WPF BackEase EaseOut: x^3 - x * amplitude * sin(pi*x), reflected about (1,1).
    private static double BackOut(double t, double amplitude)
    {
        var x = 1 - t;
        return 1 - (x * x * x - x * amplitude * Math.Sin(Math.PI * x));
    }

    private static void SetScale(Visual target, double value)
    {
        if (target.RenderTransform is ScaleTransform scale) scale.ScaleX = scale.ScaleY = value;
    }

    // Finite interactions share the same clock as the rig. No Transform is an animation target.
    private void Moment(Visual owner, double seconds, Action<double> step, Action finish)
    {
        if (_programFxSuppressed || !Env.AllowTransitions || !IsVisible || VisualRoot == null || Shell is { WindowState: WindowState.Minimized })
        { finish(); return; }
        BeatLoop? loop = null;
        loop = new BeatLoop(owner, t =>
        {
            step(Math.Min(t, seconds));
            if (t < seconds) return;
            loop!.Stop();
            _programMoments.RemoveAll(m => m.Loop == loop);
            finish();
        });
        _programMoments.Add((loop, finish));
        loop.Start();
    }

    private void FinishProgramMoments()
    {
        var moments = _programMoments.ToArray();
        _programMoments.Clear();
        foreach (var (loop, finish) in moments) { loop.Stop(); finish(); }
    }

    private void PopScale(Visual target, double from, double seconds, double amplitude) =>
        Moment(target, seconds, t => SetScale(target, from + (1 - from) * BackOut(t / seconds, amplitude)),
            () => SetScale(target, 1));

    // Generated containers exist one dispatcher pass after ItemsSource changes (WPF Loaded priority).
    // The token prevents a queued celebration from escaping hide, detach, a new run or panic.
    private void PostMoment(Action action)
    {
        var token = (_momentCts ??= new System.Threading.CancellationTokenSource()).Token;
        Dispatcher.UIThread.Post(() =>
        {
            if (!_programFxSuppressed && !token.IsCancellationRequested && IsVisible && VisualRoot != null &&
                Shell is not { WindowState: WindowState.Minimized }) action();
        }, DispatcherPriority.Loaded);
    }

    private void QueueProgramBurst(Control anchor, int count, Color? color = null) =>
        PostMoment(() => _programBurst.Fire(anchor, count, color: color));

    private void QueuePipBreathing()
    {
        // WPF's storyboard keeps running through a repaint or a maximise: keep a live breath on today's pip.
        if (_pipBreaths.Count > 0 && _pipBreaths.All(p => p.Glow.IsAttachedToVisualTree()
                && p.Glow.DataContext is ProgramDayPip { Breathe: true })) return;
        StopPipBreathing();
        var epoch = _pipEpoch;
        Dispatcher.UIThread.Post(() =>
        {
            if (_programFxSuppressed || epoch != _pipEpoch || !AmbientOn || _tier < ProgramHeatTier.Warm) return;
            foreach (var glow in Find<ItemsControl>("ProgramDayStrip").GetVisualDescendants().OfType<Ellipse>()
                         .Where(e => e.Name == "PipGlow" && e.DataContext is ProgramDayPip { Breathe: true }))
            {
                var loop = new BeatLoop(glow, t =>
                {
                    var p = BeatLoop.Breath(t, 1.2); // WPF ProgramsTabView.xaml:626-653
                    glow.Opacity = 0.35 + 0.6 * p;
                    SetScale(glow, 0.9 + 0.22 * p);
                });
                _pipBreaths.Add((loop, glow));
                loop.Start();
            }
        }, DispatcherPriority.Loaded);
    }

    private void StopPipBreathing()
    {
        _pipEpoch++;
        foreach (var (loop, glow) in _pipBreaths)
        {
            loop.Stop();
            glow.Opacity = 0.65;
            SetScale(glow, 1);
        }
        _pipBreaths.Clear();
    }

    private void QueuePipIgnition(IReadOnlyList<ProgramDayPip> pips)
    {
        if (!pips.Any(p => p.Ignite)) return;
        PostMoment(() =>
        {
            var strip = Find<ItemsControl>("ProgramDayStrip");
            if (!ReferenceEquals(strip.ItemsSource, pips)) return;
            foreach (var flare in strip.GetVisualDescendants().OfType<Ellipse>()
                         .Where(e => e.Name == "PipIgnite" && e.DataContext is ProgramDayPip { Ignite: true }))
                Moment(flare, 0.9, t =>
                {
                    flare.Opacity = Math.Clamp(BeatLoop.Keys(t, new[] { (0.0, 0.0), (0.18, 0.95), (0.9, 0.0) }), 0, 1);
                    SetScale(flare, 0.6 + 0.75 * (1 - Math.Pow(1 - t / 0.9, 3)));
                }, () => { flare.Opacity = 0; SetScale(flare, 1.35); });
        });
    }

    private void QueueTaskCompletions(IReadOnlyList<ProgramTaskItem> items)
    {
        if (!items.Any(i => i.JustCompleted)) return;
        var color = AccentNow;
        var count = ProgramHeat.BurstCount(_heat);
        PostMoment(() =>
        {
            var list = Find<ItemsControl>("TodayTaskList");
            if (!ReferenceEquals(list.ItemsSource, items)) return;
            foreach (var card in list.GetVisualDescendants().OfType<Border>()
                         .Where(c => c.Name == "TaskCard" && c.DataContext is ProgramTaskItem { JustCompleted: true }))
            {
                PopScale(card, 0.93, 0.35, 0.6);
                var chip = card.GetVisualDescendants().OfType<Border>().FirstOrDefault(c => c.Name == "TaskDoneChip");
                if (chip != null) PopScale(chip, 0.2, 0.42, 0.9);
                _programBurst.Fire(card, count, color: color);
            }
        });
    }

    // WPF ProgramsTab.cs:1689-1765: first reveal and tab entry, never a service repaint.
    private void StartRunEntrance()
    {
        void PanelIn(Control panel, double delay)
        {
            var slide = new TranslateTransform();
            panel.RenderTransform = slide;
            Moment(panel, delay + 0.34, t =>
            {
                double Ease(double seconds) { var p = Math.Clamp((t - delay) / seconds, 0, 1); return 1 - (1 - p) * (1 - p); }
                panel.Opacity = Math.Clamp(Ease(0.3), 0, 1);
                slide.Y = 14 * (1 - Ease(0.34));
            }, () => { panel.Opacity = 1; slide.Y = 0; });
        }
        PanelIn(Find<Border>("RunHeaderPanel"), 0);
        PanelIn(Find<Border>("TodayPanel"), 0.09);
        var rail = Find<Border>("RailProgressFill");
        Moment(rail, 0.7, t =>
        {
            var p = Math.Clamp((t - 0.15) / 0.55, 0, 1);
            if (rail.RenderTransform is ScaleTransform scale) scale.ScaleX = 1 - Math.Pow(1 - p, 3);
        }, () => { if (rail.RenderTransform is ScaleTransform scale) scale.ScaleX = 1; });
    }

    internal void StopProgramMomentsForPanic()
    {
        _programFxSuppressed = true;
        StopIgnitionLoops();
        StopSessionSheen();
        StopAccentFade();
        StopMoments();
    }
}
