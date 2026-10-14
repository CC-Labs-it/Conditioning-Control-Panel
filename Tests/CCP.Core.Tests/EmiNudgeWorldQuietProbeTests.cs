using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The seam EmiNudgeWorld gained when it moved to Core: whether the desktop is quiet enough for a
/// tutorial line is the head's call (WPF <c>EmiDeskService.NudgeQuiet</c>). Unset, or a probe that
/// throws, reads as "not quiet" - a nudge must never be the thing that nags or throws.
/// </summary>
[Collection("EmiNudgeWorldProbe")]
public class EmiNudgeWorldQuietProbeTests
{
    private static bool Quiet(Func<bool>? probe)
    {
        var old = EmiNudgeWorld.QuietProbe;
        try
        {
            EmiNudgeWorld.QuietProbe = probe;
            return new EmiNudgeWorld().Quiet;
        }
        finally { EmiNudgeWorld.QuietProbe = old; }
    }

    [Fact]
    public void TheHeadsAnswerIsTheAnswer()
    {
        Assert.True(Quiet(() => true));
        Assert.False(Quiet(() => false));
    }

    [Fact]
    public void UnsetOrThrowingProbeIsNotQuiet()
    {
        Assert.False(Quiet(null));
        Assert.False(Quiet(() => throw new InvalidOperationException("boom")));
    }

    [Fact]
    public void NotQuietMeansTheMachineStaysSilent()
    {
        var old = EmiNudgeWorld.QuietProbe;
        try
        {
            EmiNudgeWorld.QuietProbe = () => false;
            var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var now = t0;
            var machine = new EmiNudgeMachine(() => now);
            machine.NoteSummon(5);
            now = t0.AddHours(1);
            Assert.Null(machine.Tick(new EmiNudgeWorld()));
            Assert.Null(machine.OnRingOpened(new EmiNudgeWorld()));
        }
        finally { EmiNudgeWorld.QuietProbe = old; }
    }
}
