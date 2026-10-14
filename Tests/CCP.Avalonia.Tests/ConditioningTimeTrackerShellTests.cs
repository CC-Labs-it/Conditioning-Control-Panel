using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The conditioning-time tracker from the shell's own start paths (WPF StartEngine :423 /
/// StopEngine :540): the Start button and a session start arm it, the 1 s tick writes a minute per
/// 60 ticks and repaints the pink_hours pill live, and panic stops it with the remainder added.</summary>
public sealed class ConditioningTimeTrackerShellTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task EngineRunAccruesMinutesAndPanicStopsWithTheRemainder()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<global::ConditioningControlPanel.Avalonia.App>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var s = CoreSettings.Current;
            var saved = (s.TotalConditioningMinutes, s.UnlockedSkills, s.PanicKeyEnabled, s.FlashEnabled, s.OfflineMode);
            (s.TotalConditioningMinutes, s.PanicKeyEnabled, s.FlashEnabled, s.OfflineMode) = (100, false, false, true);
            s.UnlockedSkills = new() { "pink_hours" };
            var clock = new SteppedClock();
            var shell = new MainShellWindow();
            shell.Show();
            void Tick(int n) { for (var i = 0; i < n; i++) { clock.Now = clock.Now.AddSeconds(1); shell.ConditioningTimeTick(); } }
            try
            {
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.ConditioningTime = new ConditioningTimeTracker(clock);

                Tick(5);                                           // not running: nothing counts
                Assert.Equal(100, s.TotalConditioningMinutes);

                shell.StartEngine();                               // the Start button's path
                Assert.True(shell.ConditioningTimerRunning);
                Tick(60);
                Assert.Equal(101, s.TotalConditioningMinutes);
                Tick(30);
                Assert.Equal("1h 41m 30s", shell.Named<TextBlock>("TxtPillConditioningTime")!.Text);   // live, not stored
                Assert.Equal(101, s.TotalConditioningMinutes);

                PanicSurfaces.StopAll("conditioning-time test", shell);   // panic -> StopEngine -> hook
                Assert.False(shell.ConditioningTimerRunning);
                Assert.Equal(101.5, s.TotalConditioningMinutes, 6);      // the 30 s remainder
                Tick(120);                                               // stopped: nothing more
                Assert.Equal(101.5, s.TotalConditioningMinutes, 6);

                // A session start arms it too (WPF: sessions start through StartEngine).
                var oldSessions = global::ConditioningControlPanel.Avalonia.App.Sessions;
                var runner = global::ConditioningControlPanel.Avalonia.App.Sessions = new SessionRunner(new SessionLogService());
                try
                {
                    shell.StartSession(new ConditioningControlPanel.Models.Session { Id = "ctt_test", Name = "CTT Test", Icon = "🧪", DurationMinutes = 5 });
                    Assert.True(shell.ConditioningTimerRunning);
                }
                finally
                {
                    runner.Stop();
                    global::ConditioningControlPanel.Avalonia.App.Sessions = oldSessions;   // P02: restore, not null
                    CoreSession.IsSessionRunningProvider = null;
                }
                MainShellWindow.StopEngine();
                Assert.False(shell.ConditioningTimerRunning);
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                shell.Close();
                (s.TotalConditioningMinutes, s.UnlockedSkills, s.PanicKeyEnabled, s.FlashEnabled, s.OfflineMode) = saved;
            }
            return Task.CompletedTask;
        });
    }
}
