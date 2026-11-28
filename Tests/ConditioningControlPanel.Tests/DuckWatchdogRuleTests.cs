using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1762 "audio ducking stopped mid floating video": the 5 minute duck safety net no
/// longer cuts the duck while a playing video still holds it, and still fires for a duck whose
/// owner is gone.
/// </summary>
public class DuckWatchdogRuleTests
{
    [Fact]
    public void A_duck_held_by_a_playing_video_waits_another_period()
        => Assert.Equal(DuckWatchdogStep.Rearm, DuckWatchdogRule.Decide(isDucked: true, ownerAlive: true));

    [Fact]
    public void A_duck_whose_owner_is_gone_is_still_forced_back()
        => Assert.Equal(DuckWatchdogStep.ForceRestore, DuckWatchdogRule.Decide(isDucked: true, ownerAlive: false));

    [Fact]
    public void Nothing_ducked_retires_the_net()
    {
        Assert.Equal(DuckWatchdogStep.Retire, DuckWatchdogRule.Decide(isDucked: false, ownerAlive: false));
        Assert.Equal(DuckWatchdogStep.Retire, DuckWatchdogRule.Decide(isDucked: false, ownerAlive: true));
    }

    [Fact]
    public void The_watchdog_asks_the_rule_and_the_video_is_the_owner()
    {
        var root = RepoRoot();
        var audio = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "AudioService.cs"));
        var tick = audio.Substring(audio.IndexOf("private void DuckWatchdogTick()", StringComparison.Ordinal));
        tick = tick.Substring(0, tick.IndexOf("public void ForceUnduck()", StringComparison.Ordinal));
        Assert.Contains("Audio.DuckWatchdogRule.Decide(_isDucked, DuckOwnerAlive())", tick);
        Assert.Contains("App.Video?.HoldsAudioDuck == true", audio);

        var video = File.ReadAllText(Path.Combine(root, "ConditioningControlPanel", "Services", "Video", "VideoService.cs"));
        Assert.Contains("public bool HoldsAudioDuck => _didDuck && _videoPlaying;", video);
        // Read from the watchdog's timer thread.
        Assert.Contains("private volatile bool _didDuck;", video);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
