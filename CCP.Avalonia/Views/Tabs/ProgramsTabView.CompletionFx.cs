using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models.Program;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    private readonly HashSet<string> _tasksSeenIncomplete = new();
    private readonly EventBurstLayer _programBursts = new();
    private BeatLoop? _sessionSheen;
    private double _sheenSeconds;
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

    private void CelebrateTasks(IReadOnlyList<ProgramTaskItem> items)
    {
        var list = Find<ItemsControl>("TodayTaskList");
        var generation = _burstGeneration;
        Dispatcher.UIThread.Post(() =>
        {
            if (generation != _burstGeneration || !ReferenceEquals(list.ItemsSource, items) || !IsVisible || VisualRoot == null
                || Shell is { WindowState: WindowState.Minimized }) return;
            UpdateLayout();
            foreach (var card in list.GetVisualDescendants().OfType<Border>()
                         .Where(b => b.Name == "TaskCard" && b.DataContext is ProgramTaskItem { JustCompleted: true }))
            {
                Pop(card, .93, .35, .6);
                var chip = card.GetVisualDescendants().OfType<Border>().FirstOrDefault(b => b.Name == "TaskDoneChip");
                if (chip != null) Pop(chip, .2, .42, .9);
                QueueProgramBurst(card, ConditioningControlPanel.Helpers.ProgramHeat.BurstCount(_heat), AccentNow);
            }
        }, DispatcherPriority.Loaded);
    }

    internal void StopProgramFxForPanic()
    {
        StopIgnitionLoops();
        StopMoments();
        StopAccentFade();
        StopSessionSheen();
        _programBursts.Stop();
    }

    // WPF UpdateProgramSessionRow: one-second property writes, never rebuild the task list or rail.
    internal void UpdateLiveSession()
    {
        var svc = AvApp.Programs;
        var enrollment = svc?.ActiveEnrollment;
        var record = svc?.TodayRecord;
        var row = Find<Grid>("TodaySessionProgressRow");
        var text = Find<TextBlock>("TxtTodaySessionProgress");
        var bar = Find<ProgressBar>("TodaySessionProgressBar");
        if (enrollment == null || record == null || !IsVisible || VisualRoot == null)
        {
            row.IsVisible = text.IsVisible = false;
            bar.Value = 0;
            StopSessionSheen();
            return;
        }
        var runner = AvApp.Sessions;
        var ours = runner is { IsRunning: true, CurrentSession: { } } && svc!.IsProgramSession(runner.CurrentSession);
        var paused = enrollment.State == ProgramEnrollmentState.Paused;
        var pause = Find<Button>("BtnProgramPauseResume");
        pause.IsEnabled = !svc!.IsReadOnly && !(ours && !paused);
        ToolTip.SetTip(pause, ours && !paused ? Loc.Get("programs_pause_blocked_hint") : null);
        row.IsVisible = text.IsVisible = ours;
        if (!ours)
        {
            bar.Value = 0;
            StopSessionSheen();
            return;
        }
        var total = TimeSpan.FromMinutes(Math.Max(1, runner!.CurrentSession!.DurationMinutes));
        var elapsed = runner.Elapsed;
        if (elapsed > total) elapsed = total;
        bar.Foreground = text.Foreground = _runAccent;
        bar.Value = Math.Clamp(elapsed.TotalSeconds / total.TotalSeconds * 100, 0, 100);
        var clock = Loc.GetF("programs_session_progress", ProgramClockText(elapsed), ProgramClockText(total));
        text.Text = runner.IsPaused ? Loc.GetF("programs_session_progress_paused", clock) : clock;
        if (!runner.IsPaused && AmbientOn && _tier >= ConditioningControlPanel.Helpers.ProgramHeatTier.Warm)
        {
            var seconds = ConditioningControlPanel.Helpers.ProgramHeat.SheenSweepSeconds(_heat);
            if (_sessionSheen != null && Math.Abs(_sheenSeconds - seconds) < .05) return;
            StopSessionSheen();
            var host = Find<Border>("SessionBarHost");
            if (host.Bounds.Width < 80) return; // next tick retries after measure, as WPF.
            _sheenSeconds = seconds;
            var sheen = Find<Rectangle>("SessionSheen");
            var slide = (TranslateTransform)sheen.RenderTransform!;
            sheen.Opacity = 1;
            _sessionSheen = new BeatLoop(sheen, t => slide.X = -160 + (host.Bounds.Width + 220) * (t / seconds % 1));
            _sessionSheen.Start();
        }
        else StopSessionSheen();
    }

    private static string ProgramClockText(TimeSpan span)
    {
        if (span < TimeSpan.Zero) span = TimeSpan.Zero;
        return span.TotalHours >= 1 ? $"{(int)span.TotalHours}:{span.Minutes:D2}:{span.Seconds:D2}"
            : $"{(int)span.TotalMinutes}:{span.Seconds:D2}";
    }

    private void StopSessionSheen()
    {
        _sessionSheen?.Stop();
        _sessionSheen = null;
        Find<Rectangle>("SessionSheen").Opacity = 0;
    }
}
