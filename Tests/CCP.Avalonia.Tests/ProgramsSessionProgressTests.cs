using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ProgramsSessionProgressTests
{
    private static void Elapsed(SessionRunner runner, TimeSpan elapsed) => runner._pausedElapsed = elapsed;

    [Fact]
    public Task SheenStartsWhenLayoutProvidesWidthWithoutAnotherEngineTick() => Run(async (shell, tab, svc, runner) =>
    {
        var host = tab.FindControl<Border>("SessionBarHost")!;
        host.Width = 40;
        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        tab.UpdateLayout();
        tab.RefreshLiveSessionProgress();
        Assert.Equal(40, host.Bounds.Width);
        Assert.False(tab.SessionSheenRunning);
        var tasks = tab.FindControl<ItemsControl>("TodayTaskList")!.ItemsSource;
        host.Width = 400;
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(400, host.Bounds.Width);
        Assert.True(tab.SessionSheenRunning); // no tick or explicit refresh after layout
        Assert.Same(tasks, tab.FindControl<ItemsControl>("TodayTaskList")!.ItemsSource);
        runner.Pause();
        host.Width = 420;
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.False(tab.SessionSheenRunning);
        PanicSurfaces.StopAll("layout-retry", shell);
        host.Width = 440;
        tab.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Assert.False(tab.SessionSheenRunning);
    });

    [Fact]
    public Task LiveClockBarAndSheenFollowTheSessionWithoutRebuildingTasks() => Run(async (shell, tab, svc, runner) =>
    {
        T F<T>(string name) where T : Control => tab.FindControl<T>(name)!;
        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(F<Grid>("TodaySessionProgressRow").IsVisible);
        Assert.True(F<TextBlock>("TxtTodaySessionProgress").IsVisible);
        shell.OnSessionTick(); // next engine tick after layout has measured the bar
        var total = runner.CurrentSession!.DurationMinutes;
        Assert.Equal(60, total); // First Week's final day exercises the hour clock shape
        var tasks = F<ItemsControl>("TodayTaskList").ItemsSource; // start may rebuild; ticks must not
        var sheen = F<global::Avalonia.Controls.Shapes.Rectangle>("SessionSheen");
        Assert.Contains(BeatLoop.Running, l => l.Owner == sheen && l.OnFrameBeat);

        var originalElapsed = runner.Elapsed;
        runner.Pause();
        Elapsed(runner, TimeSpan.FromMinutes(2));
        shell.OnSessionTick();
        var clock = Loc.GetF("programs_session_progress", "2:00", "1:00:00");
        Assert.Equal(Loc.GetF("programs_session_progress_paused", clock), F<TextBlock>("TxtTodaySessionProgress").Text);
        Assert.Equal(200d / total, F<ProgressBar>("TodaySessionProgressBar").Value, 3);
        Assert.Same(tasks, F<ItemsControl>("TodayTaskList").ItemsSource);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == sheen);
        Assert.Equal(0, sheen.Opacity);
        if (Environment.GetEnvironmentVariable("CCP_PROGRAM_PROGRESS_PNG") is { Length: > 0 } png)
        {
            F<Grid>("TodaySessionProgressRow").BringIntoView();
            Dispatcher.UIThread.RunJobs();
            using var frame = shell.CaptureRenderedFrame();
            Assert.NotNull(frame);
            frame.Save(png);
        }

        Elapsed(runner, originalElapsed); // keep the runner's real monotonic clock consistent on resume
        runner.Resume();
        shell.OnSessionTick();
        Assert.DoesNotContain(Loc.GetF("programs_session_progress_paused", "").Trim(),
            F<TextBlock>("TxtTodaySessionProgress").Text);
        Assert.Contains(BeatLoop.Running, l => l.Owner == sheen);
        shell.WindowState = WindowState.Minimized;
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == sheen);
        shell.WindowState = WindowState.Normal;
        Assert.Contains(BeatLoop.Running, l => l.Owner == sheen);
        CoreSettings.Current.MotionLevel = MotionLevel.Reduced;
        ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == sheen);
        Assert.True(F<Grid>("TodaySessionProgressRow").IsVisible);
        CoreSettings.Current.MotionLevel = MotionLevel.Full;
        ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.RaiseMotionGateChanged();
        Assert.Contains(BeatLoop.Running, l => l.Owner == sheen);
        var beforeHide = F<TextBlock>("TxtTodaySessionProgress").Text;
        tab.IsVisible = false;
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == sheen);
        runner.Pause();
        Elapsed(runner, TimeSpan.FromMinutes(3));
        shell.OnSessionTick();
        Assert.Equal(beforeHide, F<TextBlock>("TxtTodaySessionProgress").Text); // hidden: no repaint
        tab.IsVisible = true;
        Assert.Equal(Loc.GetF("programs_session_progress_paused",
            Loc.GetF("programs_session_progress", "3:00", "1:00:00")), F<TextBlock>("TxtTodaySessionProgress").Text);
    });

    [Fact]
    public Task PanicAndForeignSessionsClearTheProgramProgressStrip() => Run(async (shell, tab, svc, runner) =>
    {
        T F<T>(string name) where T : Control => tab.FindControl<T>(name)!;
        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(F<Grid>("TodaySessionProgressRow").IsVisible);
        PanicSurfaces.StopAll("program-progress-test", shell);
        Dispatcher.UIThread.RunJobs();
        Assert.False(runner.IsRunning);
        Assert.False(F<Grid>("TodaySessionProgressRow").IsVisible);
        Assert.False(F<TextBlock>("TxtTodaySessionProgress").IsVisible);
        Assert.Equal(0, F<ProgressBar>("TodaySessionProgressBar").Value);
        Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == F<global::Avalonia.Controls.Shapes.Rectangle>("SessionSheen"));
        shell.StartSession(new Session { Id = "foreign-test", Name = "Foreign", DurationMinutes = 20 });
        Dispatcher.UIThread.RunJobs();
        Assert.True(runner.IsRunning);
        Assert.False(F<Grid>("TodaySessionProgressRow").IsVisible);
        Assert.Equal(Loc.Get("programs_session_other_running"), F<TextBlock>("TxtStartTodaySession").Text);
    });

    private static Task Run(Func<MainShellWindow, ProgramsTabView, ProgramService, SessionRunner, Task> body) =>
        AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var dir = Directory.CreateTempSubdirectory("ccp-program-progress-").FullName;
            var (programs, sessions, available) = (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider);
            var settings = CoreSettings.Current;
            var saved = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel, settings.PerformanceMode);
            settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            MainShellWindow? shell = null;
            try
            {
                CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
                var svc = AvApp.Programs = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: false);
                var program = svc.Library.First(p => p.Id == "first_week");
                var enrollment = svc.Enroll(program)!;
                enrollment.CurrentDay = program.LengthDays; // warm/ignited heat: sheen is owed
                enrollment.GetOrCreateRecord(enrollment.CurrentDay, enrollment.CurrentDayDate);
                var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
                shell = new MainShellWindow { Width = 1400, Height = 1000 };
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.Show();
                shell.ShowTab("programs");
                Dispatcher.UIThread.RunJobs();
                var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
                await body(shell, tab, svc, runner);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                AvApp.Sessions?.Stop();
                if (shell != null) { foreach (var w in shell.OwnedWindows.ToArray()) w.Close(); shell.Close(); }
                Dispatcher.UIThread.RunJobs();
                AvApp.Programs?.Dispose();
                (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider) = (programs, sessions, available);
                (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel, settings.PerformanceMode) = saved;
                CoreSession.IsSessionRunningProvider = null;
                Directory.Delete(dir, true);
            }
        });
}
