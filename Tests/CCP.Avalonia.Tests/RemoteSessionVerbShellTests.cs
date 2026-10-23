using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Newtonsoft.Json.Linq;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The controller's session verbs on the shell (WPF MainWindow.RemoteControl.cs:1287-1420): the
/// shell seeds RemoteCommands at startup, start/pause/resume/stop drive App.Sessions, and a pause a LOCAL
/// panic left is never undone remotely (PanicAftermath), while a remote pause may be resumed remotely.</summary>
public sealed class RemoteSessionVerbShellTests
{
    [Fact]
    public async Task Controller_starts_pauses_resumes_and_stops_a_session_but_never_undoes_a_local_panic()
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
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                var named = Session.GetAllSessions().Find(s => s.IsAvailable)!;

                Assert.Null(RemoteCommands.Execute("start_session", JObject.FromObject(new { session_id = named.Id })));
                Dispatcher.UIThread.RunJobs();
                Assert.True(runner.IsRunning);
                Assert.Equal(named.Id, runner.CurrentSession!.Id);

                Assert.Null(RemoteCommands.Execute("pause_session", null));
                Assert.True(runner.IsPaused);
                Assert.Null(RemoteCommands.Execute("resume_session", null));   // a remote pause: the controller may resume it
                Dispatcher.UIThread.RunJobs();
                Assert.False(runner.IsPaused);

                var held = runner.CurrentSession;
                MainShellWindow.StopEverything();   // a LOCAL panic (tray Stop everything) pauses the session
                Assert.True(runner.IsPaused);
                Assert.Equal("panic", RemoteCommands.Execute("resume_session", null));
                Assert.Equal("panic", RemoteCommands.Execute("start_session", null));
                Dispatcher.UIThread.RunJobs();
                Assert.True(runner.IsPaused);
                Assert.Same(held, runner.CurrentSession);

                Assert.Null(RemoteCommands.Execute("stop_session", null));
                Assert.False(runner.IsRunning || CoreEngine.IsRunning);

                Assert.Null(RemoteCommands.Execute("start_session", JObject.FromObject(new { session_id = "no-such-session" })));
                Dispatcher.UIThread.RunJobs();
                Assert.Equal("remote_session", runner.CurrentSession!.Id);   // WPF's generic fallback
                Assert.True(shell.IsSessionRemoteStarted);

                // WPF StopAllRemoteEffects :1034 (#878): the remote ending stops the controller's own session...
                RemoteCommands.StopEffects(force: false);
                Assert.False(runner.IsRunning);

                // ...but never the subject's own; a panic (force) stops either.
                shell.StartSession(named);
                Dispatcher.UIThread.RunJobs();
                Assert.True(runner.IsRunning);
                Assert.False(shell.IsSessionRemoteStarted);
                RemoteCommands.StopEffects(force: false);
                Assert.True(runner.IsRunning);
                RemoteCommands.StopEffects(force: true);
                Assert.False(runner.IsRunning);
            }
            finally
            {
                runner.Stop();
                RemoteCommands.NoteLocalPanicPause(null);
                CoreEngine.StoppedHook = null;
                CoreSession.IsSessionRunningProvider = null;
                AvApp.Sessions = null;
                CoreEngine.Stop();
                shell.Close();
            }
            Assert.Null(RemoteCommands.SessionVerb);   // the closed shell takes its seams with it
            Assert.Null(RemoteCommands.StopSessionHook);
            return Task.CompletedTask;
        });
    }
}
