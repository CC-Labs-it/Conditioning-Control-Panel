using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The head's session run (WPF MainWindow.Presets.cs StartSession / OnSessionStarted /
/// OnSessionStopped / OnSessionLogReady): a started session locks the dials and shows its clock,
/// Stop everything pauses it (the panic tail), the pause button resumes it, and a stop brings the dials back
/// and opens the Ended Early recap by itself.</summary>
public sealed class SessionRunWireTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public Task Bottom_bar_projection_restores_caller_state(bool countdown) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        var previousSessions = AvApp.Sessions;
        var previousCountdown = CoreSettings.Current.ShowSessionCountdown;
        var sentinel = new SessionRunner(new SessionLogService());
        try
        {
            AvApp.Sessions = sentinel;
            CoreSettings.Current.ShowSessionCountdown = countdown;
            // No isolation hook runs between this call and the assertions.
            await Bottom_bar_projection_uses_the_runner_clock_without_a_shell();
            Assert.Equal(countdown, CoreSettings.Current.ShowSessionCountdown);
            Assert.Same(sentinel, AvApp.Sessions);
        }
        finally
        {
            AvApp.Sessions = previousSessions;
            CoreSettings.Current.ShowSessionCountdown = previousCountdown;
        }
    });

    [Fact]
    public Task Bottom_bar_projection_uses_the_runner_clock_without_a_shell() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousSessions = AvApp.Sessions;
        var previousCountdown = CoreSettings.Current.ShowSessionCountdown;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var status = new TextBlock { IsVisible = false };
        try
        {
            CoreEngine.Start();
            MainShellWindow.RefreshBarStatus(status);
            Assert.True(status.IsVisible);
            Assert.Equal(Loc.Get("label_running"), status.Text);
            CoreSettings.Current.ShowSessionCountdown = true;
            runner.Start(new Session { Id = "bar-projection", Name = "Clock", DurationMinutes = 10 });
            runner.Pause(); // Freeze the clock for deterministic label comparisons.
            MainShellWindow.RefreshBarStatus(status);
            var mark = " [" + Loc.Get("label_paused") + "]";
            Assert.StartsWith("Clock - 09:", status.Text);
            Assert.EndsWith(" left" + mark, status.Text);
            runner.CurrentSession!.DurationMinutes = 0;
            MainShellWindow.RefreshBarStatus(status);
            Assert.EndsWith(" elapsed" + mark, status.Text);
            CoreSettings.Current.ShowSessionCountdown = false;
            MainShellWindow.RefreshBarStatus(status);
            Assert.Equal("Clock" + mark, status.Text);
            runner.Resume();
            MainShellWindow.RefreshBarStatus(status);
            Assert.Equal("Clock", status.Text);
            runner.Stop();
            MainShellWindow.RefreshBarStatus(status);
            Assert.Equal("", status.Text);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            AvApp.Sessions = previousSessions;
            CoreSettings.Current.ShowSessionCountdown = previousCountdown;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Bottom_bar_tracks_session_pause_countdown_preference_and_stop() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var previousSessions = AvApp.Sessions;
        var previousStopped = CoreEngine.StoppedHook;
        var previousCountdown = CoreSettings.Current.ShowSessionCountdown;
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var shell = new MainShellWindow();
        try
        {
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            // WPF Presets.cs:1899: the engine alone (no session) shows the running label; Stop clears it.
            CoreEngine.Start();
            shell.UpdateStartButton();
            Assert.Equal(Loc.Get("label_running"), shell.Named<TextBlock>("TxtPresetsStatus")!.Text);
            CoreEngine.Stop();
            Assert.Equal("", shell.Named<TextBlock>("TxtPresetsStatus")!.Text);
            CoreSettings.Current.ShowSessionCountdown = true;
            shell.StartSession(new Session { Id = "bar-clock", Name = "Bar clock", DurationMinutes = 10 });
            var status = shell.Named<TextBlock>("TxtPresetsStatus")!;
            Assert.StartsWith("Bar clock - ", status.Text);
            Assert.EndsWith(" left", status.Text);
            status.IsVisible = false;
            runner.CurrentSession!.DurationMinutes = 0;
            shell.OnSessionTick();
            Assert.True(status.IsVisible);
            Assert.EndsWith(" elapsed", status.Text);
            runner.CurrentSession!.DurationMinutes = 10;
            runner.Pause();
            shell.SetPauseButton(true); // A held tick must still repaint the mark immediately.
            Assert.EndsWith(" [" + Loc.Get("label_paused") + "]", status.Text);
            CoreSettings.Current.ShowSessionCountdown = false;
            shell.OnSessionTick();
            Assert.Equal("Bar clock [" + Loc.Get("label_paused") + "]", status.Text);
            runner.Resume();
            shell.SetPauseButton(false);
            Assert.Equal("Bar clock", status.Text);
            runner.Stop();
            Assert.Equal("", status.Text);
        }
        finally
        {
            runner.Stop();
            CoreEngine.Stop();
            CoreEngine.StoppedHook = previousStopped;
            AvApp.Sessions = previousSessions;
            CoreSettings.Current.ShowSessionCountdown = previousCountdown;
            shell.Close();
        }
        return Task.CompletedTask;
    });

    [Fact]
    public async Task StartLocksAndLabelsTrayStopPausesResumeRestartsStopOpensTheRecap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();

            var shell = new MainShellWindow();
            shell.Show();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            runner.SessionLog.LogReady += shell.OnSessionLogReady;
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                var session = new Session { Id = "wire_test", Name = "Wire Test", Icon = "🧪", DurationMinutes = 1 };
                shell.StartSession(session);

                Assert.True(runner.IsRunning);
                Assert.True(CoreEngine.IsRunning);
                Assert.True(shell.IsSessionFeatureLockActive);   // seeded by the runner, not the engine
                Assert.StartsWith("Wire Test 00:", shell.Named<TextBlock>("TxtStartLabel")!.Text);
                var presets = shell.Named<PresetsTabView>("PresetsTab")!;
                var stopLabel = Assert.IsType<TextBlock>(presets.BtnStartSession.Content).Text!;
                Assert.StartsWith("STOP SESSION (00:", stopLabel);

                MainShellWindow.StopEverything();   // the tray's Stop everything (and the panic tail)
                // WPF MainWindow.xaml.cs:1726: the session is paused, not ended; the engine stops.
                Assert.True(runner.IsRunning && runner.IsPaused);
                Assert.Equal(1, runner.PauseCount);
                Assert.False(CoreEngine.IsRunning || CoreFlash.IsRunning);
                Assert.True(shell.IsSessionFeatureLockActive);
                var pause = shell.Named<Button>("BtnPauseSession")!;
                Assert.True(pause.IsVisible);
                Assert.Equal("\u25B6", shell.Named<TextBlock>("TxtPauseIcon")!.Text);
                Assert.Equal(Loc.Get("tooltip_resume_session"), ToolTip.GetTip(pause));

                pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));   // Resume
                Assert.False(runner.IsPaused);
                Assert.Equal("\u23F8", shell.Named<TextBlock>("TxtPauseIcon")!.Text);
                Assert.Equal(Loc.GetF("tooltip_pause_session_100_xp_penalty_per_pause_npause", 1), ToolTip.GetTip(pause));

                runner.Stop(completed: false);   // the stop-session confirm's OK
                Assert.False(runner.IsRunning || CoreEngine.IsRunning || pause.IsVisible);
                Assert.False(shell.IsSessionFeatureLockActive);
                Assert.Equal(Loc.Get("label_start"), shell.Named<TextBlock>("TxtStartLabel")!.Text);
                Assert.Equal(Loc.Get("btn_start_session"), Assert.IsType<TextBlock>(presets.BtnStartSession.Content).Text);

                Dispatcher.UIThread.RunJobs();
                var recap = Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                Assert.Equal("Wire Test", recap.FindControl<TextBlock>("TxtSessionName")!.Text);
                Assert.Equal(Loc.Get("label_session_ended_early"), recap.FindControl<TextBlock>("TxtMainMessage")!.Text);
                recap.Close();

                // Reaching the end: the recap says Completed.
                shell.StartSession(session);
                runner.Stop(completed: true);
                Dispatcher.UIThread.RunJobs();
                recap = Assert.Single(shell.OwnedWindows.OfType<SessionCompleteWindow>());
                Assert.Equal(Loc.Get("label_good_girl_3"), recap.FindControl<TextBlock>("TxtMainMessage")!.Text);
                recap.Close();
            }
            finally
            {
                runner.Stop();
                CoreEngine.StoppedHook = null;
                CoreSession.IsSessionRunningProvider = null;
                AvApp.Sessions = null;
                CoreEngine.Stop();
                shell.Close();
            }
            return Task.CompletedTask;
        });
    }
}
