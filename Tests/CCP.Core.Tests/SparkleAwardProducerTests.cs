using System;
using System.Collections.Generic;
using System.IO;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using Xunit;

namespace CCP.Core.Tests;

public sealed class SparkleAwardProducerTests : IDisposable
{
    private readonly Func<SettingsService?>? _provider = CoreSettings.ServiceProvider;
    private readonly AppSettings _settings;
    private readonly List<SparklePointAward> _awards = new();
    private readonly List<int> _observedBalances = new();

    public SparkleAwardProducerTests()
    {
        var service = new SettingsService();
        service.Reset(); // Each test starts from fresh settings in the assembly's CCP_USERDATA_DIR sandbox.
        CoreSettings.ServiceProvider = () => service;
        _settings = service.Current;
        _settings.SkillPoints = 40;
        _settings.PlayerLevel = 1;
        _settings.PlayerXP = 0;
        _settings.SeasonPeakLevel = 1;
        _settings.OfflineMode = true;
        _settings.OfflineUsername = "sparkle-test";
        _settings.DescentEpoch = 0;
        _settings.UnlockedSkills.Clear();
        SparklePointRewards.Awarded += OnAward;
    }

    private void OnAward(object? sender, SparklePointAward award)
    {
        _observedBalances.Add(_settings.SkillPoints);
        _awards.Add(award);
    }

    public void Dispose()
    {
        SparklePointRewards.Awarded -= OnAward;
        CoreSettings.ServiceProvider = _provider;
    }

    [Fact]
    public void RealLevelLoopCreditsOnePointForEachLevel()
    {
        var xp = XpCurve.GetXPForLevel(1, 0) + XpCurve.GetXPForLevel(2, 0);
        ProgressionBank.Add(xp, "Bubble");

        Assert.Equal(3, _settings.PlayerLevel);
        Assert.Equal(42, _settings.SkillPoints);
        Assert.Equal(3, _settings.SeasonPeakLevel);
        Assert.Equal(new[] {
            new SparklePointAward(1, 41, SparklePointSource.LevelUp),
            new SparklePointAward(1, 42, SparklePointSource.LevelUp)
        }, _awards);
        Assert.Equal(new[] { 41, 42 }, _observedBalances);
    }

    [Theory]
    [InlineData(99, 1, 1)]
    [InlineData(199, 1, 1)]
    [InlineData(98, 1, 0)]
    [InlineData(99, 202, 3)]
    [InlineData(100, 99, 0)]
    [InlineData(199, 0, 0)]
    [InlineData(199, -1, 0)]
    public void BubblePopsCreditEveryHundredBoundaryIncludingBatches(int before, int count, int points)
    {
        var path = Path.Combine(RoadmapTestProfile.DirectoryPath, Guid.NewGuid() + ".json");
        var engine = new AchievementEngine(new AchievementStore(path));
        engine.Progress.TotalBubblesPopped = before;
        var popups = new List<int>();
        engine.BubbleMilestone += popups.Add;

        if (count == 1) engine.TrackBubblePopped();
        else engine.TrackBubblesPopped(count);

        Assert.Equal(before + Math.Max(0, count), engine.Progress.TotalBubblesPopped);
        Assert.Equal(40 + points, _settings.SkillPoints);
        if (points == 0) { Assert.Empty(_awards); Assert.Empty(popups); }
        else
        {
            Assert.Equal((before + count) / 100 * 100, Assert.Single(popups));   // WPF: one popup, highest hundred
            Assert.Equal(new SparklePointAward(points, 40 + points, SparklePointSource.BubbleMilestone), Assert.Single(_awards));
            Assert.Equal(40 + points, Assert.Single(_observedBalances));
            Assert.Equal(before + count, new AchievementStore(path).Load().TotalBubblesPopped);
        }
    }

    [Fact]
    public void ConsecutiveSinglePopsAwardAt100And200Only()
    {
        var engine = new AchievementEngine(new AchievementStore(Path.Combine(RoadmapTestProfile.DirectoryPath, Guid.NewGuid() + ".json")));
        for (var pop = 1; pop <= 201; pop++)
        {
            engine.TrackBubblePopped();
            Assert.Equal(pop / 100, _awards.Count);
            Assert.Equal(40 + pop / 100, _settings.SkillPoints);
        }
    }

    [Fact]
    public void ThousandthBubbleUnlocksTheExistingAchievementAndCreditsOnce()
    {
        var path = Path.Combine(RoadmapTestProfile.DirectoryPath, Guid.NewGuid() + ".json");
        var engine = new AchievementEngine(new AchievementStore(path));
        engine.Progress.TotalBubblesPopped = 999;
        engine.TrackBubblePopped();

        Assert.True(engine.Progress.IsUnlocked("pop_the_thought"));
        Assert.True(new AchievementStore(path).Load().IsUnlocked("pop_the_thought"));
        Assert.Equal(new SparklePointAward(1, 41, SparklePointSource.BubbleMilestone), Assert.Single(_awards));
    }

    [Fact]
    public void BubbleProgressFlushesEveryTenPopsAndAtBatchEnd()
    {
        var path = Path.Combine(RoadmapTestProfile.DirectoryPath, Guid.NewGuid() + ".json");
        var engine = new AchievementEngine(new AchievementStore(path));
        for (var pop = 0; pop < 9; pop++) engine.TrackBubblePopped();
        Assert.False(File.Exists(path));
        engine.TrackBubblePopped();
        Assert.Equal(10, new AchievementStore(path).Load().TotalBubblesPopped);
        engine.TrackBubblesPopped(1);
        Assert.Equal(11, new AchievementStore(path).Load().TotalBubblesPopped);
        Assert.Empty(_awards);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CappedCreditsStayQuietAndPartialCreditsPublishOnlyWhatFits(bool levelUp)
    {
        _settings.SkillPoints = SparklePoints.Cap - 1;
        void Credit()
        {
            if (levelUp) SparklePointRewards.CreditLevelUp(4, 3);
            else SparklePointRewards.CreditBubbleMilestones(99, 301);
        }
        Credit();
        Credit();

        Assert.Equal(SparklePoints.Cap, _settings.SkillPoints);
        Assert.Equal(new SparklePointAward(1, SparklePoints.Cap,
            levelUp ? SparklePointSource.LevelUp : SparklePointSource.BubbleMilestone), Assert.Single(_awards));
    }
}
