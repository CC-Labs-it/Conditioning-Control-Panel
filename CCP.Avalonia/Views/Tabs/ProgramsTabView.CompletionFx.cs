using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    private readonly HashSet<string> _tasksSeenIncomplete = new();
    private readonly HashSet<(int Day, string Task)> _pendingTaskCompletions = new();
    private readonly EventBurstLayer _programBursts = new();
    private int _burstGeneration;
    internal int CompletionBursts => _programBursts.Count;

    // WPF BackEase.EaseOut (not Avalonia BackEaseOut's different polynomial).
    private static double ProgramBackOut(double p, double amplitude)
    {
        var x = 1 - p;
        return 1 - (x * x * x - amplitude * x * Math.Sin(Math.PI * x));
    }

    // Finite moments share the rig's beat; transform properties are written directly (P82).
    private void Moment(Control owner, double seconds, Action<double> apply, Action? done = null)
    {
        if (!Env.AllowTransitions) { apply(seconds); done?.Invoke(); return; }
        var token = (_momentCts ??= new CancellationTokenSource()).Token;
        BeatLoop? loop = null;
        CancellationTokenRegistration registration = default;
        void Finish() { loop?.Stop(); registration.Dispose(); done?.Invoke(); }
        loop = new BeatLoop(owner, t =>
        {
            apply(Math.Min(t, seconds));
            if (t >= seconds) Finish();
        });
        loop.Start();
        registration = token.Register(Finish);
    }

    // WPF Views/Tabs/ProgramsTabView.xaml:1281-1318 and ProgramsTab.cs:1833-1845.
    private void Pop(Control owner, double from, double seconds, double amplitude)
    {
        if (owner.RenderTransform is not ScaleTransform scale) return;
        Moment(owner, seconds, t => scale.ScaleX = scale.ScaleY = from + (1 - from) * ProgramBackOut(t / seconds, amplitude),
            () => scale.ScaleX = scale.ScaleY = 1);
    }

    private void QueueProgramBurst(Control anchor, int count, Color? color = null)
    {
        var generation = _burstGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _burstGeneration || VisualRoot == null || !IsVisible) return;
            UpdateLayout(); // WPF Loaded priority: generated task containers / the newly revealed panel exist.
            _programBursts.Fire(anchor, count, color: color);
        }, DispatcherPriority.Loaded);
    }

    // WPF ProgramsTab.cs:1074-1090 and ProgramsFx.cs:987-1025: only observed flips pop.
    private void CelebrateTasks(IReadOnlyList<ProgramTaskItem> items)
    {
        var list = Find<ItemsControl>("TodayTaskList");
        var day = App.Programs?.ActiveEnrollment?.CurrentDay ?? 0;
        foreach (var item in items.Where(item => item.JustCompleted))
            _pendingTaskCompletions.Add((day, item.TaskId));
        var generation = _burstGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _burstGeneration || !IsVisible || VisualRoot == null
                || Shell is { WindowState: WindowState.Minimized }) return;
            UpdateLayout();
            // WPF ProgramsFx.cs:1006-1019 resolves the live containers after any queued rebuilds.
            var currentDay = App.Programs?.ActiveEnrollment?.CurrentDay ?? 0;
            foreach (var card in list.GetVisualDescendants().OfType<Border>()
                         .Where(b => b.Name == "TaskCard" && b.DataContext is ProgramTaskItem { DoneChipVisible: true } task
                                     && _pendingTaskCompletions.Remove((currentDay, task.TaskId))))
            {
                Pop(card, .93, .35, .6);
                var chip = card.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "TaskDoneChip");
                if (chip != null) Pop(chip, .2, .42, .9);
                QueueProgramBurst(card, ConditioningControlPanel.Helpers.ProgramHeat.BurstCount(_heat), AccentNow);
            }
            _pendingTaskCompletions.Clear(); // a missing container or a different day never replays later
        }, DispatcherPriority.Loaded);
    }

    // Safety extension: cancel finite effects and queued sparks with the base panic stop registry.
    internal void StopProgramFxForPanic()
    {
        StopIgnitionLoops();
        StopMoments();
        StopAccentFade();
        StopSessionSheen();
        _programBursts.Stop();
    }

}
