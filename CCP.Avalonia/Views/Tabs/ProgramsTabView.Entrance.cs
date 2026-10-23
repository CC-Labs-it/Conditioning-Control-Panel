using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Helpers;
using Env = ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

/// <summary>
/// WPF StartProgramRunEntrance + AnimateProgramPanelIn (ProgramsTab.cs:1694-1760): on tab entry and on
/// the run view's first reveal, the header and Today panel fade in (300 ms, quadratic out) and slide up
/// 14 px (340 ms), Today 90 ms later, while the rail fill grows from the left (150 ms delay, 550 ms,
/// cubic out). Interaction motion: only Motion Off skips it, and then everything sits at rest.
/// One BeatLoop frame driver (P82: no Animation on a transform), parked while hidden or minimised.
/// </summary>
public partial class ProgramsTabView
{
    private BeatLoop? _runEntrance;
    internal const double RunEntranceSeconds = 0.7;   // the rail grow ends last: 150 + 550 ms

    internal bool RunEntranceRunning => _runEntrance?.IsRunning == true;

    internal void StartRunEntrance()
    {
        StopRunEntrance();
        if (!Env.AllowTransitions) return;
        var header = Find<Border>("RunHeaderPanel");
        var today = Find<Border>("TodayPanel");
        var rail = (ScaleTransform)Find<Border>("RailProgressFill").RenderTransform!;
        var headerSlide = new TranslateTransform();
        var todaySlide = new TranslateTransform();
        header.RenderTransform = headerSlide;
        today.RenderTransform = todaySlide;
        // WPF holds the base value until each BeginTime; holding the start value avoids that one-frame flash.
        _runEntrance = new BeatLoop(header, t =>
        {
            PanelIn(header, headerSlide, t);
            PanelIn(today, todaySlide, t - 0.09);
            rail.ScaleX = Ease(t - 0.15, 0.55, cubic: true);
            if (t >= RunEntranceSeconds) StopRunEntrance();
        });
        _runEntrance.Start();
    }

    /// <summary>Stops the entrance and leaves every surface at rest (WPF's Off branch).</summary>
    internal void StopRunEntrance()
    {
        _runEntrance?.Stop();
        _runEntrance = null;
        var header = Find<Border>("RunHeaderPanel");
        var today = Find<Border>("TodayPanel");
        header.Opacity = today.Opacity = 1;
        header.RenderTransform = today.RenderTransform = null;
        ((ScaleTransform)Find<Border>("RailProgressFill").RenderTransform!).ScaleX = 1;
    }

    private static void PanelIn(Border panel, TranslateTransform slide, double t)
    {
        panel.Opacity = Ease(t, 0.30, cubic: false);
        slide.Y = 14 * (1 - Ease(t, 0.34, cubic: false));
    }

    private static double Ease(double t, double duration, bool cubic)
    {
        var p = Math.Clamp(t / duration, 0, 1);
        var inv = 1 - p;
        return cubic ? 1 - inv * inv * inv : 1 - inv * inv;
    }
}
