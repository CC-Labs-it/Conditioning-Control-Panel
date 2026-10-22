using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Program;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ProgramsSessionSheenTests
{
    [Fact]
    public Task SessionSheenSweepsOnTheSharedBeatAndStopsAtEveryGate() => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-program-sheen-").FullName;
        var (oldPrograms, oldSessions, oldTask, oldTime) = (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider, BeatLoop.Time);
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro, motion, perf) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel, settings.PerformanceMode);
        var skipPauseWarning = settings.SkipPauseXpWarning;
        MainShellWindow? shell = null;
        try
        {
            settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
            settings.MotionLevel = MotionLevel.Full;
            settings.PerformanceMode = false;
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            var svc = AvApp.Programs = new ProgramService(System.IO.Path.Combine(dir, "programs.json"), readOnly: false);
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            var program = svc.GetProgram("first_week")!;
            // A valid longer catalogue entry gives both a genuinely Cold day one and a boss day.
            var bossDay = program.LengthDays;
            var first = program.GetDay(1)!;
            for (var day = bossDay + 1; day <= 28; day++)
                program.Chapters[^1].Days.Add(new ConditioningControlPanel.Models.Program.ProgramDay
                {
                    DayIndex = day, Title = $"Day {day}", SessionTemplateId = first.SessionTemplateId,
                    SessionMinutes = first.SessionMinutes, Intensity = first.Intensity,
                });
            program.LengthDays = 28;
            Assert.True(program.Validate(out var invalid), invalid);
            var enrollment = svc.Enroll(program)!;
            enrollment.CurrentDay = bossDay; // authored boss day heats the rig
            enrollment.GetOrCreateRecord(enrollment.CurrentDay, enrollment.CurrentDayDate);
            var clock = new SteppedTime();
            BeatLoop.Time = clock;
            shell = new MainShellWindow { Width = 1400, Height = 900 };
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.Show();
            shell.ShowTab("programs");
            Dispatcher.UIThread.RunJobs();
            var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
            tab.UpdateLayout();
            await shell.StartProgramSessionAsync();
            Dispatcher.UIThread.RunJobs();
            tab.UpdateLayout();
            shell.OnSessionTick(); // retries after the progress row has been measured
            var sheen = tab.FindControl<Rectangle>("SessionSheen")!;
            var slide = (TranslateTransform)sheen.RenderTransform!;
            Assert.True(tab.SessionSheenRunning);
            Assert.Equal(1, sheen.Opacity);
            Assert.Equal(-160, slide.X, 2);
            var loop = Assert.Single(BeatLoop.Running, l => l.Owner == sheen);
            Assert.True(loop.OnFrameBeat);
            if (Environment.GetEnvironmentVariable("CCP_PROGRAM_SESSION_RENDER") is { Length: > 0 } render)
            {
                tab.FindControl<Grid>("TodaySessionProgressRow")!.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                tab.UpdateLayout();
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)tab.Bounds.Width, (int)tab.Bounds.Height));
                bitmap.Render(tab);
                bitmap.Save(render);
            }
            clock.Seconds = ProgramHeat.SheenSweepSeconds(ProgramHeat.Compute(enrollment.CurrentDay,
                program.LengthDays, svc.Today!.Intensity, svc.Today.IsBoss)) / 2;
            BeatLoop.StepAll();
            Assert.Equal(-160 + (tab.FindControl<Border>("SessionBarHost")!.Bounds.Width + 220) / 2, slide.X, 2);

            // The real pause path (BtnPauseSession_Click -> full row refresh), no tick in between.
            settings.SkipPauseXpWarning = true;
            var pause = shell.Named<Button>("BtnPauseSession")!;
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.True(runner.IsPaused);
            Assert.False(tab.SessionSheenRunning);
            Assert.Equal(0, sheen.Opacity);
            pause.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs();
            Assert.False(runner.IsPaused);
            Assert.True(tab.SessionSheenRunning);
            tab.IsVisible = false;
            Assert.False(tab.SessionSheenRunning);
            tab.IsVisible = true;
            Assert.True(tab.SessionSheenRunning);
            settings.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.False(tab.SessionSheenRunning);
            settings.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            Assert.True(tab.SessionSheenRunning);
            shell.WindowState = WindowState.Minimized;
            Assert.False(tab.SessionSheenRunning);
            shell.WindowState = WindowState.Normal;
            Assert.True(tab.SessionSheenRunning);
            PanicSurfaces.StopAll("test", shell);
            Dispatcher.UIThread.RunJobs();
            Assert.False(runner.IsRunning);
            Assert.False(tab.SessionSheenRunning);
            Assert.False(tab.FindControl<Grid>("TodaySessionProgressRow")!.IsVisible);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == sheen);
            enrollment.CurrentDay = 1;
            tab.RefreshPrograms();
            await shell.StartProgramSessionAsync();
            Dispatcher.UIThread.RunJobs();
            tab.UpdateLayout();
            shell.OnSessionTick();
            Assert.Equal(ProgramHeatTier.Cold, tab.IgnitionTier);
            Assert.True(tab.FindControl<Grid>("TodaySessionProgressRow")!.IsVisible);
            Assert.False(tab.SessionSheenRunning);
            Assert.Equal(0, sheen.Opacity);
            PanicSurfaces.StopAll("test", shell);
            Dispatcher.UIThread.RunJobs();
            runner.Start(new Session { Id = "unrelated", Name = "Other session", DurationMinutes = 30 });
            shell.OnSessionTick();
            Assert.False(tab.FindControl<Grid>("TodaySessionProgressRow")!.IsVisible);
            Assert.False(tab.SessionSheenRunning);
            runner.Stop();
            Dispatcher.UIThread.RunJobs();
            enrollment.CurrentDay = bossDay;
            tab.RefreshPrograms();
            await shell.StartProgramSessionAsync();
            Dispatcher.UIThread.RunJobs();
            tab.UpdateLayout();
            shell.OnSessionTick();
            Assert.True(tab.SessionSheenRunning);
            shell.Close();
            Assert.False(tab.SessionSheenRunning);
            Assert.DoesNotContain(BeatLoop.Running, l => l.Owner == sheen);
        }
        finally
        {
            CoreEngine.StoppedHook = null;
            shell?.Close();
            Dispatcher.UIThread.RunJobs();
            AvApp.Sessions?.Stop();
            AvApp.Programs?.Dispose();
            (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider, BeatLoop.Time) = (oldPrograms, oldSessions, oldTask, oldTime);
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel, settings.PerformanceMode) = (seenTab, seenIntro, motion, perf);
            settings.SkipPauseXpWarning = skipPauseWarning;
            CoreSession.IsSessionRunningProvider = null;
            Directory.Delete(dir, true);
        }
    });

    private sealed class SteppedTime : TimeProvider
    {
        public double Seconds;
        public override long TimestampFrequency => 1_000_000;
        public override long GetTimestamp() => (long)(Seconds * TimestampFrequency);
    }
}
