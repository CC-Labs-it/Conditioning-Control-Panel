using System;
using System.IO;
using ConditioningControlPanel.Services;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// #1484 (privMB 2003 -> 8098 in 35 min, GC heap flat) and #1488 (error 1455, commit limit): the
/// [RES] line says native memory grows, not which allocator. MemoryJumpRule decides when a
/// [RES-JUMP] breakdown is logged and how it reads; MemoryBreakdownCollector walks the address space.
/// </summary>
public class MemoryJumpRuleTests
{
    private static MemoryBreakdown Sample(bool partial = false) => new(
        PrivateMB: 2350, MappedMB: 80, ImageMB: 150, HeapsMB: 900, HeapCount: 14, GcCommittedMB: 270,
        TopPrivateRegionsMB: new double[] { 256, 128, 256, 128, 256, 64.2 }, ElapsedMs: 12, Partial: partial);

    [Theory]
    [InlineData(2000, 2299, 2000, 9999, false)] // +299 per sample, +299 since report: quiet
    [InlineData(2000, 2300, 2000, 9999, true)]  // +300 per sample: jump
    [InlineData(2400, 2499, 2000, 9999, false)] // +99 per sample, +499 creep: quiet
    [InlineData(2400, 2500, 2000, 9999, true)]  // +500 creep: report
    [InlineData(2000, 1500, 2000, 9999, false)] // shrinking never reports
    public void Thresholds(double prev, double now, double lastReported, double secs, bool expected)
        => Assert.Equal(expected, MemoryJumpRule.IsJump(prev, now, lastReported, secs));

    [Fact]
    public void RateLimit_OnePerTenMinutes_UnlessAGigabyteMore()
    {
        // A +400 jump 5 minutes after the last breakdown waits...
        Assert.False(MemoryJumpRule.IsJump(2000, 2400, 2000, 300));
        // ...the same jump after 10 minutes reports...
        Assert.True(MemoryJumpRule.IsJump(2000, 2400, 2000, 600));
        // ...and a 1 GB rise since the last breakdown always reports.
        Assert.True(MemoryJumpRule.IsJump(2000, 3024, 2000, 30));
        Assert.False(MemoryJumpRule.IsJump(2600, 3023, 2000, 30));
    }

    [Fact]
    public void FormatJumpLine_Shape()
    {
        string line = MemoryJumpRule.FormatJumpLine("jump", 2003, 2480, 2003, 120, Sample(), "uptime=5s op=(idle)");
        Assert.Equal(
            "[RES-JUMP] jump priv 2003->2480 MB (+477 in 120 s, +477 since last breakdown)" +
            " | private 2350 = heaps 900 (n=14) + gc 270 + other 1180 | mapped 80 image 150" +
            " | top private MB: 256x3, 128x2, 64 | walk 12 ms | state: uptime=5s op=(idle)",
            line);
    }

    [Fact]
    public void FormatJumpLine_Baseline_PartialAndUnknownHeaps()
    {
        var b = Sample(partial: true) with { HeapsMB = -1, TopPrivateRegionsMB = Array.Empty<double>() };
        string line = MemoryJumpRule.FormatJumpLine("baseline", 2003, 2003, 2003, 0, b, null);
        Assert.StartsWith("[RES-JUMP] baseline priv 2003 MB | private 2350 = heaps ? (n=14) + gc 270 + other 2080", line);
        Assert.Contains("top private MB: (none)", line);
        Assert.Contains("walk 12 ms (partial)", line);
        Assert.EndsWith("state: (unavailable)", line);
    }

    [Theory]
    [InlineData(null, 60)]
    [InlineData("", 60)]
    [InlineData("abc", 60)]
    [InlineData("30", 15)]
    [InlineData("3", 5)]     // floor 10 s
    [InlineData("-5", 60)]
    public void SampleInterval_EnvOverride(string? env, int beats)
        => Assert.Equal(beats, MemoryJumpRule.SampleEveryBeats(env, 2000));

    [Theory]
    [InlineData("1", true)]
    [InlineData("true", true)]
    [InlineData("0", false)]
    [InlineData(null, false)]
    public void EverySampleSwitch(string? env, bool on) => Assert.Equal(on, MemoryJumpRule.ForceEverySample(env));

    [Fact]
    public void Collector_ReturnsSaneNumbers_ForThisProcess()
    {
        var b = MemoryBreakdownCollector.Collect();
        Assert.True(b.PrivateMB > 0, b.ToString());
        Assert.True(b.ImageMB > 0, b.ToString());
        Assert.True(b.GcCommittedMB > 0, b.ToString());
        Assert.True(b.HeapCount > 0, b.ToString());
        Assert.True(b.HeapsMB > 0 && b.HeapsMB <= b.PrivateMB + 1, b.ToString());
        Assert.NotEmpty(b.TopPrivateRegionsMB);
        foreach (var r in b.TopPrivateRegionsMB) Assert.True(r <= b.PrivateMB);
        Assert.InRange(b.ElapsedMs, 0, 2000);
        // Sanity against the OS counter the [RES] line uses (private commit; same order of size).
        using var p = System.Diagnostics.Process.GetCurrentProcess();
        double osPrivMB = p.PrivateMemorySize64 / (1024.0 * 1024.0);
        Assert.True(b.PrivateMB >= osPrivMB * 0.5 && b.PrivateMB <= osPrivMB * 1.5, b + " os=" + osPrivMB);
    }

    [Fact]
    public void Wiring_SampleResourcesChecksTheRule_AndTheWalkIsGuarded()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "UI", "UiHangWatchdog.cs"));
        string sample = Between(src, "private static void SampleResources()", "private static void CheckMemoryJump(");
        Assert.Contains("CheckMemoryJump(priv);", sample);
        Assert.Contains("StartBreakdown(\"baseline\"", sample);

        string check = Between(src, "private static void CheckMemoryJump(", "private static void StartBreakdown(");
        Assert.Contains("MemoryJumpRule.IsJump(", check);

        string start = Between(src, "private static void StartBreakdown(", "internal static string FormatResLine(");
        Assert.Contains("new Thread(", start);                       // never on the watchdog or UI thread
        int collect = start.IndexOf("MemoryBreakdownCollector.Collect()", StringComparison.Ordinal);
        Assert.True(collect > 0);
        Assert.True(start.LastIndexOf("try", collect, StringComparison.Ordinal) > 0);
        Assert.Contains("catch { }", start);
        Assert.Contains("VideoDiag.Log(\"RES-JUMP\"", start);

        string bug = File.ReadAllText(Path.Combine(RepoRoot(), "ConditioningControlPanel", "Services", "BugReportService.cs"));
        Assert.Contains("\"[RES-JUMP]\", \"[Res-jump \"", bug);
    }

    private static string Between(string s, string from, string to)
    {
        int a = s.IndexOf(from, StringComparison.Ordinal);
        Assert.True(a >= 0, from);
        int b = s.IndexOf(to, a + from.Length, StringComparison.Ordinal);
        Assert.True(b > a, to);
        return s.Substring(a, b - a);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "MainWindow")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
