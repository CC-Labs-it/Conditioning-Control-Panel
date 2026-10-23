using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using Xunit;

namespace CCP.Avalonia.Tests;

// Reuse the read-only enrollment + sandboxed shell fixture. Time is stepped, never slept.
public sealed class ProgramsCompletionMotionTests
{
    private static void Pump(ProgramsTabView tab)
    {
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private static T Part<T>(ProgramsTabView tab, string list, string name) where T : Control =>
        tab.FindControl<ItemsControl>(list)!.GetVisualDescendants().OfType<T>().First(c => c.Name == name &&
            (c.DataContext is not ProgramDayPip pip || pip.IsCurrent));

    private static double Scale(Visual target) => ((ScaleTransform)target.RenderTransform!).ScaleX;
    private static void Step(double seconds) => ProgramIgnitionTests.Step(seconds);

    [Fact]
    public void TodayPipBreathesOnSharedBeatAndStopsAtEveryGate() => ProgramIgnitionTests.WithRun(true, (shell, tab, _, _) =>
    {
        Pump(tab);
        var glow = Part<Ellipse>(tab, "ProgramDayStrip", "PipGlow");
        Assert.Equal(0.35, glow.Opacity, 3);
        Assert.Equal(0.9, Scale(glow), 3);
        Assert.Single(BeatLoop.Running, l => l.Owner == glow && l.OnFrameBeat);
        Step(1.2);
        Assert.Equal(0.95, glow.Opacity, 3);
        Assert.Equal(1.12, Scale(glow), 3);
        Step(1.2);
        Assert.Equal(0.35, glow.Opacity, 3);
        foreach (var motion in new[] { MotionLevel.Reduced, MotionLevel.Off })
        {
            CoreSettings.Current.MotionLevel = motion;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Pump(tab);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == glow);
            Assert.Equal(0.65, glow.Opacity, 3);
            Assert.Equal(1, Scale(glow));
        }
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Pump(tab);
        Assert.Contains(BeatLoop.Running, l => l.Owner == glow);
        // A maximise is not a gate: the breath keeps its phase (WPF storyboard keeps running).
        Step(0.3);
        var breath = Assert.Single(BeatLoop.Running, l => l.Owner == glow);
        var midBreath = glow.Opacity;
        Assert.NotEqual(0.35, midBreath, 3);
        shell.WindowState = WindowState.Maximized;
        Pump(tab);
        Assert.Same(breath, Assert.Single(BeatLoop.Running, l => l.Owner == glow));
        Assert.Equal(midBreath, glow.Opacity, 3);
        shell.WindowState = WindowState.Normal;
        Pump(tab);
        shell.WindowState = WindowState.Minimized;
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == glow);
        shell.WindowState = WindowState.Normal;
        Pump(tab);
        Assert.Contains(BeatLoop.Running, l => l.Owner == glow);
        shell.ShowTab("settings");
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == glow);
        shell.ShowTab("programs");
        Pump(tab);
        var parent = (Panel)tab.Parent!;
        parent.Children.Remove(tab);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == glow);
        parent.Children.Add(tab);
    });

    [Fact]
    public void ColdDayKeepsTheStaticHalo() => ProgramIgnitionTests.WithRun(false, (_, tab, svc, program) =>
    {
        // Same authored day on a longer run really falls below Warm.
        var length = svc.ActiveProgram!.LengthDays;
        try
        {
            svc.ActiveProgram.LengthDays = 28;
            svc.Today!.Intensity = 0;
            tab.RefreshPrograms();
            Pump(tab);
            Assert.Equal(ProgramHeatTier.Cold, tab.IgnitionTier);
            var glow = Part<Ellipse>(tab, "ProgramDayStrip", "PipGlow");
            Assert.Equal(0.65, glow.Opacity, 3);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == glow);
        }
        finally { svc.ActiveProgram.LengthDays = length; }
    });

    [Fact]
    public void DayCompletionFlaresAndSpringsOnceIncludingReducedMotion() => ProgramIgnitionTests.WithRun(false, (_, tab, svc, _) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        Pump(tab);
        var flare = Part<Ellipse>(tab, "ProgramDayStrip", "PipIgnite");
        var banner = tab.FindControl<Border>("TodayCompleteBanner")!;
        Assert.Equal(0.9, Scale(banner), 3);
        Assert.Equal(0.6, Scale(flare), 3);
        Step(0.18);
        Assert.Equal(0.95, flare.Opacity, 3);
        Assert.True(Scale(banner) > 1); // WPF BackEase overshoot; opacity stays in range
        Step(0.72);
        Assert.Equal(0, flare.Opacity, 3);
        Assert.Equal(1.35, Scale(flare), 3);
        Assert.Equal(1, Scale(banner), 3);
        tab.RefreshPrograms();
        Pump(tab);
        Assert.False(Part<Ellipse>(tab, "ProgramDayStrip", "PipIgnite").DataContext is ProgramDayPip { Ignite: true });
        Assert.Equal(1, Scale(banner), 3);
        Assert.Equal(0, tab.ProgramBursts); // Reduced removes particles, retains interactions
    });

    [Fact]
    public void TaskTickPopsCardAndChipAndBurstsOnlyAfterSeenIncomplete() => ProgramIgnitionTests.WithRun(false, (shell, tab, svc, _) =>
    {
        shell.Activate();
        Pump(tab);
        Step(1);
        var task = svc.Today!.Tasks[0];
        svc.TodayRecord!.CompletedTaskIds.Add(task.Id);
        tab.RefreshPrograms();
        Pump(tab);
        var card = Part<Border>(tab, "TodayTaskList", "TaskCard");
        var chip = Part<Border>(tab, "TodayTaskList", "TaskDoneChip");
        Assert.True(((ProgramTaskItem)card.DataContext!).JustCompleted);
        Assert.Equal(0.93, Scale(card), 3);
        Assert.Equal(0.2, Scale(chip), 3);
        Assert.Equal(1, tab.ProgramBursts);
        Step(0.25);
        Assert.True(Scale(card) > 1);
        Step(0.17);
        Assert.Equal(1, Scale(chip), 3);
        tab.RefreshPrograms();
        Pump(tab);
        Assert.False(((ProgramTaskItem)Part<Border>(tab, "TodayTaskList", "TaskCard").DataContext!).JustCompleted);
        Assert.Equal(1, tab.ProgramBursts);
        svc.ActiveEnrollment!.AttemptNumber++;
        tab.RefreshPrograms();
        Pump(tab);
        Assert.Equal(1, tab.ProgramBursts); // completed first paint of another attempt is settled
    });

    [Fact]
    public void DaySealAndGraduationBurstsReachTheWindowOverlayOnce() => ProgramIgnitionTests.WithRun(false, (shell, tab, svc, program) =>
    {
        shell.Activate();
        Pump(tab);
        Step(1);
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        Pump(tab);
        Assert.Equal(1, tab.ProgramBursts);
        svc.ActiveEnrollment!.CompletedChapterIds.Add(program.Chapters[0].Id);
        tab.RefreshPrograms();
        Pump(tab);
        var stamp = tab.FindControl<StackPanel>("ProgramsSealStamp")!;
        Assert.Equal(1.35, Scale(stamp), 3);
        Step(0.2);
        Assert.True(Scale(stamp) < 1); // BackEase landing, previously linear
        Assert.Equal(2, tab.ProgramBursts);
        svc.ActiveEnrollment.State = ProgramEnrollmentState.Graduated;
        tab.RefreshPrograms();
        Pump(tab);
        Assert.Equal(3, tab.ProgramBursts);
        tab.RefreshPrograms();
        Pump(tab);
        Assert.Equal(3, tab.ProgramBursts);
    });

    [Fact]
    public void EntranceHasWpfStaggerAndRefreshDoesNotReplayIt() => ProgramIgnitionTests.WithRun(false, (shell, tab, _, _) =>
    {
        Step(1);
        shell.ShowTab("settings");
        shell.ShowTab("programs");
        Pump(tab);
        var header = tab.FindControl<Border>("RunHeaderPanel")!;
        var today = tab.FindControl<Border>("TodayPanel")!;
        var rail = tab.FindControl<Border>("RailProgressFill")!;
        Assert.Equal(0, header.Opacity);
        Assert.Equal(0, today.Opacity);
        Assert.Equal(0, Scale(rail));
        Step(0.09);
        Assert.Equal(0.51, header.Opacity, 3);
        Assert.Equal(0, today.Opacity, 3);
        Step(0.61);
        Assert.Equal(1, header.Opacity);
        Assert.Equal(0, ((TranslateTransform)today.RenderTransform!).Y, 3);
        Assert.Equal(1, Scale(rail), 3);
        tab.RefreshPrograms();
        Pump(tab);
        Assert.Equal(1, header.Opacity);
    });

    [Fact]
    public void OffAndPanicSettleInteractionsAndCancelQueuedSparks() => ProgramIgnitionTests.WithRun(false, (shell, tab, svc, _) =>
    {
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        Pump(tab);
        Assert.Equal(1, Scale(tab.FindControl<Border>("TodayCompleteBanner")!));
        Assert.Equal(0, tab.ProgramBursts);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        svc.TodayRecord.CompletedTaskIds.Add(svc.Today!.Tasks[0].Id);
        tab.RefreshPrograms(); // leave the generated-container callback queued
        Assert.Contains(PanicSurfaces.All, s => s.Id == "program-moments");
        PanicSurfaces.StopAll("test", shell);
        Pump(tab);
        Assert.Equal(0, tab.ProgramBursts);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == tab || tab.IsVisualAncestorOf(l.Owner));
        tab.RefreshPrograms(); // a queued service repaint must not restart after panic
        Pump(tab);
        Assert.DoesNotContain(BeatLoop.Running, l => tab.IsVisualAncestorOf(l.Owner));
    });
}
