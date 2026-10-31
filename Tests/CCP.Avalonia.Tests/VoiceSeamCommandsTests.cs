using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Controls.AppSettings;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = global::ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>WPF voice session pause/resume, including the specific local-panic pause.</summary>
public sealed class VoiceSeamCommandsTests
{
    private static readonly string[] Wake = { "hey bambi" };

    private static void Say(MainShellWindow shell, string command)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + command, Wake), command);
        Dispatcher.UIThread.RunJobs();
    }

    [Fact]
    public Task PauseAndResumeCommandsDriveTheRunningSession() => Run(shell =>
    {
        ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.StartSession(new Session { Id = "voice_pause", Name = "Voice Pause", DurationMinutes = 1 });
            Assert.True(runner.IsRunning && !runner.IsPaused);

            Say(shell, "pause the session");
            Assert.True(runner.IsPaused);
            var pauses = runner.PauseCount;
            Say(shell, "pause the session");
            Assert.Equal(pauses, runner.PauseCount);
            Assert.Equal("\u25B6", shell.Named<global::Avalonia.Controls.TextBlock>("TxtPauseIcon")!.Text);

            Say(shell, "resume the session");
            Assert.False(runner.IsPaused);
            Assert.Equal("\u23F8", shell.Named<global::Avalonia.Controls.TextBlock>("TxtPauseIcon")!.Text);
        }
        finally
        {
            runner.Stop();
            CoreEngine.StoppedHook = null;
            CoreSession.IsSessionRunningProvider = null;
            AvApp.Sessions = null;
            CoreEngine.Stop();
        }
    });

    [Fact]
    public Task RemotePanicCanResumeItsOwnPause() => WithSession((shell, runner, session) =>
    {
        Assert.True(shell.RunVoicePanic()); // WPF TriggerPanicFromRemote, not RunPanicStopTail
        Assert.True(runner.IsPaused);
        Say(shell, "resume the session");
        Assert.False(runner.IsPaused);
        Say(shell, "pause the session");
        RemoteCommands.StopEffects(force: true); // controller panic also leaves no local mark
        Say(shell, "resume the session");
        Assert.False(runner.IsPaused);
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task LocalPanicRefusesResumeButLaterOrdinaryPausesCanResume(bool leashGate) => WithSession((shell, runner, session) =>
    {
        if (leashGate)
            typeof(MainShellWindow).GetMethod("PanicFromLeashGate",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, null);
        else shell.HandlePanicKeyPress(System.DateTime.Now);
        Assert.True(runner.IsPaused && shell.LocalPanicHoldsSession);
        Say(shell, "resume the session");
        Assert.True(runner.IsPaused);
        shell.Named<global::Avalonia.Controls.Button>("BtnPauseSession")!.RaiseEvent(
            new global::Avalonia.Interactivity.RoutedEventArgs(global::Avalonia.Controls.Button.ClickEvent));
        Assert.False(runner.IsPaused); // local Resume is allowed
        Say(shell, "pause the session");
        Say(shell, "resume the session");
        Assert.False(runner.IsPaused); // stale mark cannot hold a later ordinary pause
    });

    [Fact]
    public Task ReusedSessionStartsANewRunWithoutTheOldPanicMark() => WithSession((shell, runner, session) =>
    {
        shell.HandlePanicKeyPress(System.DateTime.Now);
        var run = runner.RunNumber;
        runner.Stop();
        Assert.False(shell.LocalPanicHoldsSession);
        runner.Start(session); // same object, same first pause count, started outside the shell
        Assert.Equal(run + 1, runner.RunNumber);
        Say(shell, "pause the session");
        Say(shell, "resume the session");
        Assert.False(runner.IsPaused);
    });

    private static Task WithSession(System.Action<MainShellWindow, SessionRunner, Session> body) => Run(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var session = new Session { Id = "panic_pause", Name = "Panic pause", DurationMinutes = 1 };
        CoreSettings.Current.PanicKeyEnabled = true;
        CoreSettings.Current.PanicKey = "F8";
        CoreSettings.Current.PanicOverridesAll = true;
        try
        {
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            shell.StartSession(session);
            Assert.True(runner.IsRunning);
            body(shell, runner, session);
        }
        finally { runner.Stop(); CoreEngine.Stop(); AvApp.Sessions = null; }
    });

    private static Task Run(System.Action<MainShellWindow> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            AmbientBeatLoopsTests.Setup(); // bundled face for Windows font names on Linux
            var shell = new MainShellWindow();
            shell.Show();
            try { body(shell); }
            finally { shell.Close(); }
            return Task.CompletedTask;
        });
}
