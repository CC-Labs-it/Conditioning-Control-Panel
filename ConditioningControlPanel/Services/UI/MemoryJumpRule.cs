using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ConditioningControlPanel.Services;

/// <summary>
/// What one address-space walk found, in MB. Plain data so the rule and its tests never touch
/// Win32 (see <see cref="MemoryBreakdownCollector"/> for the walk itself).
/// </summary>
internal readonly record struct MemoryBreakdown(
    double PrivateMB,          // committed MEM_PRIVATE (VirtualQuery)
    double MappedMB,           // committed MEM_MAPPED (file views, shared sections, GPU mappings)
    double ImageMB,            // committed MEM_IMAGE (exe + dlls)
    double HeapsMB,            // native heaps, HeapSummary cbCommitted summed (-1 = unavailable)
    int HeapCount,
    double GcCommittedMB,      // GC.GetGCMemoryInfo().TotalCommittedBytes
    IReadOnlyList<double> TopPrivateRegionsMB,
    long ElapsedMs,
    bool Partial);             // the walk hit its time budget and stopped early

/// <summary>
/// When a private-bytes rise deserves a native memory breakdown, and how that breakdown reads.
///
/// WHY (#1484, #1488): one log showed privMB 2003 -> 8098 in 35 minutes while the GC heap stayed
/// 75-270 MB, and another session died with Windows error 1455 (commit limit). The [RES] line
/// says THAT native memory grows, never WHICH allocator. The breakdown splits private commit into
/// native heaps, the GC and everything else (VirtualAlloc'd buffers: Skia/WPF bitmaps, LibVLC
/// pictures, D3D staging), and lists the biggest private regions, whose sizes usually name the
/// owner (a 4K BGRA frame is ~32 MB, an 8 MB pattern is a decoded picture, 64 MB is a GC region).
///
/// Pure: numbers in, verdicts and text out.
/// </summary>
internal static class MemoryJumpRule
{
    /// <summary>Private growth between two samples that counts as a jump.</summary>
    internal const double JumpMB = 300;
    /// <summary>Growth above the last logged breakdown that counts as creep (slow leaks).</summary>
    internal const double CreepMB = 500;
    /// <summary>At most one breakdown per this many seconds...</summary>
    internal const double MinGapSeconds = 600;
    /// <summary>...unless private grew at least this much since the last one.</summary>
    internal const double BurstMB = 1024;

    internal const int DefaultSampleSeconds = 120;
    internal const int MinSampleSeconds = 10;

    internal const string EnvEverySample = "CCP_MEM_BREAKDOWN_EVERY_SAMPLE";
    internal const string EnvSampleSeconds = "CCP_RES_SAMPLE_SECONDS";

    /// <summary>
    /// True when this sample deserves a breakdown: private grew by <see cref="JumpMB"/> since the
    /// previous sample, or by <see cref="CreepMB"/> above the last breakdown we logged. Inside
    /// <see cref="MinGapSeconds"/> of the last breakdown only a <see cref="BurstMB"/> rise passes.
    /// </summary>
    internal static bool IsJump(double prevPrivMB, double nowPrivMB, double lastReportedPrivMB,
                                double secondsSinceLastReport)
    {
        double sinceSample = nowPrivMB - prevPrivMB;
        double sinceReport = nowPrivMB - lastReportedPrivMB;
        if (sinceSample < JumpMB && sinceReport < CreepMB) return false;
        if (secondsSinceLastReport < MinGapSeconds && sinceReport < BurstMB) return false;
        return true;
    }

    /// <summary>The sample interval in heartbeats: env seconds (floor 10 s) or the 2-minute default.</summary>
    internal static int SampleEveryBeats(string? envSeconds, int heartbeatMs)
    {
        int seconds = DefaultSampleSeconds;
        if (!string.IsNullOrWhiteSpace(envSeconds)
            && int.TryParse(envSeconds.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var s)
            && s > 0)
            seconds = Math.Max(MinSampleSeconds, s);
        if (heartbeatMs <= 0) heartbeatMs = 2000;
        return Math.Max(1, (int)Math.Round(seconds * 1000.0 / heartbeatMs));
    }

