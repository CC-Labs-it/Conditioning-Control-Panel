using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public sealed class ListeningPauseTests
{
    // WPF AutonomyService.VoiceCommandHost.cs:104-105, MainWindow.RemoteControl.cs:1341-1407.
    [Fact]
    public void SessionGrammarResumesOrdinaryStopsButCannotUndoLocalPanic() => Run(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            shell.StartSession(new Session { Id = "voice_pause", Name = "Voice pause", DurationMinutes = 5 });
            Assert.True(runner.IsRunning);
            var video = CoreEngine.Video;
            try
            {
                CoreEngine.Video = null;
                Assert.DoesNotContain("pause the video", shell.VoiceCmds.Grammar());
                CoreEngine.Video = new MandatoryVideoScheduler(ConditioningControlPanel.Avalonia.Views.Overlays.MandatoryVideoOverlay.Instance);
                Assert.Contains("pause the video", shell.VoiceCmds.Grammar());
                Assert.Contains("resume the video", shell.VoiceCmds.Grammar());
            }
            finally { CoreEngine.Video = video; }
            Assert.Contains("pause the session", shell.VoiceCmds.Grammar());
            Assert.Contains("resume the session", shell.VoiceCmds.Grammar());
            Command(shell, "pause the session");
            Assert.True(runner.IsPaused);
            Assert.Equal(1, runner.PauseCount);
            Command(shell, "pause the session");
            Assert.Equal(1, runner.PauseCount);
            Command(shell, "resume the session");
            Assert.False(runner.IsPaused);
            MainShellWindow.StopEngine();
            Assert.True(runner.IsPaused);
            Command(shell, "resume the session");
            Assert.False(runner.IsPaused);
            CoreSettings.Current.PanicKeyEnabled = true;
            CoreSettings.Current.PanicOverridesAll = true;
            shell.HandlePanicKeyPress(DateTime.Now);
            Assert.True(runner.IsPaused);
            Command(shell, "resume the session");
            Assert.True(runner.IsPaused);
            Assert.False(shell.ResumeSessionByVoice());
        }
        finally { runner.Stop(); AvApp.Sessions = null; CoreEngine.Stop(); }
    });

    // WPF MainWindow.Leash.cs:307 -> MainWindow.xaml.cs:1898: the gate is a local panic.
    [Fact]
    public void SessionGrammarCannotUndoLeashGatePanic() => Run(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            shell.StartSession(new Session { Id = "voice_leash_panic", Name = "Leash panic", DurationMinutes = 5 });
            Assert.True(runner.IsRunning && !runner.IsPaused);
            shell.PanicFromLeashGate();
            Assert.True(runner.IsPaused);
            Assert.True(MainShellWindow.LocalPanicHoldsSession);
            Command(shell, "resume the session");
            Assert.True(runner.IsPaused);
            Assert.False(shell.ResumeSessionByVoice());
        }
        finally { runner.Stop(); AvApp.Sessions = null; CoreEngine.Stop(); }
    });

    [Fact]
    public void LockdownRefusesSessionPause() => Run(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        var ld = LockdownService.Current = new LockdownService();
        try
        {
            shell.StartSession(new Session { Id = "voice_lock", Name = "Voice lock", DurationMinutes = 5 });
            ld.Activate(TimeSpan.FromMinutes(30));
            Command(shell, "pause the session");
            Assert.False(runner.IsPaused);
            Assert.Equal(0, runner.PauseCount);
        }
        finally { ld.Deactivate(); LockdownService.Current = null; runner.Stop(); AvApp.Sessions = null; CoreEngine.Stop(); }
    });

    internal static void Command(MainShellWindow shell, string text)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + text, new[] { "hey bambi" }));
        Dispatcher.UIThread.RunJobs();
    }

    private static void Run(Action<MainShellWindow> body) => AvaloniaTestDispatcher.Run(() =>
    {
        if (Application.Current is null)
            global::ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        Dispatcher.UIThread.RunJobs();
        try { body(shell); }
        finally { shell.Close(); Dispatcher.UIThread.RunJobs(); }
    });
}
