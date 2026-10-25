using System;
using System.IO;
using System.Linq;
using ConditioningControlPanel;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class LeaderboardRankSnapshotTests : IDisposable
{
    private readonly Func<DateTime> _oldClock = LeaderboardRankSnapshotService.UtcNow;
    private readonly string _path = Path.Combine(CorePaths.UserData, "leaderboard_ranks.json");
    private DateTime _now = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

    public LeaderboardRankSnapshotTests()
    {
        Assert.StartsWith(Path.GetTempPath(), _path);
        Directory.CreateDirectory(CorePaths.UserData);
        File.Delete(_path);
        LeaderboardRankSnapshotService.ResetForTests();
        LeaderboardRankSnapshotService.UtcNow = () => _now;
    }

    private static LeaderboardEntryData Row(string? id, int rank) => new() { UnifiedId = id, Rank = rank };

    [Fact]
    public void TwelveHourBaseline_SeparatesModes_AndReadsBeforeRecording()
    {
        LeaderboardRankSnapshotService.RecordIfDue(" All-Time ", new[] { Row("a", 4) });
        LeaderboardRankSnapshotService.RecordIfDue("monthly", new[] { Row("a", 9) });
        Assert.Equal(4, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "a"));
        Assert.Equal(9, LeaderboardRankSnapshotService.GetPreviousRank("monthly", "a"));
        var row = Row("a", 2);
        row.ApplyRankDelta(LeaderboardRankSnapshotService.GetPreviousRank("all-time", "a"), true);
        _now = _now.AddHours(11.99);
        LeaderboardRankSnapshotService.RecordIfDue("all-time", new[] { row });
        Assert.Equal(4, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "a"));
        _now = _now.AddMinutes(1);
        LeaderboardRankSnapshotService.RecordIfDue("all-time", new[] { row });
        Assert.Equal("up", row.DeltaState); // eager delta survives the new snapshot
        Assert.Equal("▲2", row.DeltaText);
        LeaderboardRankSnapshotService.ResetForTests();
        Assert.Equal(2, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "a")); // actual disk reload
        Assert.Equal(9, LeaderboardRankSnapshotService.GetPreviousRank("monthly", "a"));
        _now = _now.AddHours(-1); // future baseline must not freeze deltas
        LeaderboardRankSnapshotService.RecordIfDue("all-time", new[] { Row("a", 3) });
        Assert.Equal(3, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "a"));
    }

    [Fact]
    public void BestFiveHundredValidIds_EmptyFetchAndCorruptionAreSafe()
    {
        File.WriteAllText(_path, "{ corrupt");
        LeaderboardRankSnapshotService.ResetForTests();
        Assert.Null(LeaderboardRankSnapshotService.GetPreviousRank("all-time", "a"));
        var rows = Enumerable.Range(1, 510).Reverse().Select(i => Row("u" + i, i)).ToList();
        rows.AddRange(new[] { Row(null, 1), Row("bad", 0), Row("u1", 99) });
        LeaderboardRankSnapshotService.RecordIfDue("all-time", rows);
        Assert.Equal(1, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "u1"));
        Assert.Equal(500, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "u500"));
        Assert.Null(LeaderboardRankSnapshotService.GetPreviousRank("all-time", "u501"));
        Assert.Null(LeaderboardRankSnapshotService.GetPreviousRank("all-time", "bad"));
        _now = _now.AddHours(13);
        LeaderboardRankSnapshotService.RecordIfDue("all-time", Array.Empty<LeaderboardEntryData>());
        LeaderboardRankSnapshotService.ResetForTests();
        Assert.Equal(1, LeaderboardRankSnapshotService.GetPreviousRank("all-time", "u1"));
        Assert.False(File.Exists(_path + ".tmp"));
    }

    public void Dispose()
    {
        LeaderboardRankSnapshotService.UtcNow = _oldClock;
        LeaderboardRankSnapshotService.ResetForTests();
        File.Delete(_path);
    }
}