    /// <summary>Desk-run switch: a breakdown on every sample, not only on jumps.</summary>
    internal static bool ForceEverySample(string? env)
        => env != null && (env.Trim() == "1" || env.Trim().Equals("true", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The one-line breakdown. <paramref name="kind"/> is "baseline", "jump" or "sample".
    /// <code>
    /// [RES-JUMP] jump priv 2003->2480 MB (+477 in 120 s, +477 since last breakdown) | private 2350 = heaps 900 (n=14) + gc 270 + other 1180 | mapped 80 image 150 | top private MB: 256x3, 128x5, 64 | walk 12 ms | state: ...
    /// </code>
    /// </summary>
    internal static string FormatJumpLine(string kind, double prevPrivMB, double nowPrivMB,
        double lastReportedPrivMB, double secondsSincePrev, MemoryBreakdown b, string? state)
    {
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(256);
        sb.Append("[RES-JUMP] ").Append(kind).Append(' ');
        if (kind == "baseline")
        {
            sb.Append("priv ").Append(nowPrivMB.ToString("F0", c)).Append(" MB");
        }
        else
        {
            sb.Append("priv ").Append(prevPrivMB.ToString("F0", c)).Append("->")
              .Append(nowPrivMB.ToString("F0", c)).Append(" MB (")
              .Append(Signed(nowPrivMB - prevPrivMB)).Append(" in ")
              .Append(Math.Max(0, secondsSincePrev).ToString("F0", c)).Append(" s, ")
              .Append(Signed(nowPrivMB - lastReportedPrivMB)).Append(" since last breakdown)");
        }

        double heaps = Math.Max(0, b.HeapsMB);
        double other = Math.Max(0, b.PrivateMB - heaps - b.GcCommittedMB);
        sb.Append(" | private ").Append(b.PrivateMB.ToString("F0", c))
          .Append(" = heaps ").Append(b.HeapsMB < 0 ? "?" : heaps.ToString("F0", c))
          .Append(" (n=").Append(b.HeapCount.ToString(c)).Append(')')
          .Append(" + gc ").Append(b.GcCommittedMB.ToString("F0", c))
          .Append(" + other ").Append(other.ToString("F0", c));
        sb.Append(" | mapped ").Append(b.MappedMB.ToString("F0", c))
          .Append(" image ").Append(b.ImageMB.ToString("F0", c));
        sb.Append(" | top private MB: ").Append(FormatRegions(b.TopPrivateRegionsMB));
        sb.Append(" | walk ").Append(b.ElapsedMs.ToString(c)).Append(" ms");
        if (b.Partial) sb.Append(" (partial)");
        sb.Append(" | state: ").Append(string.IsNullOrWhiteSpace(state) ? "(unavailable)" : state);
        return sb.ToString();
    }

    /// <summary>Region sizes rounded to whole MB, biggest first, equal sizes folded: "256x3, 128x5, 64".</summary>
    internal static string FormatRegions(IReadOnlyList<double>? regionsMB)
    {
        if (regionsMB == null || regionsMB.Count == 0) return "(none)";
        var c = CultureInfo.InvariantCulture;
        var groups = regionsMB
            .Select(r => (long)Math.Round(r))
            .GroupBy(r => r)
            .OrderByDescending(g => g.Key)
            .Select(g => g.Count() > 1 ? g.Key.ToString(c) + "x" + g.Count().ToString(c) : g.Key.ToString(c));
        return string.Join(", ", groups);
    }

    private static string Signed(double mb)
    {
        var c = CultureInfo.InvariantCulture;
        return (mb >= 0 ? "+" : "") + mb.ToString("F0", c);
    }
}
