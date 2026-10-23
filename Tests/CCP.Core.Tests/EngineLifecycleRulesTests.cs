using System;
using System.IO;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class EngineLifecycleRulesTests
{
    private static readonly DateTime Monday = new(2026, 10, 19, 17, 0, 0);

    [Theory]
    [InlineData(10, true)]
    [InlineData(10.001, false)]
    [InlineData(-1, true)] // WPF uses wall time and only an upper bound.
    public void RelapseUsesWpfBoundaryAndPersists(double seconds, bool expected)
    {
        var dir = Directory.CreateTempSubdirectory("ccp-relapse-");
        try
        {
            var store = new AchievementStore(Path.Combine(dir.FullName, "achievements.json"));
            var engine = new AchievementEngine(store);
            engine.CheckRelapse(Monday);
            Assert.False(engine.Progress.IsUnlocked("relapse"));
            engine.TrackPanicPressed(Monday);
            Assert.True(engine.IsDirty);
            engine.CheckRelapse(Monday.AddSeconds(seconds));
            Assert.Equal(expected, engine.Progress.IsUnlocked("relapse"));
            Assert.Equal(expected, store.Load().IsUnlocked("relapse"));
        }
        finally { dir.Delete(true); }
    }

    [Fact]
    public void RampUsesCapturedBasesCapsAndRestoresAllSixValues()
    {
        var s = new AppSettings
        {
            FlashOpacity = 80, SpiralOpacity = 60, PinkFilterOpacity = 40,
            MasterVolume = 70, SubAudioVolume = 30, BrainDrainBlurStrength = 50,
            RampLinkFlashOpacity = true, RampLinkSpiralOpacity = true, RampLinkPinkFilterOpacity = true,
            RampLinkMasterAudio = true, RampLinkSubliminalAudio = true, RampLinkBrainDrain = true,
            SchedulerMultiplier = 3, RampDurationMinutes = 10, EndSessionOnRampComplete = true
        };
        var run = new IntensityRampRun(); run.Start(s, Monday);
        Assert.False(run.Tick(s, Monday.AddMinutes(5), false));
        Assert.Equal((100, 100, 50, 100, 60, 100), Values(s));
        Assert.True(run.Tick(s, Monday.AddMinutes(10), false));
        Assert.Equal(90, s.SubAudioVolume);
        run.Stop(s);
        Assert.Equal((80, 60, 40, 70, 30, 50), Values(s)); Assert.False(run.IsActive);
    }

    [Fact]
    public void PresetOwnsVisualRampsAndCompletionButAudioStillRamps()
    {
        var s = new AppSettings { RampLinkFlashOpacity = true, RampLinkMasterAudio = true,
            FlashOpacity = 40, MasterVolume = 30, SchedulerMultiplier = 2,
            RampDurationMinutes = 10, EndSessionOnRampComplete = true };
        var run = new IntensityRampRun(); run.Start(s, Monday);
        Assert.False(run.Tick(s, Monday.AddMinutes(10), true));
        Assert.Equal(40, s.FlashOpacity); Assert.Equal(60, s.MasterVolume);
        s.RampMode = RampMode.Range; s.RampStartPercent = 100; s.RampEndPercent = 0;
        Assert.True(run.Tick(s, Monday.AddMinutes(10), false));
        Assert.Equal(10, s.FlashOpacity); Assert.Equal(0, s.MasterVolume); // Flash setter has a 10% floor, also in WPF.
        run.Stop(s); Assert.Equal(30, s.MasterVolume);
    }

    private static (int, int, int, int, int, int) Values(AppSettings s) =>
        (s.FlashOpacity, s.SpiralOpacity, s.PinkFilterOpacity, s.MasterVolume, s.SubAudioVolume, s.BrainDrainBlurStrength);

    [Fact]
    public void SchedulerRemembersHandStopUntilExitAndStopsOnlyAutomaticRuns()
    {
        var s = new AppSettings { SchedulerEnabled = true, SchedulerStartTime = "16", SchedulerEndTime = "22" };
        var run = new SchedulerRun();
        Assert.Equal(SchedulerRun.Action.Start, run.CheckOnStartup(s, Monday));
        run.NoteManualStop(s, Monday);
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, Monday, false));
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, Monday.AddHours(6), false));
        Assert.Equal(SchedulerRun.Action.Start, run.Tick(s, Monday.AddDays(1), false));
        Assert.Equal(SchedulerRun.Action.Stop, run.Tick(s, Monday.AddDays(1).AddHours(6), true));
        Assert.Equal(SchedulerRun.Action.None, run.Tick(s, Monday.AddHours(6), true));
        run.NoteManualStop(s, Monday); run.NoteManualStart(); Assert.False(run.ManuallyStoppedDuringSchedule);
    }

    [Fact]
    public void SchedulerUsesTodaysDayForOvernightAndWpfFallbacksAndExclusiveEnd()
    {
        var s = new AppSettings { SchedulerEnabled = true, SchedulerStartTime = "22:0", SchedulerEndTime = "2.5" };
        Assert.True(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(1)));
        Assert.False(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(2).AddMinutes(5)));
        s.SchedulerMonday = false; Assert.False(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(23)));
        s.SchedulerMonday = true; s.SchedulerStartTime = "bad"; s.SchedulerEndTime = "bad";
        Assert.True(SchedulerRun.IsInWindow(s, Monday)); Assert.False(SchedulerRun.IsInWindow(s, Monday.Date.AddHours(22)));
        s.SchedulerEnabled = false;
        Assert.Equal(SchedulerRun.Action.None, new SchedulerRun().CheckOnStartup(s, Monday));
    }
}
