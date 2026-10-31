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

/// <summary>WPF voice session pause/resume, including the base's post-panic engine hold.</summary>
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
            Say(shell, "pause the session");
            ConditioningControlPanel.Services.Safety.PanicAftermath.ArmQuiet();
            Say(shell, "resume the session");
            Assert.True(runner.IsPaused);
            ConditioningControlPanel.Services.Safety.PanicAftermath.EndQuiet();
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

    private static Task Run(System.Action<MainShellWindow> body) =>
        AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var shell = new MainShellWindow();
            shell.Show();
            try { body(shell); }
            finally { shell.Close(); }
            return Task.CompletedTask;
        });
}
