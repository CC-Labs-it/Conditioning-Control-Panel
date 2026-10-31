using System;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

// WPF MainWindow.StartStop.cs:620-789, the plain intensity ramp.
[Collection(SessionStatics.Name)]
public sealed class EngineRampTests
{
    private static readonly DateTime Day = new(2026, 10, 22);

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
    public void Engine_start_arms_the_enabled_ramp_and_stop_restores_and_clears_its_crash_journal()
    {
        var s = CoreSettings.Current;
        var keep = (s.IntensityRampEnabled, s.MasterVolume, s.RampLinkMasterAudio, s.AudioOnlySession);
        var dir = Directory.CreateTempSubdirectory("ccp-ramp-").FullName;
        var oldDir = CoreIntensityRamp.JournalDir;
        try
        {
            CoreIntensityRamp.Interval = System.Threading.Timeout.InfiniteTimeSpan;
            CoreIntensityRamp.JournalDir = dir;
            s.AudioOnlySession = true;
            s.IntensityRampEnabled = true;
            s.MasterVolume = 37;
            CoreEngine.Start();
            Assert.True(CoreIntensityRamp.IsRunning);
            var path = Path.Combine(dir, SessionSettingsJournal.RampFileName);
            Assert.True(File.Exists(path));
            s.MasterVolume = 82;
            var recovered = new AppSettings { MasterVolume = 82 };
            SessionSettingsJournal.ApplyRamp(File.ReadAllText(path), recovered);
            Assert.Equal(37, recovered.MasterVolume);
            CoreIntensityRamp.Start(); // cannot overwrite the original base on a duplicate
            CoreEngine.Stop();
            Assert.Equal(37, s.MasterVolume);
            Assert.False(CoreIntensityRamp.IsRunning);
            Assert.False(File.Exists(path));
            s.IntensityRampEnabled = false;
            CoreEngine.Start();
            Assert.False(CoreIntensityRamp.IsRunning);
        }
        finally
        {
            CoreEngine.Stop();
            CoreIntensityRamp.JournalDir = oldDir;
            CoreIntensityRamp.Interval = TimeSpan.FromSeconds(2);
            (s.IntensityRampEnabled, s.MasterVolume, s.RampLinkMasterAudio, s.AudioOnlySession) = keep;
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void Range_ramp_uses_all_six_links_caps_pink_and_completes_by_stopping_the_engine()
    {
        var s = CoreSettings.Current;
        var saved = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        var now = CoreIntensityRamp.Now;
        try
        {
            CoreIntensityRamp.Interval = System.Threading.Timeout.InfiniteTimeSpan;
            var clock = Day;
            CoreIntensityRamp.Now = () => clock;
            s.IntensityRampEnabled = true;
            s.RampMode = RampMode.Range; s.RampCurve = RampCurve.Linear;
            s.RampStartPercent = 200; s.RampEndPercent = 0; s.RampDurationMinutes = 10;
            s.RampLinkFlashOpacity = s.RampLinkSpiralOpacity = s.RampLinkPinkFilterOpacity = true;
            s.RampLinkMasterAudio = s.RampLinkSubliminalAudio = s.RampLinkBrainDrain = true;
            s.FlashOpacity = s.SpiralOpacity = s.MasterVolume = s.SubAudioVolume = s.BrainDrainBlurStrength = 60;
            s.PinkFilterOpacity = 40; s.EndSessionOnRampComplete = true;
            CoreEngine.Start();
            CoreIntensityRamp.Tick();
            Assert.Equal((100, 100, 50, 100, 100, 100),
                (s.FlashOpacity, s.SpiralOpacity, s.PinkFilterOpacity, s.MasterVolume, s.SubAudioVolume, s.BrainDrainBlurStrength));
            clock = Day.AddMinutes(9);
            CoreIntensityRamp.Tick();
            Assert.InRange(s.SpiralOpacity, 11, 12);
            Assert.Equal(s.SpiralOpacity, s.BrainDrainBlurStrength);
            clock = Day.AddMinutes(10);
            CoreIntensityRamp.Tick();
            Assert.False(CoreEngine.IsRunning || CoreIntensityRamp.IsRunning);
            Assert.Equal((60, 60, 40, 60, 60, 60),
                (s.FlashOpacity, s.SpiralOpacity, s.PinkFilterOpacity, s.MasterVolume, s.SubAudioVolume, s.BrainDrainBlurStrength));
        }
        finally
        {
            CoreEngine.Stop();
            CoreIntensityRamp.Now = now;
            CoreIntensityRamp.Interval = TimeSpan.FromSeconds(2);
            Newtonsoft.Json.JsonConvert.PopulateObject(saved, s);
        }
    }

    [Fact]
    public void A_tick_queued_before_stop_cannot_ramp_the_next_run()
    {
        var s = CoreSettings.Current;
        var saved = Newtonsoft.Json.JsonConvert.SerializeObject(s);
        var post = CoreDispatch.PostProvider;
        var oldNow = CoreIntensityRamp.Now;
        var posted = new System.Threading.ManualResetEventSlim();
        Action? queued = null;
        try
        {
            CoreDispatch.PostProvider = action =>
            {
                System.Threading.Interlocked.CompareExchange(ref queued, action, null);
                posted.Set();
            };
            CoreIntensityRamp.Now = () => Day;
            s.RampMode = RampMode.Multiplier; s.SchedulerMultiplier = 2;
            s.RampLinkMasterAudio = true; s.MasterVolume = 40;
            CoreIntensityRamp.Interval = TimeSpan.FromMilliseconds(1);
            CoreIntensityRamp.Start();
            Assert.True(posted.Wait(TimeSpan.FromSeconds(5)));
            CoreIntensityRamp.Stop();
            CoreIntensityRamp.Interval = System.Threading.Timeout.InfiniteTimeSpan;
            CoreIntensityRamp.Start();
            CoreIntensityRamp.Now = () => Day.AddHours(1);
            queued!();
            Assert.Equal(40, s.MasterVolume);
        }
        finally
        {
            CoreIntensityRamp.Stop();
            CoreDispatch.PostProvider = post;
            CoreIntensityRamp.Now = oldNow;
            CoreIntensityRamp.Interval = TimeSpan.FromSeconds(2);
            Newtonsoft.Json.JsonConvert.PopulateObject(saved, s);
        }
    }
}
