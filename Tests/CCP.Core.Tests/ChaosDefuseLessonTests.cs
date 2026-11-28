using ConditioningControlPanel;
using ConditioningControlPanel.Services.Chaos;
using Xunit;

namespace CCP.Core.Tests;

public class ChaosDefuseLessonTests
{
    // WPF ChaosLessonHooks :150/:196: high water is per loop; brink is channel-only.
    [Fact]
    public void Snaps_judge_channel_brink_and_reset_loop_and_run_tallies()
    {
        int slot = CoreSettings.Current.ChaosActiveSlot;
        try
        {
            ChaosMetaStore.Save(new ChaosMetaState(), 2);
            ChaosMeta.SwitchSlot(2);
            ChaosRunLessons.OnRunStarted();
            ChaosRunLessons.OnDefuseCompleted(0.8, false);
            ChaosRunLessons.OnDefuseCompleted(0.801, true);
            Assert.Equal(0, ChaosLessons.Progress("last_breath"));
            ChaosRunLessons.OnDefuseCompleted(0.8, true);
            Assert.Equal(1, ChaosLessons.Progress("last_breath"));
            Assert.Equal(3, ChaosLessons.Progress("snap_field"));
            ChaosRunLessons.OnLoopCompleted();
            ChaosRunLessons.OnDefuseCompleted(2, false);
            Assert.Equal(3, ChaosLessons.Progress("snap_field"));
            ChaosRunLessons.OnRunStarted();
            ChaosRunLessons.OnDefuseCompleted(2, false);
            ChaosRunLessons.OnDefuseCompleted(2, false);
            ChaosRunLessons.OnDefuseCompleted(2, false);
            Assert.Equal(3, ChaosLessons.Progress("snap_field"));
        }
        finally
        {
            ChaosRunLessons.OnRunStarted();
            ChaosMetaStore.Delete(2);
            CoreSettings.Current.ChaosActiveSlot = slot;
            ChaosMeta.Init();
        }
    }
    [Fact]
    public void Channels_bank_fractional_seconds_on_snap_and_break()
    {
        int slot = CoreSettings.Current.ChaosActiveSlot;
        try
        {
            ChaosMetaStore.Save(new ChaosMetaState(), 2);
            ChaosMeta.SwitchSlot(2);
            ChaosRunLessons.OnRunStarted();
            var now = System.DateTime.UnixEpoch;
            ChaosRunLessons.OnChannelEnded(now); // no approved press
            ChaosRunLessons.OnChannelStarted(now);
            ChaosRunLessons.OnChannelEnded(now.AddMilliseconds(600));
            Assert.Equal(0, ChaosLessons.Progress("slow_fuses"));
            ChaosRunLessons.OnChannelStarted(now);
            ChaosRunLessons.OnChannelEnded(now.AddMilliseconds(600));
            Assert.Equal(1, ChaosLessons.Progress("slow_fuses"));
            Assert.Equal(1.2, ChaosMeta.State.TotalChannelSeconds, 6);
            ChaosRunLessons.OnChannelEnded(now.AddSeconds(3)); // no double credit
            Assert.Equal(1.2, ChaosMeta.State.TotalChannelSeconds, 6);
            ChaosRunLessons.OnChannelStarted(now);
            ChaosRunLessons.OnChannelEnded(now.AddSeconds(30)); // stale channel clamp
            Assert.Equal(2.45, ChaosMeta.State.TotalChannelSeconds, 6);
            Assert.Equal(2, ChaosLessons.Progress("slow_fuses"));
        }
        finally
        {
            ChaosRunLessons.OnRunStarted();
            ChaosMetaStore.Delete(2);
            CoreSettings.Current.ChaosActiveSlot = slot;
            ChaosMeta.Init();
        }
    }

}
