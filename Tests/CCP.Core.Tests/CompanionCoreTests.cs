using System;
using System.Collections.Generic;
using ConditioningControlPanel;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Companion;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>Core CompanionCore against WPF 7.1.5 CompanionService: modifier, XP and level loop, drain, switch.</summary>
[Collection(SessionStatics.Name)]
public sealed class CompanionCoreTests : IDisposable
{
    private readonly AppSettings _s = CoreSettings.Current;
    private readonly (int, CompanionBonusType?, double, int, int, bool, bool, string) _old;
    private readonly Dictionary<int, CompanionProgress> _oldProgress;

    public CompanionCoreTests()
    {
        _old = (_s.ActiveCompanionId, _s.CompanionPerk, _s.PlayerXP, _s.PlayerLevel, _s.HighestLevelEver, _s.StrictLockEnabled, _s.OfflineMode, _s.OfflineUsername);
        (_oldProgress, _s.CompanionProgressData) = (_s.CompanionProgressData, new());
    }

    public void Dispose()
    {
        (_s.ActiveCompanionId, _s.CompanionPerk, _s.PlayerXP, _s.PlayerLevel, _s.HighestLevelEver, _s.StrictLockEnabled, _s.OfflineMode, _s.OfflineUsername) = _old;
        _s.CompanionProgressData = _oldProgress;
    }
    [Fact]
    public void PromptStoreFailureStillCompletesTheSwitch()
    {
        var folder = System.IO.Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);
        var backup = folder + Guid.NewGuid().ToString("N"); var hadFolder = System.IO.Directory.Exists(folder);
        var oldAssigned = _s.GetCompanionPromptId(1); CompanionId? announced = null;
        Action<CompanionId> on = id => announced = id;
        if (hadFolder) System.IO.Directory.Move(folder, backup);
        CompanionCore.Switched += on;
        try
        {
            System.IO.Directory.CreateDirectory(CorePaths.UserData);
            System.IO.File.WriteAllText(folder, "block directory creation");
            _s.ActiveCompanionId = 0;
            _s.SetCompanionPromptId(1, "assigned");
            Assert.False(InstalledCommunityPrompts.ApplyAssigned((CompanionId)1));
            Assert.True(CompanionCore.SwitchCompanion((CompanionId)1)); Assert.Equal(1, _s.ActiveCompanionId);
            Assert.Equal((CompanionId)1, announced);
        }
        finally
        {
            CompanionCore.Switched -= on;
            System.IO.File.Delete(folder);
            if (hadFolder) System.IO.Directory.Move(backup, folder);
            _s.SetCompanionPromptId(1, oldAssigned);
        }
    }

    [Fact]
    public void SwitchAppliesInstalledAssignmentBeforeAnnouncingTheCompanion()
    {
        var folder = System.IO.Path.Combine(CorePaths.UserData, InstalledCommunityPrompts.FolderName);
        System.IO.Directory.CreateDirectory(folder);
        var id = "roster-test-" + Guid.NewGuid().ToString("N");
        var path = System.IO.Path.Combine(folder, id + ".json");
        var oldPrompt = _s.CompanionPrompt;
        var oldActive = _s.ActiveCommunityPromptId;
        var oldAssigned = _s.GetCompanionPromptId(1);
        var oldMode = _s.SlutModeEnabled;
        try
        {
            _s.SlutModeEnabled = false;
            _s.ActiveCompanionId = 0;
            _s.SetCompanionPromptId(1, id);
            System.IO.File.WriteAllText(path, "{\"Name\":\"Test persona\",\"PromptSettings\":{\"Personality\":\"a calm voice\"}}");
            string? seen = null;
            Action<CompanionId> on = _ => seen = _s.ActiveCommunityPromptId;
            CompanionCore.Switched += on;
            try { CompanionCore.SwitchCompanion((CompanionId)1); }
            finally { CompanionCore.Switched -= on; }
            Assert.Equal(id, seen);
            Assert.Equal("a calm voice", _s.CompanionPrompt.Personality);
            Assert.True(_s.CompanionPrompt.UseCustomPrompt);
            CompanionCore.SwitchCompanion((CompanionId)0);
            Assert.Equal(id, _s.ActiveCommunityPromptId); // unassigned leaves the current voice alone
        }
        finally
        {
            System.IO.File.Delete(path);
            _s.SetCompanionPromptId(1, oldAssigned);
            _s.CompanionPrompt = oldPrompt;
            _s.ActiveCommunityPromptId = oldActive;
            _s.SlutModeEnabled = oldMode;
        }
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
