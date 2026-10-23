using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Media;
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

/// <summary>
/// fin-shell-programs-tab review fix: the program session toasts (WPF ProgramsTab.cs:1864-1940, from
/// Presets.cs:1935,1979) and the run-view entrance (WPF StartProgramRunEntrance :1694-1760), both
/// driven from the shell (P04) on a stepped clock (P08).
/// </summary>
[Collection(RunsAloneCollection.Name)]
public sealed class ProgramsToastAndEntranceTests
{
    private sealed class Clock : TimeProvider
    {
        private const long Start = 1_000_000;
        public long Now = Start;
        public override long GetTimestamp() => Now;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void At(double seconds)
        {
            Now = Start + TimeSpan.FromSeconds(seconds).Ticks;
            BeatLoop.StepAll();
        }
    }

    private static Task Run(Func<MainShellWindow, ProgramService, SessionRunner, Clock, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var dir = Directory.CreateTempSubdirectory("ccp-programs-toast-").FullName;
        var (oldPrograms, oldSessions, oldTask, oldTime) = (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider, BeatLoop.Time);
        var settings = CoreSettings.Current;
        var (seenTab, seenIntro, motion) = (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel);
        settings.HasSeenProgramsTab = settings.HasSeenProgramsIntro = true;
        settings.MotionLevel = MotionLevel.Full;
        var clock = new Clock();
        BeatLoop.Time = clock;
        MainShellWindow? shell = null;
        try
        {
            CoreProgram.TaskAvailableProvider = ProgramCapabilities.IsAvailable;
            var svc = AvApp.Programs = new ProgramService(Path.Combine(dir, "programs.json"), readOnly: false);
            Assert.NotNull(svc.Enroll(svc.GetProgram("first_week")!));
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            shell = new MainShellWindow();
            runner.SessionLog.LogReady += shell.OnSessionLogReady;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.Show();
            Dispatcher.UIThread.RunJobs();
            await body(shell, svc, runner, clock);
        }
        finally
        {
            CoreEngine.StoppedHook = null;
            if (shell != null)
            {
                AvApp.Sessions!.SessionLog.LogReady -= shell.OnSessionLogReady;
                foreach (var w in shell.OwnedWindows.ToList()) w.Close();
                shell.Close();
            }
            Dispatcher.UIThread.RunJobs();
            AvApp.Sessions?.Stop();
            AvApp.Programs?.Dispose();
            (AvApp.Programs, AvApp.Sessions, CoreProgram.TaskAvailableProvider, BeatLoop.Time) = (oldPrograms, oldSessions, oldTask, oldTime);
            (settings.HasSeenProgramsTab, settings.HasSeenProgramsIntro, settings.MotionLevel) = (seenTab, seenIntro, motion);
            CoreSession.IsSessionRunningProvider = null;
            Directory.Delete(dir, true);
        }
    });

    private static Grid Toast(Control toast) => (Grid)((Border)toast).Child!;
    private static string ToastText(Control toast) => Toast(toast).Children.OfType<TextBlock>().First().Text!;

    [Fact]
    public Task ProgramSessionAnnouncesStartEarlyEndAndCompletionWithViewToday() => Run(async (shell, svc, runner, _) =>
    {
        var host = new StackPanel();
        AvApp.Notifications.AttachHost(host);   // replays anything an earlier test queued, before we listen
        // Recorded as shown: a toast's own (real) expiry timer may remove it before the assert.
        var toasts = new System.Collections.Generic.List<Control>();
        host.Children.CollectionChanged += (_, e) => { foreach (Control c in e.NewItems ?? Array.Empty<Control>()) toasts.Add(c); };
        var title = svc.ActiveProgram!.Title;

        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        Assert.True(runner.IsRunning && svc.IsProgramSession(runner.CurrentSession));
        Assert.Single(toasts);
        Assert.Equal(Loc.GetF("programs_toast_session_started", title, svc.ActiveEnrollment!.CurrentDay), ToastText(toasts[^1]));

        // Panic cuts it short: WPF still says so (OnSessionStopped -> AnnounceProgramSessionEnded).
        PanicSurfaces.StopAll("test", shell);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, toasts.Count);
        Assert.Equal(Loc.GetF("programs_toast_session_ended_early", title), ToastText(toasts[^1]));

        await shell.StartProgramSessionAsync();
        Dispatcher.UIThread.RunJobs();
        runner.Stop(completed: true);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new[]
        {
            Loc.GetF("programs_toast_session_started", title, svc.ActiveEnrollment!.CurrentDay),
            Loc.GetF("programs_toast_session_ended_early", title),
            Loc.GetF("programs_toast_session_started", title, svc.ActiveEnrollment!.CurrentDay),
            Loc.GetF("programs_toast_session_completed", title),
        }, toasts.Select(ToastText));
        foreach (var w in shell.OwnedWindows.ToList()) w.Close();   // the session recap
        Dispatcher.UIThread.RunJobs();

        // "View today" routes back to Programs.
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();
        var view = Toast(toasts[^1]).Children.OfType<StackPanel>().Single().Children.OfType<Button>().Single(b => b.Name == "ToastAction");
        Assert.Equal(Loc.Get("programs_toast_view_today"), ((TextBlock)view.Content!).Text);
        view.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("programs", shell.CurrentTab);

        // A foreign session is silent at both ends.
        var count = toasts.Count;
        shell.StartSession(Session.MorningDrift);
        Dispatcher.UIThread.RunJobs();
        Assert.True(runner.IsRunning);
        runner.Stop(completed: false);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(count, toasts.Count);
    });

    [Fact]
    public Task RunViewEntranceFadesSlidesAndGrowsTheRailThenRests() => Run((shell, _, _, clock) =>
    {
        shell.ShowTab("programs");
        Dispatcher.UIThread.RunJobs();
        var tab = shell.Named<ProgramsTabView>("ProgramsTab")!;
        var header = tab.FindControl<Border>("RunHeaderPanel")!;
        var today = tab.FindControl<Border>("TodayPanel")!;
        var rail = (ScaleTransform)tab.FindControl<Border>("RailProgressFill")!.RenderTransform!;
        double Y(Border b) => ((TranslateTransform)b.RenderTransform!).Y;

        Assert.True(tab.RunEntranceRunning);
        clock.At(0);
        Assert.Equal(0, header.Opacity, 3);
        Assert.Equal(14, Y(header), 3);
        Assert.Equal(0, today.Opacity, 3);
        Assert.Equal(0, rail.ScaleX, 3);

        clock.At(0.15);   // header 0.5 of 300 ms quad-out; Today 90 ms behind; rail not begun
        Assert.Equal(0.75, header.Opacity, 3);
        Assert.Equal(14 * Math.Pow(1 - 0.15 / 0.34, 2), Y(header), 3);
        Assert.Equal(1 - Math.Pow(1 - 0.06 / 0.3, 2), today.Opacity, 3);
        Assert.Equal(0, rail.ScaleX, 3);

        clock.At(0.425);  // rail half way, cubic out
        Assert.Equal(1 - Math.Pow(0.5, 3), rail.ScaleX, 3);

        clock.At(0.7);    // rest, loop released
        Assert.False(tab.RunEntranceRunning);
        Assert.Equal(1, header.Opacity);
        Assert.Equal(1, today.Opacity);
        Assert.Null(header.RenderTransform);
        Assert.Equal(1, rail.ScaleX);

        // Tab re-entry replays it; Motion Off shows the run view at rest with no clock.
        shell.ShowTab("presets");
        Dispatcher.UIThread.RunJobs();
        CoreSettings.Current.MotionLevel = MotionLevel.Off;
        shell.ShowTab("programs");
        Dispatcher.UIThread.RunJobs();
        Assert.False(tab.RunEntranceRunning);
        Assert.Equal(1, header.Opacity);
        Assert.Equal(1, rail.ScaleX);
        return Task.CompletedTask;
    });
}
