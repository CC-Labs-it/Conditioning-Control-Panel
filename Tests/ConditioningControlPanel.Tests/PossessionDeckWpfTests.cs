using System;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Scenes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The scene half of the deck rules (the scenes are WPF). The deck and the director moved to
/// CCP.Core with their tests (Tests/CCP.Core.Tests/PossessionDeckTests.cs, PossessionDirectorTests.cs).</summary>
public class PossessionDeckWpfTests
{
    // ---------------------------------------------------------------------------------------------
    //  Scenes - "a scene counts as its beat count" (POSSESSION.md)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void EveryScene_FitsUnderTheCapAtItsOwnMinRung()
    {
        // A scene worth more slots than its MinRung allows can never be elected at all, which is the
        // failure mode of "fixing" an over-claiming scene by raising its Beats.
        foreach (var scene in PossessionSceneCatalog.CreateAll())
        {
            Assert.True(scene.Beats >= 1, $"{scene.Id} declares {scene.Beats} beats");
            Assert.True(PossessionDeck.FitsConcurrency(0, scene.Beats, scene.MinRung),
                $"{scene.Id} is worth {scene.Beats} slots but MaxLive at {scene.MinRung} is "
                + $"{PossessionDeck.MaxLive(scene.MinRung)} - it could never run");
        }
    }

    [Fact]
    public void RailSweep_NeverClaimsMoreDoorsThanItDeclaresBeats()
    {
        var sweep = new RailSweepScene();
        Assert.True(RailSweepScene.MaxDoors <= sweep.Beats,
            $"scene_rail_sweep claims up to {RailSweepScene.MaxDoors} doors but is booked as {sweep.Beats} slots");
        Assert.True(RailSweepScene.MaxDoors >= 2);   // fewer than two doors is not a sweep
    }
}
