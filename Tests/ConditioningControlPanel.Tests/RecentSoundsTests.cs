using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Audio;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// ccp-bugs #1537, #1540: a report of a mystery sound has to name it. The ring of recent sounds and
/// its place at the very end of the report's log field.
/// </summary>
public class RecentSoundsTests
{
    private static readonly DateTime T0 = new(2026, 11, 1, 21, 57, 45, 123, DateTimeKind.Local);

    [Fact]
    public void Keeps_the_newest_entries_in_order()
    {
        var ring = new RecentSounds();
        for (int i = 0; i < RecentSounds.Capacity + 5; i++)
            ring.Note("p" + i, $"clip{i}.mp3", false, false, T0.AddMinutes(i));

        var all = ring.Snapshot();
        Assert.Equal(RecentSounds.Capacity, all.Count);
        Assert.Equal("p5", all[0].Player);
        Assert.Equal("p" + (RecentSounds.Capacity + 4), all[^1].Player);
    }

    [Fact]
    public void A_burst_from_one_player_folds_so_it_cannot_push_a_sting_out()
    {
        var ring = new RecentSounds();
        ring.Note("oneshot:launcher-open", "reveal_chime.mp3", false, false, T0);
        for (int i = 0; i < 200; i++)
            ring.Note("oneshot:launcher-hover", $"wood_{i % 8:00}.wav", false, false, T0.AddMilliseconds(100 * (i + 1)));
        ring.Note("oneshot:launcher-launch", "reveal_chime.mp3", false, false, T0.AddSeconds(30));

        var all = ring.Snapshot();
        Assert.Equal(new[] { "oneshot:launcher-open", "oneshot:launcher-hover", "oneshot:launcher-launch" },
            all.Select(e => e.Player).ToArray());
        Assert.Equal(200, all[1].Count);
        Assert.Equal("wood_07.wav", all[1].File);
    }

    [Fact]
    public void The_same_clip_a_while_apart_stays_two_entries()
    {
        var ring = new RecentSounds();
        ring.Note("oneshot:x", "sting.mp3", false, false, T0);
        ring.Note("oneshot:x", "sting.mp3", false, false, T0 + RecentSounds.FoldGap + TimeSpan.FromMilliseconds(1));
        Assert.Equal(2, ring.Snapshot().Count);
    }

    [Fact]
    public void A_change_of_engine_state_does_not_fold()
    {
        var ring = new RecentSounds();
        ring.Note("oneshot:x", "sting.mp3", false, false, T0);
        ring.Note("oneshot:x", "sting.mp3", true, false, T0.AddSeconds(1));
        Assert.Equal(2, ring.Snapshot().Count);
    }

    [Theory]
    [InlineData(@"C:\Users\someone\assets\mindwipe\wand 01.mp3", "wand 01.mp3")]
    [InlineData("/home/someone/clip.wav", "clip.wav")]
    [InlineData("https://cdn.example.com/a/b.mp4?token=secret#t", "b.mp4")]
    [InlineData(null, "?")]
    public void Only_the_file_name_gets_in(string? path, string expected)
    {
        Assert.Equal(expected, RecentSounds.CleanName(path));
    }

    [Fact]
    public void A_very_long_name_is_capped()
    {
        Assert.Equal(RecentSounds.MaxNameChars, RecentSounds.CleanName(new string('a', 500) + ".mp3").Length);
    }

    [Fact]
    public void The_section_reads_time_player_file_and_state()
    {
        var ring = new RecentSounds();
        ring.Note("oneshot:launcher-launch", @"C:\Users\someone\chaos\reveal_chime.mp3", false, false, T0);
        ring.Note("keyword-trigger", "a.mp3", true, true, T0.AddMinutes(1));

        var section = ring.BuildSection();

        Assert.StartsWith(RecentSounds.SectionHeader, section);
        Assert.Contains("21:57:45.123 oneshot:launcher-launch 'reveal_chime.mp3' engine-off", section);
        Assert.Contains("21:58:45.123 keyword-trigger 'a.mp3' engine-on asked", section);
        Assert.DoesNotContain("someone", section);
    }

    [Fact]
    public void An_empty_ring_still_says_so()
    {
        var section = new RecentSounds().BuildSection();
        Assert.Contains(RecentSounds.NoSoundsLine, section);
    }

    [Fact]
    public void The_scrubber_leaves_the_section_alone()
    {
        var ring = new RecentSounds();
        ring.Note("oneshot:launcher-open", "reveal_chime.mp3", false, false, T0);
        var section = ring.BuildSection();

        var (scrubbed, counts) = LogScrubber.Scrub(section);

        Assert.Equal(section, scrubbed);
        Assert.Equal(ScrubberCounts.Empty, counts);
    }

    [Fact]
    public void The_section_survives_the_tail_cut_a_flooded_log_gets()
    {
        var ring = new RecentSounds();
        ring.Note("oneshot:launcher-open", "reveal_chime.mp3", false, false, T0);
        var section = ring.BuildSection();
        var flood = string.Join(Environment.NewLine,
            Enumerable.Repeat("11:48:34.915 DBG [WebcamTrackin] mouth-diag baseline=0.057", 10_000));

        var combined = BugReportService.AppendRecentSounds(flood, section);

        Assert.EndsWith(section, BugReportService.Tail(combined, section.Length + 10));
        Assert.Equal(flood, BugReportService.AppendRecentSounds(flood, ""));
    }

    [Fact]
    public void The_report_appends_the_section_after_everything_else_and_before_the_scrubber()
    {
        var src = File.ReadAllText(Path.Combine(RepoRoot(), "CCP.Core", "Services", "BugReportService.cs"));

        var hang = src.IndexOf("BuildHangReportSection(TryReadNewestHangReport", StringComparison.Ordinal);
        var sounds = src.IndexOf("combined = AppendRecentSounds(combined,", StringComparison.Ordinal);
        var scrub = src.IndexOf("LogScrubber.Scrub(combined)", StringComparison.Ordinal);

        Assert.True(hang > 0 && sounds > hang && scrub > sounds, $"hang={hang} sounds={sounds} scrub={scrub}");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "ConditioningControlPanel.csproj")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
