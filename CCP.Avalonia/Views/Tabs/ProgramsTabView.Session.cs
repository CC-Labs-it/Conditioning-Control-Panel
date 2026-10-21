using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    private BeatLoop? _sessionSheen;
    private double _sessionSheenSeconds;
    internal bool SessionSheenRunning => _sessionSheen?.IsRunning == true;

    // WPF UpdateProgramSessionRow: session ticks repaint only the slot, never the whole tab.
    // clockOnly (the 1 s tick): only the clock and bar; the button keeps its binding (P07).
    internal void RefreshProgramSessionRow(bool clockOnly = false)
    {
        if (!IsVisible || VisualRoot is null) return;
        var svc = AvApp.Programs;
        var accent = _runAccent ?? Theme("PinkBrush", Brushes.HotPink);
        if (svc?.ActiveEnrollment is { } enrollment && svc.TodayRecord is { } record &&
            Find<StackPanel>("ProgramsRunPanel").IsVisible)
        {
            if (clockOnly)
                PaintSessionProgress(svc, AvApp.Sessions is { IsRunning: true } r && svc.IsProgramSession(r.CurrentSession), accent);
            else
                UpdateSessionRow(svc, enrollment, record, accent, Theme("TextMutedBrush", Brushes.Gray));
        }
        else
        {
            Find<Grid>("TodaySessionProgressRow").IsVisible = false;
            Find<TextBlock>("TxtTodaySessionProgress").IsVisible = false;
            StopSessionSheen();
        }
    }

    private void PaintSessionProgress(ProgramService svc, bool ours, IBrush accent)
    {
        var runner = AvApp.Sessions;
        var live = ours && !svc.IsReadOnly && runner?.CurrentSession != null;
        Find<Grid>("TodaySessionProgressRow").IsVisible = live;
        var clock = Find<TextBlock>("TxtTodaySessionProgress");
        clock.IsVisible = live;
        var bar = Find<ProgressBar>("TodaySessionProgressBar");
        if (!live)
        {
            bar.Value = 0;
            StopSessionSheen();
            return;
        }
        var total = TimeSpan.FromMinutes(Math.Max(1, runner!.CurrentSession!.DurationMinutes));
        var elapsed = runner.Elapsed;
        if (elapsed > total) elapsed = total;
        bar.Foreground = accent;
        bar.Value = SessionClockLabel.ProgramProgress(runner.Elapsed, runner.CurrentSession.DurationMinutes);
        var text = Loc.GetF("programs_session_progress", SessionClockLabel.ProgramClock(elapsed),
            SessionClockLabel.ProgramClock(total));
        clock.Text = runner.IsPaused ? Loc.GetF("programs_session_progress_paused", text) : text;
        clock.Foreground = accent;
        SyncSessionSheen();
    }

    // WPF EnsureProgramSessionSheen: warm tier, full ambient motion, visible and unpaused only.
    // BeatLoop rides the existing shared clock; no infinite Avalonia Animation is installed.
    private void SyncSessionSheen()
    {
        var runner = AvApp.Sessions;
        var allowed = AmbientOn && _tier >= ProgramHeatTier.Warm &&
            AvApp.Programs is { IsReadOnly: false } svc &&
            runner is { IsRunning: true, IsPaused: false } && svc.IsProgramSession(runner.CurrentSession) &&
            Find<Grid>("TodaySessionProgressRow").IsVisible;
        var host = Find<Border>("SessionBarHost");
        if (!allowed || host.Bounds.Width < 80)
        {
            StopSessionSheen();
            return;
        }
        var seconds = ProgramHeat.SheenSweepSeconds(_heat);
        if (_sessionSheen?.IsRunning == true && Math.Abs(seconds - _sessionSheenSeconds) < 0.05) return;
        StopSessionSheen();
        var sheen = Find<Rectangle>("SessionSheen");
        var slide = (TranslateTransform)sheen.RenderTransform!;
        sheen.Opacity = 1;
        _sessionSheenSeconds = seconds;
        _sessionSheen = new BeatLoop(sheen, t => slide.X = -160 + (host.Bounds.Width + 220) * (t / seconds % 1));
        _sessionSheen.Start();
    }

    private void StopSessionSheen()
    {
        _sessionSheen?.Stop();
        _sessionSheen = null;
        if (this.FindControl<Rectangle>("SessionSheen") is { } sheen) sheen.Opacity = 0;
    }
}
