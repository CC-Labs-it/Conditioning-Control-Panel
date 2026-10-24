using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Timing;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// Gaze dwells count the real time between timer ticks (<see cref="TickStep"/>), not a fixed TickMs
/// per tick. A 33 ms DispatcherTimer lands every ~47 ms at the default Windows timer resolution
/// and later under load, so the attention check's 1 s stare took ~1.4 s against its wall-clock
/// grace, the Lab gaze game's 3 s pass took ~3.75 s of a 4 s window, and the calibration bubbles
/// ate their timeout. Same class as ccp-bugs #1489 (ConditioningClock).
/// </summary>
public class TickStepTests
{
    private static readonly DateTime T0 = new(2026, 10, 22, 3, 0, 0, DateTimeKind.Utc);

    /// <summary>Ticks every <paramref name="periodMs"/> from T0; how long until the dwell reaches the target.</summary>
    private static double RealMsToReach(double targetMs, double periodMs, bool countTicks, double nominalMs)
    {
        var last = T0;
        double dwell = 0;
        for (int i = 1; i < 10_000; i++)
        {
            var now = T0.AddMilliseconds(i * periodMs);
            dwell += countTicks ? nominalMs : TickStep.Ms(ref last, now, TickStep.DwellMaxStepMs);
            if (dwell >= targetMs) return i * periodMs;
        }
        return double.PositiveInfinity;
    }

    [Fact]
    public void A_steady_stare_on_a_late_timer_passes_in_real_time()
    {
        // The attention check: 1000 ms on target, a 33 ms timer that really lands every 47 ms.
        var counted = RealMsToReach(1000, 47, countTicks: true, nominalMs: 33);
        var measured = RealMsToReach(1000, 47, countTicks: false, nominalMs: 33);

        Assert.True(counted > 1400, $"counting ticks took {counted} ms");
        Assert.InRange(measured, 1000, 1047);
    }

    [Fact]
    public void The_gaze_game_pass_fits_its_window_again()
    {
        // 3 s pass, 4 s of window after the 1 s grace, a 50 ms timer landing every 62.5 ms.
        Assert.True(RealMsToReach(3000, 62.5, countTicks: true, nominalMs: 50) > 3700);
        Assert.InRange(RealMsToReach(3000, 62.5, countTicks: false, nominalMs: 50), 3000, 3063);
    }

    [Fact]
    public void A_stall_credits_at_most_the_cap()
    {
        var last = T0;
        Assert.Equal(TickStep.DwellMaxStepMs, TickStep.Ms(ref last, T0.AddSeconds(2), TickStep.DwellMaxStepMs));
        Assert.Equal(T0.AddSeconds(2), last);
    }

    [Fact]
    public void An_unset_stamp_or_a_clock_going_back_credits_nothing()
    {
        DateTime unset = default;
        Assert.Equal(0, TickStep.Ms(ref unset, T0, 200));
        Assert.Equal(T0, unset);

        var last = T0;
        Assert.Equal(0, TickStep.Ms(ref last, T0.AddMilliseconds(-50), 200));
        Assert.Equal(0, TickStep.Ms(ref last, T0.AddMilliseconds(-50), 200)); // same instant again
    }

    [Fact]
    public void Normal_ticks_credit_their_real_gap()
    {
        var last = T0;
        Assert.Equal(47, TickStep.Ms(ref last, T0.AddMilliseconds(47), 200), 6);
        Assert.Equal(16, TickStep.Ms(ref last, T0.AddMilliseconds(63), 200), 6);
    }

    // ---- wiring: every gaze dwell reads the step, none adds TickMs per tick ----------------

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        Assert.True(dir != null, "could not locate the repo root from " + AppContext.BaseDirectory);
        return dir!.FullName;
    }

    private static string ReadSource(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { RepoRoot(), "ConditioningControlPanel" }.Concat(parts).ToArray()));

    [Theory]
    [InlineData("Services/AttentionCheckService.cs", "_dwellMs += TickMs")]
    [InlineData("Lab/GazeMinigame/GazeMinigameWindow.xaml.cs", "_correctMs += tickMs")]
    // The calibration window's bubble dwell is Core BubbleAccuracyTest.Dwell since the split (both heads run it).
    [InlineData("Services/Webcam/BubbleAccuracyTest.cs", "DwellMs += TickMs")]
    public void Gaze_dwells_count_real_time(string file, string oldCount)
    {
        var src = SourceRoots.ReadProductFile(file.Split('/'));
        Assert.DoesNotContain(oldCount, src);
        Assert.Contains("TickStep.Ms(ref ", src);
    }

    [Fact]
    public void Overlay_minutes_use_the_running_time_credit()
    {
        var src = ReadSource("Services", "Progression", "AchievementService.cs");
        Assert.DoesNotContain("elapsed < 0.1", src);
        foreach (var credit in new[] { "_pinkFilterCredit", "_spiralCredit", "_brainDrainCredit", "_deeperCredit" })
            Assert.Contains(credit + ".Sample(", src);
    }
}
