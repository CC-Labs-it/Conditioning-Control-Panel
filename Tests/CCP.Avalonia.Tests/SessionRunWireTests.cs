using System;
using System.Linq;
using Avalonia.Input;
using ConditioningControlPanel.Avalonia.Views.Dialogs;
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
    [Fact]
    public async Task StopConfirmUsesWpfCopyAndNoKeepsTheSessionRunning()
    {
        await AvaloniaTestDispatcher.RunAsync(async () =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
            try
            {
                shell.Show();
                var session = new Session { Id = "stop_copy", Name = "Stop copy", DurationMinutes = 60 };
                runner.Start(session);
                foreach (var answer in new[] { "no", "enter-no", "close", "yes" })
                {
                    var pending = shell.ConfirmStopSession("title_stop_session_confirm", "msg_stop_session_body");
                    var dialog = Assert.Single(shell.OwnedWindows.OfType<MessageDialog>());
                    var no = dialog.FindControl<Button>("BtnCancel")!;
                    var yes = dialog.FindControl<Button>("BtnOk")!;
                    Assert.Equal("Keep going", Assert.IsType<TextBlock>(no.Content).Text);
                    Assert.Equal("Stop the session", Assert.IsType<TextBlock>(yes.Content).Text);
                    Assert.False(no.IsDefault || yes.IsDefault || no.IsCancel || yes.IsCancel);
                    Assert.Same(yes, dialog.FocusManager!.GetFocusedElement());
                    dialog.KeyPress(Key.Escape, RawInputModifiers.None, PhysicalKey.Escape, null);
                    Assert.False(pending.IsCompleted);
                    if (answer == "enter-no")
                    {
                        no.Focus();
                        dialog.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                    }
                    else if (answer == "close") dialog.Close();
                    else (answer == "no" ? no : yes).RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                    await pending.WaitAsync(TimeSpan.FromSeconds(5));
                    Assert.Equal(answer != "yes", runner.IsRunning);
                    if (answer != "yes")
                    {
                        Assert.Same(session, runner.CurrentSession);
                        Assert.False(runner.IsPaused);
                    }
                }
            }
            finally
            {
                foreach (var dialog in shell.OwnedWindows.ToArray()) dialog.Close();
                runner.Stop();
                CoreEngine.Stop();
                AvApp.Sessions = null;
                CoreSession.IsSessionRunningProvider = null;
                shell.Close();
            }
        });
    }

    [Fact]
    public async Task StartLocksAndLabelsTrayStopPausesResumeRestartsStopOpensTheRecap()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp()
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
