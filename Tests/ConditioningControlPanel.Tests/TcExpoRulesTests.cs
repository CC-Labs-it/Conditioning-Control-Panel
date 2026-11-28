using System;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The Llama Expo download as pure rules (Contract v2 TRANSMISSION) and the caps it feeds.</summary>
public class TcExpoRulesTests
{
    [Fact]
    public void Full_caps_name_the_expo_state()
    {
        var ready = TcRules.Caps("full", true, TcExpoRules.Cap(new TcExpoState(TcExpoPhase.Ready, 100), TcRules.ExpoBase, 250));
        Assert.Equal("ready", (string?)ready["expo"]!["state"]);
        Assert.Equal(TcRules.ExpoBase, (string?)ready["expo"]!["base"]);
        Assert.Null(ready["expo"]!["mb"]);

        var absent = TcRules.Caps("full", true, TcExpoRules.Cap(TcExpoState.Absent, TcRules.ExpoBase, 250));
        Assert.Equal("absent", (string?)absent["expo"]!["state"]);
        Assert.Equal(250, (int)absent["expo"]!["mb"]!);
        Assert.Null(absent["expo"]!["base"]);
        Assert.Null(TcExpoRules.Cap(TcExpoState.Absent, TcRules.ExpoBase, null)["mb"]);
    }

    [Fact]
    public void Only_a_subscriber_may_start()
    {
        var s = TcExpoRules.Start(TcExpoState.Absent, subscriber: false, offlineMode: false);
        Assert.Equal(TcExpoPhase.Failed, s.Phase);
        Assert.Equal("refused", s.Reason);
        Assert.Equal(TcExpoPhase.Fetching, TcExpoRules.Start(TcExpoState.Absent, true, false).Phase);
    }

    [Fact]
    public void Offline_mode_fails_as_offline()
        => Assert.Equal("offline", TcExpoRules.Start(TcExpoState.Absent, true, offlineMode: true).Reason);

    [Fact]
    public void Cancel_keeps_progress_and_a_restart_resumes()
    {
        var s = TcExpoRules.Progress(TcExpoRules.Start(TcExpoState.Absent, true, false), 42);
        var cancelled = TcExpoRules.Cancel(s);
        Assert.Equal(TcExpoPhase.Absent, cancelled.Phase);
        Assert.Equal(42, cancelled.Pct);
        var again = TcExpoRules.Start(cancelled, true, false);
        Assert.Equal(TcExpoPhase.Fetching, again.Phase);
        Assert.Equal(42, again.Pct);
    }

    [Fact]
    public void Failure_keeps_progress_and_cleans_the_reason()
    {
        var s = TcExpoRules.Progress(TcExpoRules.Start(TcExpoState.Absent, true, false), 10);
        Assert.Equal("disk", TcExpoRules.Fail(s, "disk").Reason);
        Assert.Equal(10, TcExpoRules.Fail(s, "disk").Pct);
        Assert.Equal("refused", TcExpoRules.Fail(s, "anything").Reason);
        // A failed reading can be started again.
        Assert.Equal(TcExpoPhase.Fetching, TcExpoRules.Start(TcExpoRules.Fail(s, "offline"), true, false).Phase);
    }

    [Fact]
    public void Ready_stays_ready()
    {
        var ready = TcExpoRules.Complete(TcExpoRules.Start(TcExpoState.Absent, true, false));
        Assert.Equal(TcExpoPhase.Ready, ready.Phase);
        Assert.Equal(ready, TcExpoRules.Start(ready, subscriber: false, offlineMode: true));
        Assert.Equal(ready, TcExpoRules.Cancel(ready));
        Assert.Equal(ready, TcExpoRules.Fail(ready, "disk"));
        Assert.Equal(ready, TcExpoRules.Progress(ready, 3));
    }

    [Fact]
    public void Progress_only_moves_a_running_download_and_never_reads_100_before_ready()
    {
        Assert.Equal(TcExpoState.Absent, TcExpoRules.Progress(TcExpoState.Absent, 50));
        var s = TcExpoRules.Start(TcExpoState.Absent, true, false);
        Assert.Equal(99, TcExpoRules.Progress(s, 100).Pct);
        Assert.Equal(0, TcExpoRules.Progress(s, -4).Pct);
    }

    [Theory]
    [InlineData(true, 0, 0, "Ready", 100)]
    [InlineData(false, 0, 1000, "Absent", 0)]
    [InlineData(false, 500, 1000, "Absent", 50)]
    [InlineData(false, 1000, 1000, "Absent", 100)]   // all bytes, no stamp: not ready until checked
    public void Disk_reading_trusts_only_the_stamp(bool stamp, long partial, long total, string phase, int pct)
    {
        var s = TcExpoRules.FromDisk(stamp, partial, total);
        Assert.Equal(phase, s.Phase.ToString());
        Assert.Equal(pct, s.Pct);
    }

    [Fact]
    public void Progress_goes_out_at_most_twice_a_second()
    {
        var t0 = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Utc);
        Assert.True(TcExpoRules.ShouldSendProgress(null, 0, t0, 1));
        Assert.False(TcExpoRules.ShouldSendProgress(t0, 1, t0.AddMilliseconds(200), 2));
        Assert.True(TcExpoRules.ShouldSendProgress(t0, 1, t0.AddMilliseconds(500), 2));
        Assert.False(TcExpoRules.ShouldSendProgress(t0, 2, t0.AddSeconds(5), 2));
    }

    [Fact]
    public void Frames_follow_the_contract()
    {
        Assert.Null(TcExpoRules.Frame(TcExpoState.Absent, TcRules.ExpoBase));
        var p = TcExpoRules.Frame(new TcExpoState(TcExpoPhase.Fetching, 7), TcRules.ExpoBase)!;
        Assert.Equal("expo-progress", (string?)p["type"]);
        Assert.Equal(7, (int)p["pct"]!);
        var r = TcExpoRules.Frame(new TcExpoState(TcExpoPhase.Ready, 100), TcRules.ExpoBase)!;
        Assert.Equal("expo-ready", (string?)r["type"]);
        Assert.Equal(TcRules.ExpoBase, (string?)r["base"]);
        var f = TcExpoRules.Frame(new TcExpoState(TcExpoPhase.Failed, 3, "disk"), TcRules.ExpoBase)!;
        Assert.Equal("expo-failed", (string?)f["type"]);
        Assert.Equal("disk", (string?)f["reason"]);
    }
}
