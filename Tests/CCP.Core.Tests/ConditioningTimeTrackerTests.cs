using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>WPF's conditioning-time tracker (MainWindow.UiUpdates.cs:746-842) on a stepped clock: a whole
/// minute per 60 ticks while tracking, nothing after Stop, the wall-clock remainder on Stop, and a
/// settings save behind every write (SkillTreeService.AddConditioningTime :659-669).</summary>
[Collection(SessionStatics.Name)]
public sealed class ConditioningTimeTrackerTests
{
    private sealed class SteppedClock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static void Seconds(ConditioningTimeTracker t, AppSettings s, SteppedClock c, int n)
    {
        for (var i = 0; i < n; i++) { c.Now = c.Now.AddSeconds(1); t.Tick(s); }
    }

    [Fact]
    public void DelayedTicksCreditRealMinutesAndRestartDiscardsPendingTime()
    {
        var s = new AppSettings();
        var clock = new SteppedClock();
        var tracker = new ConditioningTimeTracker(clock);
        tracker.Start(s);
        for (var i = 0; i < 47; i++)
        {
            clock.Now = clock.Now.AddSeconds(1.3);
            tracker.Tick(s);
        }
        Assert.Equal(1, s.TotalConditioningMinutes);
        tracker.Stop(s);
        var baseline = s.TotalConditioningMinutes;
        tracker.Start(s);
        Seconds(tracker, s, clock, 59);
        Assert.Equal(baseline, s.TotalConditioningMinutes);
    }

    [Fact]
    public void LongTickGapsAreCappedAndBackwardsTicksDoNotCredit()
    {
        var s = new AppSettings();
        var clock = new SteppedClock();
        var tracker = new ConditioningTimeTracker(clock);
        tracker.Start(s);
        clock.Now = clock.Now.AddHours(8);
        tracker.Tick(s);
        clock.Now = clock.Now.AddSeconds(-5);
        tracker.Tick(s);
        Seconds(tracker, s, clock, 44);
        Assert.Equal(0, s.TotalConditioningMinutes);
        Seconds(tracker, s, clock, 1);
        Assert.Equal(1, s.TotalConditioningMinutes);
    }

    [Fact]
    public void AccruesAMinutePerSixtyTicksAndTheRemainderOnStop()
    {
        var s = new AppSettings { TotalConditioningMinutes = 10 };
        var clock = new SteppedClock();
        var t = new ConditioningTimeTracker(clock);
        t.Start(s);
        Seconds(t, s, clock, 59);
        Assert.Equal(10, s.TotalConditioningMinutes);          // nothing before the 60th tick
        Assert.Equal(10 + 59 / 60.0, t.LiveMinutes(s), 6);      // the pill counts live
        Seconds(t, s, clock, 1);
        Assert.Equal(11, s.TotalConditioningMinutes);
        Seconds(t, s, clock, 90);
        Assert.Equal(12, s.TotalConditioningMinutes);
        t.Stop(s);                                              // 150 s in all: 30 s still owed
        Assert.Equal(12.5, s.TotalConditioningMinutes, 6);
        Assert.False(t.IsTracking);

        Seconds(t, s, clock, 120);                              // stopped (Stop button / panic): nothing
        t.Stop(s);
        Assert.Equal(12.5, s.TotalConditioningMinutes, 6);
        Assert.Equal(12.5, t.LiveMinutes(s), 6);
    }

    [Fact]
    public void StartWhileTrackingKeepsTheBaselineAndStopNeverCountsTwice()
    {
        var s = new AppSettings { TotalConditioningMinutes = 0 };
        var clock = new SteppedClock();
        var t = new ConditioningTimeTracker(clock);
        t.Start(s);
        Seconds(t, s, clock, 30);
        t.Start(s);                                             // WPF: "Already started"
        Seconds(t, s, clock, 30);
        Assert.Equal(1, s.TotalConditioningMinutes);
        // A sync lift above baseline + elapsed (ProfileSync takes the higher total): no remainder.
        s.TotalConditioningMinutes = 50;
        t.Stop(s);
        Assert.Equal(50, s.TotalConditioningMinutes);
    }

    [Fact]
    public void EveryWriteSavesSettings()
    {
        var old = CoreSettings.ServiceProvider;
        var svc = new SettingsService();
        try
        {
            CoreSettings.ServiceProvider = () => svc;
            SettingsService.DisarmAllPendingSaves();
            ConditioningTimeTracker.AddConditioningTime(svc.Current, 0);
            Assert.False(svc.HasPendingSave);                   // nothing to add, nothing to write
            var before = svc.Current.TotalConditioningMinutes;
            ConditioningTimeTracker.AddConditioningTime(svc.Current, 1.0);
            Assert.Equal(before + 1, svc.Current.TotalConditioningMinutes);
            Assert.True(svc.HasPendingSave);
        }
        finally
        {
            SettingsService.DisarmAllPendingSaves();
            CoreSettings.ServiceProvider = old;
        }
    }
}
