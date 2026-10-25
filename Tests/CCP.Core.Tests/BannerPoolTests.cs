using System;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Banner;
using Xunit;

namespace CCP.Core.Tests;

public sealed class BannerPoolTests
{
    [Fact]
    public void HeaderDrawExcludesReadsButNamedReadsStayAvailable()
    {
        for (int roll = 0; roll < 75; roll++)
        {
            var order = BannerPoolService.BuildDrawOrder(null, "reads", max => roll % max);
            Assert.Equal(2, order.Count);
            Assert.Contains("taglines", order);
            Assert.Contains("trivia", order);
            Assert.DoesNotContain("reads", order);
            Assert.Equal(roll < 40 ? "taglines" : "trivia", order[0]);
        }
        Assert.Equal(new[] { "reads" }, BannerPoolService.BuildDrawOrder("reads", "reads", _ => 0));
    }

    [Fact]
    public void NumbersMustBeKnownAndNonzeroAndEveryConditionBoundMustHold()
    {
        var s = new AppSettings { PlayerLevel = 5, TotalSessions = 0 };
        Assert.Equal("Level 5", BannerPoolService.Render("Level {player_level}", s));
        Assert.Null(BannerPoolService.Render("Sessions {total_sessions}", s));
        Assert.Null(BannerPoolService.Render("Unknown {not_a_ledger_key}", s));
        Assert.Equal("Literal {open", BannerPoolService.Render("Literal {open", s));
        Assert.True(BannerPoolService.Passes(new BannerPoolCond { Key = "player_level", Gte = 5, Lte = 5, Eq = 5 }, s));
        Assert.False(BannerPoolService.Passes(new BannerPoolCond { Key = "player_level", Gte = 6 }, s));
        Assert.False(BannerPoolService.Passes(new BannerPoolCond { Key = "player_level", Lte = 4 }, s));
        Assert.False(BannerPoolService.Passes(new BannerPoolCond { Key = "player_level", Eq = 4 }, s));
        Assert.False(BannerPoolService.Passes(new BannerPoolCond { Key = "unknown" }, s));
    }

    [Fact]
    public void ShippedPoolPrefersUnseenAndResetsOnlyItsExhaustedBucket()
    {
        var s = CoreSettings.Current;
        var enabled = s.BannerPoolEnabled;
        var before = s.BannerSeenIds.ToList();
        try
        {
            s.BannerPoolEnabled = true;
            s.BannerSeenIds.Clear();
            s.RecordBannerSeen("tv_keep");
            var pool = new BannerPoolService();
            Assert.False(string.IsNullOrWhiteSpace(pool.NextLine("taglines")));
            var first = s.BannerSeenIds.Single(id => id.StartsWith("tg_", StringComparison.Ordinal));
            Assert.False(string.IsNullOrWhiteSpace(pool.NextLine("taglines")));
            Assert.Equal(2, s.BannerSeenIds.Count(id => id.StartsWith("tg_", StringComparison.Ordinal)));
            Assert.Contains(first, s.BannerSeenIds);
            // Mark every authored tagline seen. Exhaustion starts that bucket over only.
            for (int i = 1; i <= 100; i++) s.RecordBannerSeen($"tg_{i:000}");
            Assert.NotNull(pool.NextLine("taglines"));
            Assert.Single(s.BannerSeenIds.Where(id => id.StartsWith("tg_", StringComparison.Ordinal)));
            Assert.Contains("tv_keep", s.BannerSeenIds);
            s.BannerPoolEnabled = false;
            var seen = s.BannerSeenIds.ToArray();
            Assert.Null(pool.NextLine());
            Assert.Equal(seen, s.BannerSeenIds);
        }
        finally
        {
            s.BannerPoolEnabled = enabled;
            s.BannerSeenIds = before;
        }
    }
}
