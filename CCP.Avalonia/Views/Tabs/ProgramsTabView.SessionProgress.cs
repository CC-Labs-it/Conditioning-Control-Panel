using System;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Program;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    private BeatLoop? _sessionSheen;
    private double _sessionSheenSeconds;

    /// <summary>WPF UpdateProgramSessionRow: repaint only the live clock/bar on the engine's tick.
    /// The task list and ignition scenery retain their instances while the seconds advance.</summary>
    internal void RefreshLiveSessionProgress()
    {
        if (!IsVisible || VisualRoot is null) { StopSessionSheen(); return; }
        var svc = AvApp.Programs;
        var runner = AvApp.Sessions;
        var ours = svc is { IsReadOnly: false, ActiveEnrollment: not null, TodayRecord: not null }
                   && runner is { IsRunning: true, CurrentSession: not null }
                   && svc.IsProgramSession(runner.CurrentSession);
        var row = Find<Grid>("TodaySessionProgressRow");
        var text = Find<TextBlock>("TxtTodaySessionProgress");
        var bar = Find<ProgressBar>("TodaySessionProgressBar");
        row.IsVisible = text.IsVisible = ours;
        if (!ours)
        {
            bar.Value = 0;
            StopSessionSheen();
            return;
        }

        var progress = ProgramSessionProgress.From(runner!.Elapsed, runner.CurrentSession!.DurationMinutes);
        var accent = Windows.MainShellWindow.AccentBrush(svc!.ActiveProgram?.AccentColor);
        bar.Foreground = text.Foreground = accent;
        bar.Value = progress.Percent;
        var clock = Loc.GetF("programs_session_progress", ProgramSessionProgress.Clock(progress.Elapsed),
            ProgramSessionProgress.Clock(progress.Total));
        text.Text = runner.IsPaused ? Loc.GetF("programs_session_progress_paused", clock) : clock;
        SyncSessionSheen(!runner.IsPaused);
    }

    private void SyncSessionSheen(bool running)
    {
        var host = Find<Border>("SessionBarHost");
        if (!running || !AmbientOn || _tier < ProgramHeatTier.Warm || host.Bounds.Width < 80)
        {
            StopSessionSheen();
            return;
        }
        var seconds = ProgramHeat.SheenSweepSeconds(_heat);
        if (_sessionSheen is { IsRunning: true } && Math.Abs(_sessionSheenSeconds - seconds) < 0.05) return;
        StopSessionSheen();
        _sessionSheenSeconds = seconds;
        var sheen = Find<Rectangle>("SessionSheen");
        var slide = (TranslateTransform)sheen.RenderTransform!;
        var width = host.Bounds.Width;
        sheen.Opacity = 1;
        // WPF -160 -> width+60, linear, capped at the shared 30 Hz window beat.
        _sessionSheen = new BeatLoop(sheen, elapsed =>
            slide.X = -160 + (width + 220) * (elapsed / seconds % 1));
        _sessionSheen.Start();
    }

    private void StopSessionSheen()
    {
        _sessionSheen?.Stop();
        _sessionSheen = null;
        var sheen = Find<Rectangle>("SessionSheen");
        sheen.Opacity = 0;
        ((TranslateTransform)sheen.RenderTransform!).X = -160;
    }
}
