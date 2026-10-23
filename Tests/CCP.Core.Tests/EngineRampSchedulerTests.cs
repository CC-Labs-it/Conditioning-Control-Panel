using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF MainWindow.StartStop.cs leftovers moved to Core: the plain Intensity Ramp (:525-692), the
// scheduler rules (:697-855) and the Relapse stamp (AchievementService.cs:669-705).
[Collection(SessionStatics.Name)]
public sealed class EngineRampSchedulerTests
{
    private static AppSettings Schedule(string start, string end, bool today = true)
    {
        var s = new AppSettings
        {
            SchedulerEnabled = true, SchedulerStartTime = start, SchedulerEndTime = end,
            SchedulerMonday = today, SchedulerTuesday = today, SchedulerWednesday = today, SchedulerThursday = today,
            SchedulerFriday = today, SchedulerSaturday = today, SchedulerSunday = today,
        };
        return s;
    }

    private static readonly DateTime Day = new(2026, 10, 22);   // a Thursday

    [Theory]
    [InlineData("16:00", "22:00", 16, 0, true)]    // start inclusive
    [InlineData("16:00", "22:00", 22, 0, false)]   // end exclusive
    [InlineData("16:00", "22:00", 15, 59, false)]
    [InlineData("22:00", "02:00", 23, 30, true)]   // overnight
    [InlineData("22:00", "02:00", 1, 59, true)]
    [InlineData("22:00", "02:00", 12, 0, false)]
    [InlineData("junk", "junk", 17, 0, true)]      // falls back to 16:00-22:00
    public void Window_matches_WPF_IsInScheduledTimeWindow(string start, string end, int h, int m, bool expected) =>
        Assert.Equal(expected, CoreScheduler.InWindow(Schedule(start, end), Day.AddHours(h).AddMinutes(m)));

    [Fact]
    public void Window_is_closed_on_an_inactive_day() =>
        Assert.False(CoreScheduler.InWindow(Schedule("00:00", "23:59", today: false), Day.AddHours(12)));

    [Fact]
    public void Tick_starts_once_holds_off_after_a_manual_stop_and_stops_only_what_it_started()
    {
        var s = Schedule("16:00", "22:00");
        var sch = new CoreScheduler();
        var inside = Day.AddHours(17);
        var outside = Day.AddHours(23);

        Assert.Equal(CoreScheduler.Step.Start, sch.OnTick(s, inside, running: false));
        Assert.Equal(CoreScheduler.Step.None, sch.OnTick(s, inside, running: true));
        sch.OnManualToggle(s, inside, stopping: true);   // the user's own Stop inside the window
        Assert.Equal(CoreScheduler.Step.None, sch.OnTick(s, inside, running: false));
        Assert.Equal(CoreScheduler.Step.Stop, sch.OnTick(s, outside, running: true));   // still auto-started
        Assert.Equal(CoreScheduler.Step.None, sch.OnTick(s, outside, running: false));   // resets both flags
        Assert.False(sch.ManuallyStopped || sch.AutoStarted);
        Assert.Equal(CoreScheduler.Step.Start, sch.OnTick(s, inside.AddDays(1), running: false));

        var manual = new CoreScheduler();   // a manual start is never auto-stopped
        Assert.Equal(CoreScheduler.Step.None, manual.OnTick(s, outside, running: true));
        manual.OnManualToggle(s, inside, stopping: true);   // the user started by hand, then stopped in the window
        Assert.Equal(CoreScheduler.Step.None, manual.OnTick(s, inside, running: false));
        manual.OnManualToggle(s, inside, stopping: false);
        Assert.Equal(CoreScheduler.Step.Start, manual.OnTick(s, inside, running: false));
        s.SchedulerEnabled = false;
        Assert.Equal(CoreScheduler.Step.None, new CoreScheduler().OnTick(s, inside, running: false));
        Assert.Equal(CoreScheduler.Step.None, new CoreScheduler().OnStartup(s, inside));
    }

