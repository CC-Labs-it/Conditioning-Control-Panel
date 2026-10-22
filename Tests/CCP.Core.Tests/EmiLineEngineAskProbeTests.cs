using System;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The two seams EmiLineEngine gained when it moved to Core: the head's ask situation
/// (WPF <c>App.EmiDesk.AskSituationOk()</c>) and effect feasibility (WPF <c>EmiOffers.EffectFeasible</c>).
/// An offer is dealt only when both say yes; an unset probe refuses, as WPF's null service did.
/// </summary>
[Collection("EmiLineEngineProbes")]
public class EmiLineEngineAskProbeTests
{
    // A scripted ceremony moment with certain ask odds: the cadence gates, floor and odds all pass,
    // so the probes are the only thing deciding whether the ask is dealt.
    private const string File =
        "{\"version\":1,\"moments\":{\"m\":{\"priority\":3,\"scripted\":true,\"askOdds\":1.0}}," +
        "\"pools\":{},\"asks\":[{\"id\":\"a1\",\"moment\":\"m\",\"q\":\"q?\",\"chips\":[\"y\",\"n\"],\"effect\":\"none\"}]}";

    private static AskDraw? Ask(Func<bool>? situation, Func<string?, bool>? feasible)
    {
        var oldS = EmiLineEngine.AskSituationProbe;
        var oldF = EmiLineEngine.EffectFeasibleProbe;
        var oldBed = EmiState.Current.BedtimeUntil;
        try
        {
            // Bedtime is a gate too; pin it off so a persisted value cannot decide the result.
            EmiState.Current.BedtimeUntil = DateTime.MinValue;
            EmiLineEngine.AskSituationProbe = situation;
            EmiLineEngine.EffectFeasibleProbe = feasible;
            var engine = EmiLineEngine.FromJson(File);
            engine.Draw("m");
            return engine.DrawAsk("m");
        }
        finally
        {
            EmiLineEngine.AskSituationProbe = oldS;
            EmiLineEngine.EffectFeasibleProbe = oldF;
            EmiState.Current.BedtimeUntil = oldBed;
        }
    }

    [Fact]
    public void BothProbesYesDealsTheAsk()
        => Assert.Equal("a1", Ask(() => true, _ => true)?.Id);

    [Fact]
    public void SituationNoOrUnsetRefuses()
    {
        Assert.Null(Ask(() => false, _ => true));
        Assert.Null(Ask(null, _ => true));
    }

    [Fact]
    public void InfeasibleOrUnsetEffectRefuses()
    {
        Assert.Null(Ask(() => true, _ => false));
        Assert.Null(Ask(() => true, null));
    }
}
