using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Prizes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>Shares the static event with the reward tests, so they never run side by side.</summary>
[Collection(nameof(SparklePointRewardsCollection))]
public sealed class SparklesLedgerTests
{
    [Theory]
    [InlineData(10, 13, 3, 13)]
    [InlineData(50, 12, -38, 12)]
    [InlineData(0, -5, 0, 0)]                 // clamped to the same value: no change
    [InlineData(99_998, 100_002, 1, 99_999)]
    public void DescribeIsTheClampedSignedDifference(int before, int after, int amount, int balance)
    {
        var change = SparklesLedger.Describe(before, after, SparkleSource.Synced);
        if (amount == 0) { Assert.Null(change); return; }
        Assert.Equal(new SparkleChange(amount, SparklePoints.Clamp(before), balance, SparkleSource.Synced), change);
    }

    [Fact]
    public void WriteSetsTheBalanceAndRaisesOneSignedChange()
    {
        var seen = new List<(object? Sender, SparkleChange Change)>();
        EventHandler<SparkleChange> listener = (s, c) => seen.Add((s, c));
        var settings = new AppSettings { SkillPoints = 40 };
        SparklesLedger.Changed += listener;
        try
        {
            Assert.True(SparklesLedger.Write(settings, 45, SparkleSource.BackRoom));
            Assert.True(SparklesLedger.Write(settings, 20, SparkleSource.CounterBuy));
            Assert.False(SparklesLedger.Write(settings, 20, SparkleSource.Synced));
        }
        finally { SparklesLedger.Changed -= listener; }

        Assert.Equal(20, settings.SkillPoints);
        Assert.Equal(2, seen.Count);
        Assert.Same(settings, seen[0].Sender);
        Assert.Equal(new SparkleChange(5, 40, 45, SparkleSource.BackRoom), seen[0].Change);
        Assert.True(seen[0].Change.IsGain);
        Assert.Equal(new SparkleChange(-25, 45, 20, SparkleSource.CounterBuy), seen[1].Change);
        Assert.False(seen[1].Change.IsGain);
    }

    [Fact]
    public void ThrowingListenerNeitherBlocksTheNextNorUndoesTheWrite()
    {
        SparkleChange? observed = null;
        EventHandler<SparkleChange> broken = (_, _) => throw new InvalidOperationException("test listener");
        EventHandler<SparkleChange> good = (_, c) => observed = c;
        var settings = new AppSettings { SkillPoints = 3 };
        SparklesLedger.Changed += broken;
        SparklesLedger.Changed += good;
        try { Assert.True(SparklesLedger.Write(settings, 4, SparkleSource.LevelUp)); }
        finally { SparklesLedger.Changed -= broken; SparklesLedger.Changed -= good; }
        Assert.Equal(4, settings.SkillPoints);
        Assert.Equal(new SparkleChange(1, 3, 4, SparkleSource.LevelUp), observed);
    }

    /// <summary>
    /// The one door: no shipped file writes the balance except the ledger. The two lines left are
    /// restore copies that carry the live balance across unchanged (nothing moves).
    /// </summary>
    [Fact]
    public void EveryBalanceWriterGoesThroughTheLedger()
    {
        var app = Path.Combine(RepoRoot(), "ConditioningControlPanel");
        var write = new Regex(@"\.SkillPoints\s*(=(?![=>])|\+=|-=|\+\+|--)");
        var allowed = new[] { "restored.SkillPoints = current.SkillPoints;" };
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(app, "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) ||
                file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (Path.GetFileName(file) == "SparklesLedger.cs") continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (line.StartsWith("//") || line.StartsWith("///")) continue;
                if (write.IsMatch(line) && !allowed.Any(line.Contains))
                    offenders.Add(Path.GetRelativePath(app, file) + ":" + (i + 1) + "  " + line);
            }
        }
        Assert.True(offenders.Count == 0, "Write the balance through SparklesLedger.Write:\n" + string.Join("\n", offenders));

        // The known writers, by source: each names the ledger with the right reason.
        void Pins(string rel, params string[] needles)
        {
            var src = File.ReadAllText(Path.Combine(app, rel));
            foreach (var n in needles) Assert.Contains(n, src);
        }
        Pins("Services/Progression/SkillTreeService.cs", "SparkleSource.LevelUp");
        Pins("Services/Progression/AchievementService.cs", "SparkleSource.BubbleMilestone");
        Pins("Services/Sparkles/SparklesClaimService.cs", "SparkleSource.Place");
        Pins("Services/BackRoom/BackRoomHostService.cs", "SparkleSource.BackRoom");
        Pins("Services/Stakes/StakeAppHooks.cs", "SparkleSource.Stake");   // port: the settlement is Core, its books are the head's
        Pins("Services/PrizeCounter/PrizeCounterHostService.cs", "SparkleSource.CounterBuy");
        Pins("App.xaml.cs", "SparkleSource.CounterBuy");
        Pins("Services/Settings/ProfileSyncService.cs", "SparkleSource.Synced", "SparkleSource.SkillTree");
        Pins("../CCP.Core/Services/Account/ProgressionClear.cs", "SparkleSource.Reset");   // port: MainWindow.Login calls ProgressionClear
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "ConditioningControlPanel", "Resources")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
