using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Models.Program;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>
/// Programs slice 5: the ignition rig (WPF MainWindow.ProgramsFx.cs) on the run view. Read-only
/// service; the fixture file's bytes are asserted unchanged.
/// </summary>
public sealed class ProgramIgnitionTests
{
    [Fact]
    public void IgnitedRunLightsTheRigAndItsClocksFollowTheGates() => WithRun(lastDay: true, (shell, tab, svc, program) =>
    {
        T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
        Assert.Equal(ProgramHeatTier.Ignited, tab.IgnitionTier);
        Assert.True(tab.IgnitionLoopsRunning);
        Assert.IsType<LinearGradientBrush>(F<Border>("TodayPanel").BorderBrush);   // T2+ turning border
        Assert.NotNull(F<Rectangle>("ProgramsEdgeGlow").Fill);                    // T4 edge glow
        Assert.NotEqual(default, F<Border>("RailProgressFill").BoxShadow);        // T2+ rail glow, a BoxShadow
        Assert.DoesNotContain(global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tab), v => v.Effect != null);   // P82: no Effect under the loops
        var particles = tab.IgnitionParticles!;                                   // through the shared canvas
        Assert.Same(F<Grid>("TodayFxLayer"), particles.Parent);
        var registry = (System.Collections.IDictionary)typeof(MainShellWindow).GetField("_tabFxCanvases",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!.GetValue(shell)!;
        Assert.Contains(particles, (System.Collections.Generic.List<AmbientFxCanvas>)registry["programs"]!);   // RegisterTabFx("programs")

        // P39 + P73: the clocks really advance - tick the render timer, time each frame; the same
        // frames with the loops parked (Motion Reduced, below) are the baseline.
        var edge = F<Rectangle>("ProgramsEdgeGlow");
        Assert.True(F<Grid>("RailCometHost").IsVisible);                        // T4 rail comet

        // On WPF's curves, stepped (P08): edge 0.55 x peak -> peak over breath x 2.4 s sine (ProgramsFx.cs:631),
        // the border a linear lap (:526), the comet -140 -> rail + 40 (:745).
        var heat = ProgramHeat.Compute(program.LengthDays, program.LengthDays, svc.Today?.Intensity ?? 0, svc.Today?.IsBoss == true);
        var (edgePeak, breath, lap) = (ProgramHeat.EdgeGlowOpacity(heat), ProgramHeat.BreathSeconds(heat), ProgramHeat.BorderLapSeconds(heat));
        var spin = (RotateTransform)F<Border>("TodayPanel").BorderBrush!.Transform!;
        var cometX = (TranslateTransform)F<Rectangle>("RailComet").RenderTransform!;
        // StepAll skips the FrameClock, so prove every rig loop also rides the live window's frames.
        var rig = BeatLoop.Running.Where(l => l.Owner == tab || global::Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(tab, l.Owner)).ToArray();
        Assert.True(rig.Length >= 6, $"only {rig.Length} rig loops");          // sigil x2 (if art), border, wash, shimmer, edge, comet
        Assert.All(rig, l => Assert.True(l.OnFrameBeat, $"{l.Owner.GetType().Name}/{(l.Owner as Control)?.Name} ticking={l.IsTicking} parked={l.Parked}"));
        Assert.Equal(edgePeak * 0.55, edge.Opacity, 3);
        Assert.Equal(0, spin.Angle, 3);
        Assert.Equal(-140, cometX.X, 3);
        Step(breath * 2.4);
        Assert.Equal(edgePeak, edge.Opacity, 3);
        Assert.Equal(360 * (breath * 2.4 / lap % 1), spin.Angle, 1);
        var log = new StringBuilder("phase,frame,ms,edgeOpacity,borderAngle,shimmerX,cometX\n");
        var seen = Frames("ignited", log, tab);
        Assert.True(seen.Edge > 3, $"edge glow did not breathe ({seen.Edge} values)");
        // The loops that animate a brush's own transform move too (border spin, counter shimmer, comet).
        Assert.True(seen.Angle > 3, $"border did not turn ({seen.Angle} values)");
        Assert.True(seen.Shimmer > 3, $"counter shimmer did not move ({seen.Shimmer} values)");
        Assert.True(seen.Comet > 3, $"comet did not travel ({seen.Comet} values)");


        // P01: the shell hides the tab -> every clock stops and the rim parks dark.
        tab.IsVisible = false;
        Assert.False(tab.IgnitionLoopsRunning);
        Assert.Equal(0, edge.Opacity);
        tab.IsVisible = true;
        Assert.True(tab.IgnitionLoopsRunning);

        // Motion Reduced: lit, but static (WPF "T0 visuals" for loops; the edge sits at its peak).
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.False(tab.IgnitionLoopsRunning);
        Assert.Equal(1, Frames("parked", log, tab).Edge);
        if (Environment.GetEnvironmentVariable("CCP_FX_EVIDENCE") is { Length: > 0 } evidence)
            File.WriteAllText(evidence, log.ToString());
        Assert.Equal(ProgramHeat.EdgeGlowOpacity(ProgramHeat.Compute(program.LengthDays, program.LengthDays,
            svc.Today?.Intensity ?? 0, svc.Today?.IsBoss == true)), edge.Opacity, 3);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.True(tab.IgnitionLoopsRunning);

        // The window gate is minimise only, like WPF (ProgramsFx.cs:818); headless never deactivates a
        // shown window, so focus loss (dust parks itself, loops keep running) is untested here.
        shell.WindowState = WindowState.Minimized;
        Assert.False(tab.IgnitionLoopsRunning);
        shell.WindowState = WindowState.Normal;
        Assert.True(tab.IgnitionLoopsRunning);

        // P65: detaching the tab (window still active and normal) stops everything.
        var host = (Panel)tab.Parent!;
        host.Children.Remove(tab);
        Dispatcher.UIThread.RunJobs();
        Assert.False(tab.IgnitionLoopsRunning);
        host.Children.Add(tab);
    });

