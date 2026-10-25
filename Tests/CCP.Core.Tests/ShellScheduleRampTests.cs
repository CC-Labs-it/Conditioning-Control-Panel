using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class ShellScheduleRampTests
{
    [Fact]
    public void ScheduleStartsOnceHonoursManualStopAndResetsOutsideWindow()
    {
        var s = new AppSettings { SchedulerEnabled = true, SchedulerMonday = true, SchedulerStartTime = "16:00", SchedulerEndTime = "22:00" };
        var now = new DateTime(2026, 10, 26, 16, 0, 0);
        var schedule = new ShellSchedule();
        Assert.Equal(1, schedule.Tick(s, now, false));
        Assert.Equal(0, schedule.Tick(s, now, false));
        schedule.ManualStop(s, now);
        Assert.Equal(0, schedule.Tick(s, now, false));
        Assert.Equal(-1, schedule.Tick(s, now.AddHours(6), true));
        Assert.Equal(1, schedule.Tick(s, now.AddDays(7), false));
        s.SchedulerEnabled = false;
        Assert.Equal(0, schedule.Tick(s, now.AddHours(8), true));
    }
    [Fact]
    public void ScheduleUsesCurrentDayOvernightAndFallbackTimes()
    {
        var s = new AppSettings { SchedulerMonday = true, SchedulerTuesday = false, SchedulerStartTime = "22:0", SchedulerEndTime = "2" };
        Assert.True(ShellSchedule.InWindow(s, new DateTime(2026, 10, 26, 23, 0, 0)));
        Assert.False(ShellSchedule.InWindow(s, new DateTime(2026, 10, 27, 1, 0, 0)));
        s.SchedulerStartTime = "invalid"; s.SchedulerEndTime = "invalid";
        Assert.True(ShellSchedule.InWindow(s, new DateTime(2026, 10, 26, 16, 0, 0)));
        Assert.False(ShellSchedule.InWindow(s, new DateTime(2026, 10, 26, 22, 0, 0)));
    }
    [Fact]
    public void RampRestoresBasesAndNeverCompletesAPresetSession()
    {
        var s = new AppSettings { IntensityRampEnabled = true, RampDurationMinutes = 1,
            RampMode = RampMode.Range, RampStartPercent = 100, RampEndPercent = 200,
            RampLinkFlashOpacity = true, RampLinkMasterAudio = true, EndSessionOnRampComplete = true,
            FlashOpacity = 30, MasterVolume = 40 };
        var now = DateTime.Now;
        var ramp = new ShellRamp();
        ramp.Start(s, now);
        try
        {
            Assert.False(ramp.Tick(s, now.AddMinutes(s.RampDurationMinutes), true));
            Assert.Equal(30, s.FlashOpacity);
            Assert.Equal(80, s.MasterVolume);
            Assert.True(ramp.Tick(s, now.AddMinutes(s.RampDurationMinutes), false));
            Assert.Equal(60, s.FlashOpacity);
        }
        finally { ramp.Stop(s); }
        Assert.Equal(30, s.FlashOpacity);
        Assert.Equal(40, s.MasterVolume);
        Assert.False(ramp.Tick(s, now.AddMinutes(s.RampDurationMinutes + 1), false));
    }
    [Fact]
    public void RampCrashJournalHealsBeforeNextRun()
    {
        var s = new AppSettings { IntensityRampEnabled = true, FlashOpacity = 31, MasterVolume = 42 };
        Directory.CreateDirectory(CorePaths.UserData);   // a fresh runner has none; the app always does
        var ramp = new ShellRamp();
        ramp.Start(s, DateTime.Now);
        try
        {
            s.FlashOpacity = 99; s.MasterVolume = 1;
            Assert.True(ShellRamp.HealOnLaunch(s));
            Assert.Equal(31, s.FlashOpacity);
            Assert.Equal(42, s.MasterVolume);
            Assert.False(ShellRamp.HealOnLaunch(s));   // journal gone: nothing to save
        }
        finally { ramp.Stop(s); }
    }
}
