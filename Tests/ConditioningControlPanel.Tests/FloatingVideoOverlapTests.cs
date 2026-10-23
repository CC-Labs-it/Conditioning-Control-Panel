using System.IO;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Floating videos overlap now and then (owner, 2026-10-18): while a window is up the next one
/// joins after a quarter of the picked gap, so a higher frequency means more overlap. Before, the
/// run's own Cleanup re-armed a full gap and two windows almost never met.
/// </summary>
public class FloatingVideoOverlapTests
{
    [Theory]
    [InlineData(20, 45)]   // 180 s gap
    [InlineData(10, 90)]
    [InlineData(6, 150)]
    [InlineData(4, 225)]
    [InlineData(2, 450)]
    [InlineData(1, 600)]   // 900 s, held at the ceiling
    public void Joins_after_a_quarter_of_the_gap(int perHour, double expected)
        => Assert.Equal(expected, MandatoryVideoPresentation.JoinDelaySeconds(perHour, 4, 0.5)!.Value, 6);

    [Fact]
    public void A_higher_frequency_never_joins_later()
    {
        double? last = null;
        for (var perHour = 1; perHour <= 20; perHour++)
        {
            var d = MandatoryVideoPresentation.JoinDelaySeconds(perHour, 2, 0.5)!.Value;
            if (last.HasValue) Assert.True(d <= last.Value, $"{perHour}/h joined later than {perHour - 1}/h");
            last = d;
        }
    }

    [Fact]
    public void Always_sooner_than_a_fresh_video_and_never_instant()
    {
        for (var perHour = 1; perHour <= 20; perHour++)
            foreach (var unit in new[] { 0.0, 1.0 })
            {
                var d = MandatoryVideoPresentation.JoinDelaySeconds(perHour, 4, unit)!.Value;
                Assert.True(d < 3600.0 / perHour * 0.8, $"{perHour}/h join {d}s is no sooner than the next video");
                Assert.True(d >= MandatoryVideoPresentation.MinJoinSeconds * 0.8);
            }
    }

    [Theory]
    [InlineData(0.0, 120)]
    [InlineData(1.0, 180)]
    [InlineData(-3.0, 120)]
    [InlineData(7.0, 180)]
    public void Jitter_is_plus_or_minus_a_fifth(double unit, double expected)
        => Assert.Equal(expected, MandatoryVideoPresentation.JoinDelaySeconds(6, 3, unit)!.Value, 6);

    [Theory]
    [InlineData(1)]
    [InlineData(0)]
    [InlineData(-2)]
    public void One_window_at_most_keeps_the_ordinary_gap(int limit)
        => Assert.Null(MandatoryVideoPresentation.JoinDelaySeconds(12, limit, 0.5));

    /// <summary>Both places that re-arm the scheduler during a run use the join delay.</summary>
    [Fact]
    public void The_run_rearms_on_the_join_delay()
    {
        var dir = Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "Video");
        Assert.Contains("if (_isRunning) ScheduleNext(FloatingJoinDelay());",
            File.ReadAllText(Path.Combine(dir, "VideoService.FloatingInstances.cs")));
        Assert.Contains("if (duringFloatingRun && _isRunning) ScheduleNext(FloatingJoinDelay());",
            File.ReadAllText(Path.Combine(dir, "VideoService.cs")));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(System.AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root");
        return dir!.FullName;
    }
}
