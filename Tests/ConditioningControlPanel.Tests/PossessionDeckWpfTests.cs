using System;
using ConditioningControlPanel.Services.Possession;
using ConditioningControlPanel.Services.Possession.Scenes;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The scene and director halves of the deck rules: both are WPF, the deck (and its own tests)
/// moved to CCP.Core (Tests/CCP.Core.Tests/PossessionDeckTests.cs).</summary>
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

    // ---------------------------------------------------------------------------------------------
    //  The director's own invariants (the parts that need no dispatcher)
    // ---------------------------------------------------------------------------------------------

    [Fact]
    public void RestartReset_ReArmsTheLadder_ButSpendsTheRungItJustSet()
    {
        // No host, no dispatcher: this is the bookkeeping half of the restart handler.
        using var lockdown = new ConditioningControlPanel.Services.LockdownService();
        using var director = new PossessionDirector(lockdown, Array.Empty<IPossessionEffect>());

        // The clock rewinds to its full duration, so the ladder drops back to Settle with it.
        director.ApplyRestartReset(PossessionRung.Settle);

        Assert.Equal(PossessionRung.Settle, director.CurrentRung);
        // Settle is already spent: the CountdownTick from the same rewind sees the new elapsed
        // fraction, changes rung to Settle and would otherwise bark it a second time.
        Assert.Contains(PossessionRung.Settle, director.AnnouncedRungs);
        Assert.Single(director.AnnouncedRungs);

        // Idempotent - it does not matter which of the two events arrives first, or how often.
        director.ApplyRestartReset(PossessionRung.Settle);
        Assert.Single(director.AnnouncedRungs);

        // ...and a rewind that lands further up the ladder re-arms every rung below it.
        director.ApplyRestartReset(PossessionRung.Melt);
        Assert.Equal(PossessionRung.Melt, director.CurrentRung);
        Assert.Single(director.AnnouncedRungs);
        Assert.Contains(PossessionRung.Melt, director.AnnouncedRungs);
        Assert.DoesNotContain(PossessionRung.Settle, director.AnnouncedRungs);
    }

    [Fact]
    public void ShouldFinishReassembly_OnlyWhenNoNewLockdownStartedUnderneathIt()
    {
        // The tail strips every ember outline and forces the rung display back to Settle, so it must
        // only run for the lockdown that started it.
        Assert.True(PossessionDirector.ShouldFinishReassembly(3, 3, haunting: false));
        Assert.False(PossessionDirector.ShouldFinishReassembly(3, 4, haunting: false));  // Activate bumped it
        Assert.False(PossessionDirector.ShouldFinishReassembly(3, 3, haunting: true));   // haunting again
        Assert.False(PossessionDirector.ShouldFinishReassembly(3, 4, haunting: true));
    }
}