    [Fact]
    public void Ramp_scales_from_the_start_snapshot_skips_visuals_in_a_session_and_restores_on_stop()
    {
        var s = CoreSettings.Current;
        var keep = (s.FlashOpacity, s.MasterVolume, s.PinkFilterOpacity, s.RampMode, s.SchedulerMultiplier, s.RampDurationMinutes,
            s.RampLinkFlashOpacity, s.RampLinkMasterAudio, s.RampLinkPinkFilterOpacity, s.EndSessionOnRampComplete, s.RampCurve);
        var clock = Day;
        var oldNow = CoreIntensityRamp.Now;
        var oldSession = CoreSession.IsSessionRunningProvider;
        var oldDone = CoreIntensityRamp.CompletedProvider;
        bool session = false; int done = 0;
        try
        {
            CoreIntensityRamp.Now = () => clock;
            CoreIntensityRamp.Interval = System.Threading.Timeout.InfiniteTimeSpan;
            CoreSession.IsSessionRunningProvider = () => session;
            CoreIntensityRamp.CompletedProvider = () => done++;
            s.RampMode = RampMode.Multiplier; s.RampCurve = RampCurve.Linear;
            s.SchedulerMultiplier = 2.0; s.RampDurationMinutes = 10;
            s.RampLinkFlashOpacity = s.RampLinkMasterAudio = s.RampLinkPinkFilterOpacity = true;
            s.EndSessionOnRampComplete = true;
            s.FlashOpacity = 40; s.MasterVolume = 40; s.PinkFilterOpacity = 20;

            CoreIntensityRamp.Start();
            clock = Day.AddMinutes(5);   // half way: 1.5x
            CoreIntensityRamp.Tick();
            Assert.Equal((60, 60, 30), (s.FlashOpacity, s.MasterVolume, s.PinkFilterOpacity));

            session = true;   // a session ramps its own visuals; audio still follows
            clock = Day.AddMinutes(10);
            CoreIntensityRamp.Tick();
            Assert.Equal((60, 80, 30), (s.FlashOpacity, s.MasterVolume, s.PinkFilterOpacity));
            Assert.Equal(0, done);   // never ends a running session (#444)

            session = false;
            CoreIntensityRamp.Tick();
            Assert.Equal((80, 40), (s.FlashOpacity, s.PinkFilterOpacity));   // pink capped at 50, 20 x 2 = 40
            Assert.Equal(1, done);

            CoreIntensityRamp.Stop();
            Assert.False(CoreIntensityRamp.IsRunning);
            Assert.Equal((40, 40, 20), (s.FlashOpacity, s.MasterVolume, s.PinkFilterOpacity));
        }
        finally
        {
            CoreIntensityRamp.Stop();
            CoreIntensityRamp.Now = oldNow;
            CoreIntensityRamp.Interval = TimeSpan.FromSeconds(2);
            CoreSession.IsSessionRunningProvider = oldSession;
            CoreIntensityRamp.CompletedProvider = oldDone;
            (s.FlashOpacity, s.MasterVolume, s.PinkFilterOpacity, s.RampMode, s.SchedulerMultiplier, s.RampDurationMinutes,
                s.RampLinkFlashOpacity, s.RampLinkMasterAudio, s.RampLinkPinkFilterOpacity, s.EndSessionOnRampComplete, s.RampCurve) = keep;
        }
    }

    [Fact]
    public void Relapse_unlocks_on_a_start_within_ten_seconds_of_a_stop()
    {
        var dir = Directory.CreateTempSubdirectory("ccp-relapse-").FullName;
        try
        {
            var a = new AchievementEngine(new AchievementStore(Path.Combine(dir, "achievements.json")));
            a.CheckRelapse(Day);   // no stop yet
            a.TrackPanicPressed(Day);
            a.CheckRelapse(Day.AddSeconds(11));
            Assert.False(a.Progress.IsUnlocked("relapse"));
            a.CheckRelapse(Day.AddSeconds(10));
            Assert.True(a.Progress.IsUnlocked("relapse"));
        }
        finally { Directory.Delete(dir, true); }
    }
}