    [Fact]
    public void FirstDayStaysBelowChargedWithNoParticlesRimOrComet() => WithRun(lastDay: false, (_, tab, svc, program) =>
    {
        var today = svc.Today!;
        Assert.Equal(ProgramHeat.TierOf(ProgramHeat.Compute(1, program.LengthDays, today.Intensity, today.IsBoss)), tab.IgnitionTier);
        Assert.True(tab.IgnitionTier < ProgramHeatTier.Charged);
        Assert.Null(tab.IgnitionParticles);
        Assert.IsNotType<LinearGradientBrush>(tab.FindControl<Border>("TodayPanel")!.BorderBrush);
        Assert.Null(tab.FindControl<Rectangle>("ProgramsEdgeGlow")!.Fill);
        Assert.False(tab.FindControl<Grid>("RailCometHost")!.IsVisible);
    });

    [Fact]
    public void DayChapterAndGraduationMomentsFireOncePerRun() => WithRun(lastDay: false, (_, tab, svc, program) =>
    {
        T F<T>(string n) where T : Control => tab.FindControl<T>(n)!;
        var enrollment = svc.ActiveEnrollment!;

        // Day complete: the day's real XP floats up (WPF PlayProgramDayCompleteMoment).
        Assert.Equal("", F<TextBlock>("TxtTodayXpFloat").Text ?? "");
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        Assert.Equal(Loc.GetF("label_0_xp_3", ProgramHeat.DayXp(svc.Today!.Intensity, svc.Today.IsBoss)),
            F<TextBlock>("TxtTodayXpFloat").Text);
        F<TextBlock>("TxtTodayXpFloat").Text = "";
        tab.RefreshPrograms();
        Assert.Equal("", F<TextBlock>("TxtTodayXpFloat").Text);   // once per day

        // Chapter seal: only a chapter closing after the run's baseline stamps.
        var chapter = program.Chapters[0];
        enrollment.CompletedChapterIds.Add(chapter.Id);
        tab.RefreshPrograms();
        Assert.True(F<Grid>("ProgramsSealHost").IsVisible);
        Assert.Equal(chapter.Name, F<TextBlock>("TxtProgramsSealTitle").Text);

        // Graduation: the card's accent hands over to gold (Motion Off snaps, WPF :1172).
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        enrollment.State = ProgramEnrollmentState.Graduated;
        tab.RefreshPrograms();
        Assert.Equal(Color.FromRgb(0xFF, 0xD7, 0x00),
            ((ISolidColorBrush)F<Border>("ProgramsGraduatedCard").BorderBrush!).Color);
    });

