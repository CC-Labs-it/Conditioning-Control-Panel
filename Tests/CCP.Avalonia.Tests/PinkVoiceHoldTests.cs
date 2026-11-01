using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class PinkVoiceHoldTests
{
    // WPF VoiceCommandHost.cs:89-91 and OverlayService.cs:1109-1153,1192.
    [Theory]
    [InlineData(false, false, 10, 40)]
    [InlineData(true, false, 50, 50)]
    [InlineData(false, true, 10, 40)]
    [InlineData(true, true, 50, 50)]
    public Task Voice_hold_survives_repaint_and_releases_only_its_own_tint(bool enabled, bool running, int saved, int expected)
        => WithShell(shell =>
        {
            var s = CoreSettings.Current;
            (s.PinkFilterEnabled, s.PinkFilterOpacity) = (enabled, saved);
            if (running) shell.StartEngine();
            Speak(shell, "go pink");
            Assert.True(PinkFilterOverlay.IsShowing);
            Assert.Equal((byte)(expected * 255 / 100), Alpha());
            Assert.Equal((enabled, saved, running), (s.PinkFilterEnabled, s.PinkFilterOpacity, CoreEngine.IsRunning));
            s.PinkFilterOpacity = 45;
            PinkFilterOverlay.Refresh(shell);
            Assert.True(PinkFilterOverlay.IsShowing);
            Assert.Equal((byte)(expected * 255 / 100), Alpha());
            Speak(shell, "pink filter on");
            Assert.Equal((byte)(0.45 * 255), Alpha());
            Speak(shell, "pink filter off");
            Assert.Equal(enabled && running, PinkFilterOverlay.IsShowing);
            if (PinkFilterOverlay.IsShowing)
            {
                Assert.Equal((byte)(0.45 * 255), Alpha());
                s.PinkFilterOpacity = 30;
                PinkFilterOverlay.Refresh(shell);
                Assert.Equal((byte)(0.30 * 255), Alpha());
            }
            Speak(shell, "pink filter off");
            Assert.Equal(enabled && running, PinkFilterOverlay.IsShowing);
            return Task.CompletedTask;
        });

    [Theory]
    [InlineData("go pink")]
    [InlineData("pink filter off")]
    public Task Command_is_in_grammar(string phrase) => WithShell(shell =>
    {
        Assert.Contains(phrase, shell.VoiceCmds.Grammar());
        return Task.CompletedTask;
    });

    [Fact]
    public Task Pink_row_is_visible_in_the_tab() => WithShell(shell =>
    {
        shell.ShowTab("shelistening");
        Dispatcher.UIThread.RunJobs();
        var row = shell.SheListeningPage!.GetLogicalDescendants()
            .OfType<ConditioningControlPanel.Avalonia.Controls.IconLabel>()
            .Single(x => Equals(x.Tag, "voice:pink_on|pink_off"));
        Assert.True(row.IsVisible);
        return Task.CompletedTask;
    });

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Stop_and_panic_clear_a_standalone_hold(bool panic) => WithShell(shell =>
    {
        CoreSettings.Current.PinkFilterEnabled = false;
        Speak(shell, "go pink");
        Assert.True(PinkFilterOverlay.IsShowing);
        if (panic)
        {
            CoreSettings.Current.PanicKeyEnabled = true;
            CoreSettings.Current.PanicKey = "F8";
            shell.HandlePanicKeyPress(new DateTime(2026, 1, 1, 12, 0, 0));
        }
        else MainShellWindow.StopEngine();
        PinkFilterOverlay.Refresh(shell);
        Assert.False(PinkFilterOverlay.IsShowing);
        return Task.CompletedTask;
    });

    [Fact]
    public Task Refused_show_does_not_leave_a_stale_hold() => WithShell(shell =>
    {
        CoreSettings.Current.PinkFilterEnabled = false;
        PinkFilterOverlay.SkipPlatformChecksForTests = false; // headless has no X11 window
        Speak(shell, "go pink");
        Assert.False(PinkFilterOverlay.IsShowing);
        PinkFilterOverlay.SkipPlatformChecksForTests = true;
        PinkFilterOverlay.Refresh(shell);
        Assert.False(PinkFilterOverlay.IsShowing);
        return Task.CompletedTask;
    });

    // WPF SessionEngine.cs:530 clears the hold on pause, and :799 drives the tint on every ramp tick.
    [Fact]
    public Task Pause_drops_hold_and_resume_does_not_restore_it() => WithShell(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            CoreSettings.Current.SkipPauseXpWarning = true;
            var session = new Session { Id = "pink_voice_pause", Name = "Pink pause", DurationMinutes = 1 };
            session.Settings.PinkFilterEnabled = false;
            shell.StartSession(session);
            Speak(shell, "go pink");
            Assert.True(PinkFilterOverlay.IsShowing);
            var pause = shell.Named<Button>("BtnPauseSession")!;
            pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.True(runner.IsPaused);
            Assert.False(PinkFilterOverlay.IsShowing);
            pause.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.False(runner.IsPaused);
            Assert.False(PinkFilterOverlay.IsShowing);
        }
        finally { runner.Stop(); CoreSession.IsSessionRunningProvider = null; AvApp.Sessions = null; }
        return Task.CompletedTask;
    });

    [Fact]
    public Task Session_tick_replaces_voice_opacity() => WithShell(shell =>
    {
        var runner = AvApp.Sessions = new SessionRunner(new SessionLogService());
        try
        {
            var session = new Session { Id = "pink_voice_ramp", Name = "Pink ramp", DurationMinutes = 20 };
            session.Settings.RampCurve = RampCurve.Linear;
            session.Settings.PinkFilterEnabled = true;
            session.Settings.PinkFilterStartMinute = 0;
            session.Settings.PinkFilterStartOpacity = 10;
            session.Settings.PinkFilterEndOpacity = 80;
            shell.StartSession(session);
            runner.Tick(TimeSpan.FromMinutes(10));
            shell.OnSessionTick();
            Speak(shell, "go pink");
            Assert.Equal((byte)(Math.Max(0.4, CoreSettings.Current.PinkFilterOpacity / 100.0) * 255), Alpha());
            runner.Tick(TimeSpan.FromMinutes(15));
            shell.OnSessionTick();
            Assert.Equal((byte)(runner.PinkOpacity!.Value / 100 * 255), Alpha());
            PinkFilterOverlay.Refresh(shell);
            Assert.Equal((byte)(runner.PinkOpacity.Value / 100 * 255), Alpha());

            // WPF SessionEngine.cs:793: a disabled tint does not receive a session ramp.
            CoreSettings.Current.PinkFilterEnabled = false;
            Speak(shell, "go pink");
            var voiceAlpha = Alpha();
            shell.OnSessionTick(); // refresh with the last ramp value still parked
            Assert.Equal(voiceAlpha, Alpha());
        }
        finally { runner.Stop(); CoreSession.IsSessionRunningProvider = null; AvApp.Sessions = null; }
        return Task.CompletedTask;
    });

    private static byte Alpha() => ((SolidColorBrush)((Border)PinkFilterOverlay.Shown[0].Content!).Background!).Color.A;

    private static void Speak(MainShellWindow shell, string phrase)
    {
        Assert.True(shell.VoiceCmds.TryHandleInlineCommand("hey bambi " + phrase, new[] { "hey bambi" }));
        Dispatcher.UIThread.RunJobs();
    }

    private static Task WithShell(Func<MainShellWindow, Task> body) => AvaloniaTestDispatcher.RunAsync(async () =>
    {
        if (Application.Current is null)
            AppBuilder.Configure<AvApp>().WithInterFont().With(AppFonts.Options()).ConfigureFonts(AppFonts.Register)
                .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                .SetupWithoutStarting();
        var shell = new MainShellWindow();
        shell.Show();
        try
        {
            CoreSession.IsEngineRunningProvider = () => CoreEngine.IsRunning;
            CoreEngine.StoppedHook = shell.OnEngineStopped;
            PinkFilterOverlay.SkipPlatformChecksForTests = true;
            await body(shell);
        }
        finally
        {
            CoreEngine.Stop();
            CoreEngine.StoppedHook = null;
            PinkFilterOverlay.CloseAll();
            PinkFilterOverlay.SkipPlatformChecksForTests = false;
            shell.Close();
        }
    });
}
