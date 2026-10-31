using System;
using Avalonia.Controls;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Controls.Fx;

namespace ConditioningControlPanel.Avalonia.Views.Tabs;

public partial class ProgramsTabView
{
    private FrameClock? _entranceClock;
    private long _entranceStart;
    internal bool RunEntranceRunning => _entranceClock?.IsEnabled == true;

    // WPF MainWindow.ProgramsTab.cs:1694-1760: interaction motion, including Reduced.
    internal void StartRunEntrance()
    {
        CompleteRunEntrance();
        if (!IsVisible || VisualRoot is null || !Find<StackPanel>("ProgramsRunPanel").IsVisible ||
            !AmbientFxCanvas.Env.AllowTransitions) return;
        _entranceClock ??= CreateEntranceClock();
        _entranceStart = Clock.GetTimestamp();
        StepRunEntrance(0);
        _entranceClock.Start();
    }

    private FrameClock CreateEntranceClock()
    {
        var timer = new FrameClock(this) { Interval = TimeSpan.FromSeconds(1.0 / 60) };
        timer.Tick += (_, _) => StepRunEntrance(Clock.GetElapsedTime(_entranceStart).TotalMilliseconds);
        return timer;
    }

    internal void CompleteRunEntrance() => StepRunEntrance(700);

    internal void StepRunEntrance(double ms)
    {
        static double Ease(double t, double duration) => 1 - Math.Pow(1 - Math.Clamp(t / duration, 0, 1), 2);
        void Panel(string name, double delay)
        {
            var panel = Find<Border>(name);
            var elapsed = ms - delay;
            // WPF delayed animations leave the base value intact until BeginTime.
            panel.Opacity = elapsed < 0 ? 1 : Ease(elapsed, 300);
            if (panel.RenderTransform is not TranslateTransform) panel.RenderTransform = new TranslateTransform();
            ((TranslateTransform)panel.RenderTransform).Y = elapsed < 0 ? 14 : 14 * (1 - Ease(elapsed, 340));
        }
        Panel("RunHeaderPanel", 0);
        Panel("TodayPanel", 90);
        var scale = (ScaleTransform)Find<Border>("RailProgressFill").RenderTransform!;
        scale.ScaleX = ms < 150 ? 1 : 1 - Math.Pow(1 - Math.Clamp((ms - 150) / 550, 0, 1), 3);
        if (ms >= 700) _entranceClock?.Stop();
    }

    private void EntranceMotionChanged()
    {
        if (!AmbientFxCanvas.Env.AllowTransitions) CompleteRunEntrance();
    }
}