    [Fact]
    public void TodaysPipBreathesOnTheBeatAndParksWithEveryGate() => WithRun(lastDay: true, (shell, tab, _, _) =>
    {
        var glow = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tab)
            .OfType<Ellipse>().Single(e => e.Name == "PipGlow" && e.DataContext is ProgramDayPip { IsCurrent: true });
        var scale = (ScaleTransform)glow.RenderTransform!;
        Assert.Equal(.35, glow.Opacity, 3);
        Assert.Equal(.9, scale.ScaleX, 3);
        Assert.Contains(BeatLoop.Running, loop => loop.Owner == glow && loop.OnFrameBeat);
        Step(1.2);
        Assert.Equal(.95, glow.Opacity, 3);
        Assert.Equal(1.12, scale.ScaleX, 3);
        using (var image = shell.CaptureRenderedFrame())
            Assert.NotNull(image);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(.65, glow.Opacity, 3);
        Assert.Equal(1, scale.ScaleX, 3);
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == glow);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Dispatcher.UIThread.RunJobs();
        shell.WindowState = WindowState.Minimized;
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == glow);
        shell.WindowState = WindowState.Normal;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(BeatLoop.Running, loop => loop.Owner == glow);
        tab.IsVisible = false;
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == glow);
        Assert.Equal(.65, glow.Opacity, 3);
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void CompletedDayIgnitesItsOwnNodeOnceAndNeverReplays(MotionLevel motion) => WithRun(lastDay: false, (_, tab, svc, _) =>
    {
        CoreSettings.Current.MotionLevel = motion;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var flare = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tab)
            .OfType<Ellipse>().Single(e => e.Name == "PipIgnite" && e.DataContext is ProgramDayPip { IsCurrent: true });
        var scale = (ScaleTransform)flare.RenderTransform!;
        Assert.Equal(0, flare.Opacity);
        Step(.18);
        if (motion == MotionLevel.Off) Assert.Equal(0, flare.Opacity);
        else
        {
            Assert.Equal(.95, flare.Opacity, 3);
            Assert.InRange(scale.ScaleX, .6, 1.35);
        }
        Step(.72);
        Assert.Equal(0, flare.Opacity, 3);
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == flare);
        // Gate changes reuse carriers; a finished/off-gated moment must remain consumed.
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(BeatLoop.Running, loop => loop.Owner == flare);
        tab.RefreshPrograms();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(tab.FindControl<ItemsControl>("ProgramDayStrip")!.Items.Cast<ProgramDayPip>(), pip => pip.Ignite);
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void TaskCardAndTickPopOnlyAfterAnObservedCompletion(MotionLevel motion) => WithRun(false, (_, tab, svc, _) =>
    {
        CoreSettings.Current.MotionLevel = motion;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        var task = svc.Today!.Tasks.First();
        svc.TodayRecord!.CompletedTaskIds.Add(task.Id);
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        var card = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(tab)
            .OfType<Border>().Single(b => b.Name == "TaskCard" && b.DataContext is ProgramTaskItem t && t.TaskId == task.Id);
        var chip = global::Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(card)
            .OfType<Border>().Single(b => b.Name == "TaskDoneChip");
        var scale = (ScaleTransform)card.RenderTransform!;
        var tick = (ScaleTransform)chip.RenderTransform!;
        Assert.Equal(motion == MotionLevel.Off ? 1 : .93, scale.ScaleX, 3);
        Assert.Equal(motion == MotionLevel.Off ? 1 : .2, tick.ScaleX, 3);
        Step(.3);
        if (motion != MotionLevel.Off) Assert.True(scale.ScaleX > 1); // WPF BackEase overshoot
        Step(.12);
        Assert.Equal(1, scale.ScaleX, 3);
        Assert.Equal(1, tick.ScaleX, 3);
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(BeatLoop.Running, b => b.Owner is Border { Name: "TaskCard" or "TaskDoneChip" });
    });

    [Theory]
    [InlineData(MotionLevel.Full)]
    [InlineData(MotionLevel.Reduced)]
    [InlineData(MotionLevel.Off)]
    public void DayBannerSpringsOnceAndParksOnHide(MotionLevel motion) => WithRun(false, (_, tab, svc, _) =>
    {
        CoreSettings.Current.MotionLevel = motion;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        var banner = tab.FindControl<Border>("TodayCompleteBanner")!;
        var scale = (ScaleTransform)banner.RenderTransform!;
        Assert.Equal(motion == MotionLevel.Off ? 1 : .9, scale.ScaleX, 3);
        Step(.3);
        if (motion != MotionLevel.Off) Assert.True(scale.ScaleX > 1);
        tab.IsVisible = false;
        Assert.Equal(1, scale.ScaleX, 3);
        Assert.DoesNotContain(BeatLoop.Running, b => b.Owner == banner);
        tab.IsVisible = true;
        Assert.DoesNotContain(BeatLoop.Running, b => b.Owner == banner);
    });

    [Fact]
    public void SealUsesWpfBackLandingAndCollapsesAtOnePointSixSeconds() => WithRun(false, (_, tab, svc, program) =>
    {
        svc.ActiveEnrollment!.CompletedChapterIds.Add(program.Chapters[0].Id);
        tab.RefreshPrograms();
        var host = tab.FindControl<Grid>("ProgramsSealHost")!;
        var stamp = tab.FindControl<StackPanel>("ProgramsSealStamp")!;
        var scale = (ScaleTransform)stamp.RenderTransform!;
        Assert.Equal(1.35, scale.ScaleX, 3);
        Step(.22);
        Assert.Equal(1, host.Opacity, 3);
        Assert.True(scale.ScaleX < 1); // WPF BackEase(.5), not a linear landing
        Step(.08);
        Assert.Equal(1, scale.ScaleX, 3);
        Step(1.3);
        Assert.Equal(1.06, scale.ScaleX, 3);
        Assert.False(host.IsVisible);
        Assert.Equal(0, host.Opacity);
    });

    [Fact]
    public void SessionTickUpdatesOnlyTheLiveStripAndSheenObeysGates() => WithRun(true, (shell, tab, svc, _) =>
    {
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var old = AvApp.Sessions;
        var provider = CoreSession.IsSessionRunningProvider;
        var runner = new ConditioningControlPanel.Services.SessionRunner(new ConditioningControlPanel.Services.SessionLogService());
        void Set(string name, object value) => typeof(ConditioningControlPanel.Services.SessionRunner)
            .GetField(name, flags)!.SetValue(runner, value);
        var session = new Session { Id = "program-fx-test", DurationMinutes = 60 };
        Set("<CurrentSession>k__BackingField", session);
        Set("<IsRunning>k__BackingField", true);
        Set("_startTime", DateTime.Now);
        Set("_pausedElapsed", TimeSpan.FromMinutes(15));
        typeof(ProgramService).GetField("_expectedSessionId", flags)!.SetValue(svc, session.Id);
        AvApp.Sessions = runner;
        try
        {
            tab.RefreshPrograms();
            tab.UpdateLayout();
            shell.OnSessionTick();
            var tasks = tab.FindControl<ItemsControl>("TodayTaskList")!.ItemsSource;
            var bar = tab.FindControl<ProgressBar>("TodaySessionProgressBar")!;
            var row = tab.FindControl<Grid>("TodaySessionProgressRow")!;
            var text = tab.FindControl<TextBlock>("TxtTodaySessionProgress")!;
            var sheen = tab.FindControl<Rectangle>("SessionSheen")!;
            Assert.True(row.IsVisible);
            Assert.InRange(bar.Value, 25, 25.1);
            Assert.Equal(Loc.GetF("programs_session_progress", "15:00", "1:00:00"), text.Text);
            Assert.Equal(1, sheen.Opacity);
            var slide = (TranslateTransform)sheen.RenderTransform!;
            Step(.5);
            Assert.True(slide.X > -160);
            Set("<IsPaused>k__BackingField", true);
            shell.OnSessionTick();
            Assert.Equal(Loc.GetF("programs_session_progress_paused",
                Loc.GetF("programs_session_progress", "15:00", "1:00:00")), text.Text);
            Assert.Equal(0, sheen.Opacity);
            Set("<IsPaused>k__BackingField", false);
            shell.OnSessionTick();
            Assert.Equal(1, sheen.Opacity);
            CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.Equal(0, sheen.Opacity);
            Assert.True(row.IsVisible);
            CoreSettings.Current.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            shell.WindowState = WindowState.Minimized;
            Assert.Equal(0, sheen.Opacity);
            shell.WindowState = WindowState.Normal;
            Assert.Equal(1, sheen.Opacity);
            Assert.Same(tasks, tab.FindControl<ItemsControl>("TodayTaskList")!.ItemsSource);
            session.Id = "foreign-session";
            shell.OnSessionTick();
            Assert.False(row.IsVisible);
            Assert.Equal(0, bar.Value);
            Assert.Equal(0, sheen.Opacity);
        }
        finally
        {
            Set("<IsRunning>k__BackingField", false);
            AvApp.Sessions = old;
            CoreSession.IsSessionRunningProvider = provider;
        }
    });

    [Fact]
    public void TaskSealAndGraduationBurstOnceAndPanicStopsTheNewSurface() => WithRun(true, (shell, tab, svc, program) =>
    {
        // Off consumes completion without sparks; returning to Full must not replay it.
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        svc.TodayRecord!.CompletedTaskIds.Add(svc.Today!.Tasks.First().Id);
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, tab.CompletionBursts);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        AmbientFxCanvas.Env.RaiseMotionGateChanged();
        var next = svc.Today.Tasks.Skip(1).First();
        svc.TodayRecord.CompletedTaskIds.Add(next.Id);
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, tab.CompletionBursts);
        svc.ActiveEnrollment!.CompletedChapterIds.Add(program.Chapters[0].Id);
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, tab.CompletionBursts);
        var panic = PanicSurfaces.All.Single(s => s.Id == "programs-fx");
        panic.Stop(shell);
        Assert.False(tab.FindControl<Grid>("ProgramsSealHost")!.IsVisible);
        Assert.DoesNotContain(BeatLoop.Running, b => b.Owner == tab ||
            global::Avalonia.VisualTree.VisualExtensions.IsVisualAncestorOf(tab, b.Owner));
        svc.ActiveEnrollment.State = ProgramEnrollmentState.Graduated;
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, tab.CompletionBursts);
        tab.RefreshPrograms();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, tab.CompletionBursts);
    });

    [Fact]
    public void DayCompletionUsesItsOwnBannerBurstAndCancelsPendingBurstsOnHide() => WithRun(false, (_, tab, svc, _) =>
    {
        svc.TodayRecord!.DayCompleted = true;
        tab.RefreshPrograms();
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, tab.CompletionBursts);
        tab.RefreshPrograms();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, tab.CompletionBursts);
        // A different task completion queued immediately before hiding may not leak onto another tab.
        svc.TodayRecord.CompletedTaskIds.Add(svc.Today!.Tasks.First().Id);
        tab.RefreshPrograms();
        tab.IsVisible = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, tab.CompletionBursts);
    });

    /// <summary>40 beats of stepped time, time-bounded (P73); returns how many distinct values each loop showed:
    /// edge opacity, the border brush's rotation, the counter ink's X offset and the comet's X offset.</summary>
    private static (int Edge, int Angle, int Shimmer, int Comet) Frames(string phase, StringBuilder log, ProgramsTabView tab)
    {
        var edge = tab.FindControl<Rectangle>("ProgramsEdgeGlow")!;
        var panel = tab.FindControl<Border>("TodayPanel")!;
        var counter = tab.FindControl<TextBlock>("TxtRunDayCounter")!;
        var comet = tab.FindControl<Rectangle>("RailComet")!;
        double Of<T>(object? t, Func<T, double> read) where T : class => t is T x ? read(x) : double.NaN;
        var seen = new[] { new System.Collections.Generic.HashSet<double>(), new(), new(), new() };
        var watch = Stopwatch.StartNew();
        for (var i = 0; i < 40 && watch.Elapsed < TimeSpan.FromSeconds(10); i++)
        {
            var t0 = watch.Elapsed.TotalMilliseconds;
            Step(1.0 / 30);                                                          // one 30 fps beat
            var v = new[]
            {
                edge.Opacity,
                Of<RotateTransform>((panel.BorderBrush as IBrush)?.Transform, r => r.Angle),
                Of<TranslateTransform>((counter.Foreground as IBrush)?.Transform, r => r.X),
                Of<TranslateTransform>(comet.RenderTransform, r => r.X),
            };
            for (var k = 0; k < 4; k++) seen[k].Add(Math.Round(v[k], 4));
            log.Append(FormattableString.Invariant(
                $"{phase},{i},{watch.Elapsed.TotalMilliseconds - t0:0.00},{v[0]:0.0000},{v[1]:0.00},{v[2]:0.00},{v[3]:0.00}\n"));
        }
        return (seen[0].Count, seen[1].Count, seen[2].Count, seen[3].Count);
    }

    private static SteppedTime _time = new();

    private static void Step(double seconds)
    {
        _time.Advance(TimeSpan.FromSeconds(seconds));
        BeatLoop.StepAll();
    }

    private static void WithRun(bool lastDay, Action<MainShellWindow, ProgramsTabView, ProgramService, ProgramDefinition> body) =>
        AvaloniaTestDispatcher.Run(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var program = BuiltInPrograms.All().First(p => p.Id == "first_week");
            var day = lastDay ? program.LengthDays : 1;
            var today = DateTime.Today;
            var state = new ProgramState
            {
                Active = new ProgramEnrollment
                {
                    ProgramId = program.Id, StartedAt = today.AddDays(1 - day), CurrentDay = day,
                    CurrentDayDate = today, State = ProgramEnrollmentState.Active,
                }
            };
            for (var i = 1; i < day; i++) state.Active.GetOrCreateRecord(i, today.AddDays(i - day)).DayCompleted = true;
            state.Active.GetOrCreateRecord(day, today);

            var dir = Directory.CreateTempSubdirectory("ccp-programs-ignition-").FullName;
            var path = System.IO.Path.Combine(dir, "programs.json");
            File.WriteAllText(path, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            var bytes = File.ReadAllBytes(path);

            var oldPrograms = AvApp.Programs;
            var settings = CoreSettings.Current;
            var (motion, perf) = (settings.MotionLevel, settings.PerformanceMode);
            var (seenTab, seenIntro) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro);
            settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            MainShellWindow? shell = null;
            BeatLoop.Time = _time = new SteppedTime();
            try
            {
                var svc = AvApp.Programs = new ProgramService(path, readOnly: true);
                shell = new MainShellWindow { Width = 1400, Height = 900 };
                shell.Show();
                shell.ShowTab("programs");
                Dispatcher.UIThread.RunJobs();
                var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
                tab.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                Assert.True(tab.FindControl<StackPanel>("ProgramsRunPanel")!.IsVisible);
                body(shell, tab, svc, program);
                Assert.Equal(bytes, File.ReadAllBytes(path));   // display only: nothing written
            }
            finally
            {
                shell?.Close();
                Dispatcher.UIThread.RunJobs();
                AvApp.Programs?.Dispose();
                AvApp.Programs = oldPrograms;
                (settings.MotionLevel, settings.PerformanceMode) = (motion, perf);
                (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro) = (seenTab, seenIntro);
                Directory.Delete(dir, true);
                BeatLoop.Time = TimeProvider.System;
            }
        });
}
