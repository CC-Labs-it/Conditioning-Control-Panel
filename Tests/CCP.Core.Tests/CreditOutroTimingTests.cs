using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel.Services.Video;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The "via ..." end card of an online clip lays over the clip while it ends, the cut's end on a
/// capped clip, instead of after it over a frozen frame (owner, 2026-10-19).
/// </summary>
public class CreditOutroTimingTests
{
    private const int Card = 2500;

    [Fact]
    public void The_card_starts_one_card_before_the_file_ends()
    {
        Assert.False(CreditOutroTiming.ShouldStart(20_000 - Card - 1, 20_000 - Card - 1, 20_000, 0, Card));
        Assert.True(CreditOutroTiming.ShouldStart(20_000 - Card, 20_000 - Card, 20_000, 0, Card));
        Assert.True(CreditOutroTiming.ShouldStart(19_900, 19_900, 20_000, 0, Card));
    }

    [Fact]
    public void On_a_cut_clip_the_end_is_the_cut()
    {
        // 10 s limit on a 60 s file with a random start at 30 s: played time is what counts.
        Assert.False(CreditOutroTiming.ShouldStart(30_000 + 7_000, 7_000, 60_000, 10_000, Card));
        Assert.True(CreditOutroTiming.ShouldStart(30_000 + 7_500, 7_500, 60_000, 10_000, Card));
        // The file ending first still wins.
        Assert.True(CreditOutroTiming.ShouldStart(58_000, 4_000, 60_000, 10_000, Card));
    }

    [Fact]
    public void A_short_clip_keeps_its_first_half_clear_and_an_unknown_end_keeps_the_old_card()
    {
        // 3 s clip: the card waits for the half.
        Assert.False(CreditOutroTiming.ShouldStart(1_000, 1_000, 3_000, 0, Card));
        Assert.True(CreditOutroTiming.ShouldStart(1_500, 1_500, 3_000, 0, Card));
        Assert.False(CreditOutroTiming.ShouldStart(500_000, 500_000, 0, 0, Card));
        Assert.False(CreditOutroTiming.ShouldStart(19_900, 19_900, 20_000, 0, 0));
    }

    [Fact]
    public void Both_browser_players_tell_the_page_once()
    {
        var instances = Read("Services", "Video", "VideoService.FloatingInstances.cs");
        Assert.Contains("CreditOutroTiming.ShouldStart(", instances);
        Assert.Contains("c.OutroSent = true;", instances);
        var full = Read("Services", "Video", "VideoService.Browser.cs");
        Assert.Contains("Video.CreditOutroTiming.ShouldStart(", full);
        Assert.Contains("_browserOutroSent = true;", full);
        Assert.Contains("Browser.ShowCreditOutro()", full);
        // A paused clip's forced time post never brings the card up.
        Assert.Contains("!_browserOutroSent && paused != true &&", full);
        Assert.Contains("!c.OutroSent && (bool?)o[\"paused\"] != true &&", instances);
        // A press on the card of a clip the limit cuts still opens, before the cut's Cleanup.
        var core = Read("Services", "Video", "VideoService.cs");
        var open = core.IndexOf("OpenQueuedCreditLink();", StringComparison.Ordinal);
        var cleanup = core.IndexOf("Cleanup();", open, StringComparison.Ordinal);
        Assert.True(open > 0 && cleanup > open && cleanup - open < 200);
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
