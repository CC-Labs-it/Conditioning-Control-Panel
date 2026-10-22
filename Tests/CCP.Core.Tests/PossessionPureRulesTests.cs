using System;
using ConditioningControlPanel;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Effects;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The pure Possession rules that moved from the WPF head into Core: the off-limits name rule
/// (moved from ConditioningControlPanel.Tests/PossessionOffLimitsTests.cs) and the active-mod read the
/// toast and rewrite pools now take through <see cref="CoreMods"/> instead of App.Mods.
/// </summary>
public class PossessionPureRulesTests
{
    [Theory]
    [InlineData("LockdownCardBorder")]
    [InlineData("BtnEmergencyExit")]
    [InlineData("TxtSecretExitPhrase")]
    [InlineData("lockdowngate")]            // case does not matter
    [InlineData("PanelEMERGENCY")]
    public void ReservedNames_are_off_limits(string name)
        => Assert.True(PossessionOffLimits.IsReservedName(name));

    [Theory]
    [InlineData("BtnStart")]
    [InlineData("CardFlashSettings")]
    [InlineData("")]
    [InlineData(null)]
    public void Ordinary_names_are_not(string? name)
        => Assert.False(PossessionOffLimits.IsReservedName(name));

    [Fact]
    public void Pools_follow_the_active_mod_from_CoreMods()
    {
        var prev = CoreMods.ActiveModIdProvider;
        try
        {
            CoreMods.ActiveModIdProvider = () => ToastLines.SissyMod;
            Assert.Equal(ToastLines.SissyMod, RewritePools.ActiveModId);
            Assert.Contains(ToastLines.Pick(new Random(1)), ToastLines.PoolFor(ToastLines.SissyMod));

            CoreMods.ActiveModIdProvider = null;
            Assert.Equal(ConditioningControlPanel.Models.BuiltInMods.CCPDefaultId, RewritePools.ActiveModId);
            Assert.Contains(ToastLines.Pick(new Random(1)), ToastLines.PoolFor(null));
        }
        finally { CoreMods.ActiveModIdProvider = prev; }
    }
}
