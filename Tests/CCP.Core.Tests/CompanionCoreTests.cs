using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core CompanionCore against WPF 7.1.5 CompanionService: modifier, XP and level loop, drain, switch.</summary>
[Collection(SessionStatics.Name)]
public sealed class CompanionCoreTests : IDisposable
{
    private readonly string _oldLanguage = LocalizationManager.Instance.CurrentLanguage;
    private readonly AppSettings _s = CoreSettings.Current;
    private readonly (int, CompanionBonusType?, double, int, int, bool, bool, string) _old;
    private readonly Dictionary<int, CompanionProgress> _oldProgress;

    public CompanionCoreTests()
    {
        LocalizationManager.Instance.Initialize("en");
        _old = (_s.ActiveCompanionId, _s.CompanionPerk, _s.PlayerXP, _s.PlayerLevel, _s.HighestLevelEver, _s.StrictLockEnabled, _s.OfflineMode, _s.OfflineUsername);
        (_oldProgress, _s.CompanionProgressData) = (_s.CompanionProgressData, new());
    }

    public void Dispose()
    {
        (_s.ActiveCompanionId, _s.CompanionPerk, _s.PlayerXP, _s.PlayerLevel, _s.HighestLevelEver, _s.StrictLockEnabled, _s.OfflineMode, _s.OfflineUsername) = _old;
        _s.CompanionProgressData = _oldProgress;
        LocalizationManager.Instance.Initialize(_oldLanguage);
    }

    [Fact]
    public void AwardAndDrainEventsMatchWpfPayloadAndObserveTheUpdatedSettings()
    {
        (_s.CompanionPerk, _s.StrictLockEnabled) = (CompanionBonusType.StrictModeBonus, false);
        var awards = new List<(CompanionId Id, double Amount, double Modifier)>();
        var drained = new List<double>();
        Action<CompanionId, double, double> award = (id, amount, modifier) =>
        {
            Assert.Equal(amount, _s.ActiveCompanionProgress.CurrentXP);
            awards.Add((id, amount, modifier));
        };
        Action<double> drain = amount => { Assert.Equal(0, _s.PlayerXP); drained.Add(amount); };
        CompanionCore.XPAwarded += award;
        CompanionCore.XPDrained += drain;
        try
        {
            CompanionCore.AddCompanionXP(12, "test");
            Assert.Equal((CompanionCore.ActiveCompanion, 6.0, 0.5), Assert.Single(awards));
            _s.ActiveCompanionProgress.Level = CompanionProgress.MaxLevel;
            CompanionCore.AddCompanionXP(12, "test");
            Assert.Single(awards);
            (_s.CompanionPerk, _s.PlayerXP) = (CompanionBonusType.XPDrain, 2);
            Assert.True(CompanionCore.DrainTick(DateTime.UtcNow));
            Assert.Equal(3.0, Assert.Single(drained)); // WPF announces its tick rate, even at the floor.
            Assert.False(CompanionCore.DrainTick(DateTime.UtcNow));
            Assert.Single(drained);
        }
        finally { CompanionCore.XPAwarded -= award; CompanionCore.XPDrained -= drain; }
    }

    [Theory]
    [InlineData(9, null, null)]
    [InlineData(10, "Companion level up", "Circe reached Level 10.")]
    [InlineData(CompanionProgress.MaxLevel, "Max level", "Circe has reached maximum level.")]
    public void MilestoneRuleUsesTheSuppliedModAwareName(int level, string? title, string? body)
    {
        var notice = CompanionCore.MilestoneNotification("Circe", level);
        Assert.Equal(title, notice?.Title);
        Assert.Equal(body, notice?.Body);
    }

    [Fact]
    public void TheModifierIsWpfsPerkTable()
    {
        Assert.Equal(1.5, CompanionCore.CalculateXPModifier(CompanionBonusType.PinkFilterBonus, false, false, false, false, 50));
        Assert.Equal(1.5, CompanionCore.CalculateXPModifier(CompanionBonusType.AutonomyBonus, true, false, false, false, 0));
        Assert.Equal(0.5, CompanionCore.CalculateXPModifier(CompanionBonusType.StrictModeBonus, false, false, true, true, 0));
        Assert.Equal(1.0, CompanionCore.CalculateXPModifier(CompanionBonusType.StrictModeBonus, false, true, false, true, 0));
        Assert.Equal(2.0, CompanionCore.CalculateXPModifier(CompanionBonusType.StrictModeBonus, false, true, true, true, 0));
        Assert.Equal(1.0, CompanionCore.CalculateXPModifier(CompanionBonusType.XPDrain, true, true, true, true, 50));
    }

    [Fact]
    public void TheXpBankFeedsTheActiveCompanionItsBaseAmountThroughTheLevelLoop()
    {
        (_s.CompanionPerk, _s.StrictLockEnabled, _s.OfflineMode, _s.OfflineUsername) = (CompanionBonusType.StrictModeBonus, false, true, "t");
        var levels = new List<int>();
        Action<CompanionId, int> onLevel = (_, l) => levels.Add(l);
        CompanionCore.LevelUp += onLevel;
        try
        {
            var need = _s.ActiveCompanionProgress.XPForNextLevel;
            ProgressionBank.Add(need * 2 + 2, "Video");                 // Trainer without strict: 0.5x
            Assert.Equal(new[] { 2 }, levels);
            Assert.Equal(1.0, _s.ActiveCompanionProgress.CurrentXP, 3);
            _s.ActiveCompanionProgress.Level = CompanionProgress.MaxLevel;
            CompanionCore.AddCompanionXP(1000, "Video");
            Assert.Equal(1.0, _s.ActiveCompanionProgress.CurrentXP, 3);   // nothing at max level
        }
        finally { CompanionCore.LevelUp -= onLevel; }
    }

    [Fact]
    public void BrainParasiteDrainsThreeXpPerTickNeverBelowZero()
    {
        (_s.CompanionPerk, _s.PlayerXP) = (CompanionBonusType.PinkFilterBonus, 10);
        Assert.False(CompanionCore.DrainTick(DateTime.UtcNow));
        _s.CompanionPerk = CompanionBonusType.XPDrain;
        Assert.True(CompanionCore.DrainTick(DateTime.UtcNow));
        Assert.Equal(7, _s.PlayerXP);
        _s.PlayerXP = 2;
        Assert.True(CompanionCore.DrainTick(DateTime.UtcNow));
        Assert.Equal(0, _s.PlayerXP);
        Assert.False(CompanionCore.DrainTick(DateTime.UtcNow));
    }

    [Fact]
    public void SwitchingStampsFirstActivationAndAnnouncesOnlyARealChange()
    {
        _s.ActiveCompanionId = (int)CompanionId.OGBambiSprite;
        var switched = new List<CompanionId>();
        Action<CompanionId> on = switched.Add;
        CompanionCore.Switched += on;
        try
        {
            Assert.True(CompanionCore.SwitchCompanion(CompanionId.OGBambiSprite));
            Assert.True(CompanionCore.SwitchCompanion(CompanionId.BrainParasite));
            Assert.Equal(new[] { CompanionId.BrainParasite }, switched);
            Assert.Equal((int)CompanionId.BrainParasite, _s.ActiveCompanionId);
            Assert.NotEqual(DateTime.MinValue, _s.CompanionProgressData[(int)CompanionId.BrainParasite].FirstActivated);
        }
        finally { CompanionCore.Switched -= on; }
    }
}
