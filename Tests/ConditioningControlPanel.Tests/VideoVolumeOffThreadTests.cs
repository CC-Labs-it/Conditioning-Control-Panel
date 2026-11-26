using System;
using System.IO;
using System.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1697 (7.3.6): the UI thread froze 15 s inside LibVLC's set-volume, called from the master
/// volume ramp on every dispatcher tick. The LibVLC players now take their volume from a pool thread,
/// holding the player lock (CloseAll clears the list under it before any dispose).
/// </summary>
public class VideoVolumeOffThreadTests
{
    [Fact]
    public void The_dispatcher_path_never_calls_LibVLC_volume_itself()
    {
        var src = Read("Services", "Video", "VideoService.cs");
        var update = Member(src, "private void UpdatePlayingVideosVolume(");
        Assert.DoesNotContain("player.Volume", update);
        Assert.DoesNotContain("player.Mute", update);
        Assert.Contains("PushLibVlcVolume(effectiveVolume);", update);
    }

    [Fact]
    public void The_worker_runs_off_thread_under_the_player_lock_and_skips_a_teardown()
    {
        var push = Member(Read("Services", "Video", "VideoService.cs"), "private void PushLibVlcVolume(");
        Assert.Contains("Task.Run(", push);
        Assert.Contains("lock (_mediaPlayersLock)", push);
        Assert.Contains("if (_isCleaningUp) continue;", push);
        Assert.Contains("Interlocked.Exchange(ref _pendingLibVlcVolume, -1)", push);
        Assert.Contains("player.Mute = want <= 0;", push);
    }

    private static string Member(string src, string signature)
    {
        var at = src.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(at >= 0, signature);
        var next = src.IndexOf("\n        p", at + signature.Length, StringComparison.Ordinal);
        return next > at ? src[at..next] : src[at..];
    }

    private static string Read(params string[] parts)
        => File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
