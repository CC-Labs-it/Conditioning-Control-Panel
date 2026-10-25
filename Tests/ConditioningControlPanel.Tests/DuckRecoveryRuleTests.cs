using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Another app never stays at the ducked volume after CCP closes (sleepysana, 2026-10-20): parked
/// restores stay in ducking_recovery.json, and the launch recovery finds an app by name when its
/// PID changed, and never by a recycled PID.
/// </summary>
public class DuckRecoveryRuleTests
{
    private const int Own = 999;

    [Fact]
    public void Parked_restores_are_kept_beside_the_live_ducks_and_a_live_duck_wins()
    {
        var all = DuckRecoveryRule.ToPersist(
            new Dictionary<int, float> { [1] = 0.8f, [2] = 0.5f },
            new Dictionary<int, float> { [2] = 0.1f, [3] = 0.9f });
        Assert.Equal(new[] { 1, 2, 3 }, all.Keys.OrderBy(k => k));
        Assert.Equal(0.5f, all[2]);
        Assert.Equal(0.9f, all[3]);
        Assert.Empty(DuckRecoveryRule.ToPersist(new Dictionary<int, float>(), new Dictionary<int, float>()));
    }

    [Fact]
    public void The_same_pid_and_name_match()
    {
        var live = new Dictionary<int, string> { [10] = "spotify" };
        Assert.Equal(10, DuckRecoveryRule.Match(10, "Spotify", live, Own));
        Assert.Equal(10, DuckRecoveryRule.Match(10, null, live, Own));   // an old file with no names
    }

    [Fact]
    public void An_app_that_restarted_is_found_by_name()
        => Assert.Equal(42, DuckRecoveryRule.Match(10, "chrome", new Dictionary<int, string> { [42] = "chrome" }, Own));

    [Fact]
    public void A_recycled_pid_never_takes_another_apps_volume()
    {
        var live = new Dictionary<int, string> { [10] = "discord", [11] = "chrome" };
        Assert.Equal(11, DuckRecoveryRule.Match(10, "chrome", live, Own));
        Assert.Null(DuckRecoveryRule.Match(10, "vlc", live, Own));
    }

    [Fact]
    public void Our_own_process_never_matches()
    {
        var live = new Dictionary<int, string> { [Own] = "ConditioningControlPanel" };
        Assert.Null(DuckRecoveryRule.Match(Own, null, live, Own));
        Assert.Null(DuckRecoveryRule.Match(5, "ConditioningControlPanel", live, Own));
    }

    [Fact]
    public void The_service_keeps_owed_volumes_on_disk_and_recovers_through_the_rule()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "AudioService.cs"));
        Assert.Contains("Audio.DuckRecoveryRule.ToPersist(_originalVolumes, _pendingRestores)", src);
        Assert.Contains("Audio.DuckRecoveryRule.Match(kv.Key, name, live, Environment.ProcessId)", src);
        // The unduck no longer deletes the file while restores are parked, and the retry window
        // running out no longer forgets them.
        Assert.DoesNotContain("// Clear crash recovery file\r\n            ClearDuckingState();", src);
        Assert.DoesNotContain("giving up", src);
    }

    private static string RepoRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !d.GetDirectories("ConditioningControlPanel").Any(x => File.Exists(Path.Combine(x.FullName, "ConditioningControlPanel.csproj"))))
            d = d.Parent;
        return d?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
