using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaster;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

/// <summary>The Chaster idle-day row reads the day log, as WPF ChasterServiceApp.MinutesFromDayLog
/// (ChasterServiceApp.cs:40,49). End to end: the engine runs N minutes on a stepped clock, the
/// conditioning-time tracker grows TotalConditioningMinutes, the day log credits today, and the
/// day-end judge (ChasterService.JudgeEndedDay -> TabDayEnd.IdleCharge) lets today off and charges a
/// day without conditioning (docs/avalonia-decisions.md).</summary>
public sealed class ChasterMinutesTests
{
    private static readonly PropertyInfo DayLog = typeof(AvApp).GetProperty("FeatureDayLog", BindingFlags.Static | BindingFlags.NonPublic)!;

    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    [Fact]
    public async Task ConditionedDayIsNotIdle_ADayWithoutConditioningIsCharged()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current is null)
                AppBuilder.Configure<AvApp>()
                    .UseSkia().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
                    .SetupWithoutStarting();
            var oldSettings = CoreSettings.ServiceProvider;
            var settings = new SettingsService();
            CoreSettings.ServiceProvider = () => settings;   // the day log reads CoreSettings.Service, as on the app
            var s = CoreSettings.Current;
            var saved = (s.TotalConditioningMinutes, s.PanicKeyEnabled, s.FlashEnabled, s.OfflineMode);
            (s.TotalConditioningMinutes, s.PanicKeyEnabled, s.FlashEnabled, s.OfflineMode) = (100, false, false, true);
            var savedLog = DayLog.GetValue(null);
            var dir = Path.Combine(Path.GetTempPath(), "ccp-chaster-minutes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var progress = new AchievementProgress();
            var log = new FeatureDayLogService(() => progress, Path.Combine(dir, "feature_day_log.json"));   // baseline: 100
            var chaster = ChasterHead.Create("sandbox", null);
            var clock = new SteppedClock();
            var shell = new MainShellWindow();
            shell.Show();
            try
            {
                DayLog.SetValue(null, log);
                CoreEngine.StoppedHook = shell.OnEngineStopped;
                shell.ConditioningTime = new ConditioningTimeTracker(clock);

                const int minutes = TabDayEnd.IdleMinutes + 2;
                shell.StartEngine();
                for (var i = 0; i < minutes * 60; i++) { clock.Now = clock.Now.AddSeconds(1); shell.ConditioningTimeTick(); }
                MainShellWindow.StopEngine();
                Assert.Equal(100 + minutes, s.TotalConditioningMinutes, 6);

                log.Tick();                                        // the day log's 60 s tick
                var today = DateTime.Today;
                string Key(DateTime d) => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                Assert.Equal(minutes, chaster.MinutesOn?.Invoke(Key(today)));
                Assert.Equal(0, TabDayEnd.IdleCharge(chaster.MinutesOn?.Invoke(Key(today))));
                Assert.Equal(TabDayEnd.IdleSeconds, TabDayEnd.IdleCharge(chaster.MinutesOn?.Invoke(Key(today.AddDays(-1)))));

                DayLog.SetValue(null, null);                       // no log (construction failed): nobody can tell
                Assert.Null(chaster.MinutesOn?.Invoke(Key(today.AddDays(-1))));
            }
            finally
            {
                CoreEngine.StoppedHook = null;
                CoreEngine.Stop();
                shell.Close();
                DayLog.SetValue(null, savedLog);
                chaster.Dispose();
                log.Dispose();
                try { Directory.Delete(dir, true); } catch (IOException) { }
                (s.TotalConditioningMinutes, s.PanicKeyEnabled, s.FlashEnabled, s.OfflineMode) = saved;
                CoreSettings.ServiceProvider = oldSettings;
            }
            return Task.CompletedTask;
        });
    }
}
