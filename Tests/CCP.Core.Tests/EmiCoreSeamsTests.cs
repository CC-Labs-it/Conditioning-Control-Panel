using System.Collections.Generic;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The head seams the line engine, names and bark mirror read now that they live in Core. Each
/// stands in for an <c>App.*</c> read the WPF copy made; unset must read exactly like WPF's
/// null service (no desk = no offer, no fire).
/// </summary>
public sealed class EmiCoreSeamsTests
{
    // One scripted ceremony moment (skips the cadence gates, the floor and the odds) with one offer.
    private const string AskFile =
        "{\"version\":1,\"moments\":{\"m\":{\"priority\":3,\"askOdds\":1,\"scripted\":true}},\"pools\":{},"
        + "\"asks\":[{\"id\":\"a1\",\"moment\":\"m\",\"q\":\"q?\",\"chips\":[\"y\",\"n\"],\"effect\":\"none\"}]}";

    private static AskDraw? DrawAsk()
    {
        var engine = EmiLineEngine.FromJson(AskFile);
        engine.Draw("m");
        return engine.DrawAsk("m");
    }

    [Fact]
    public void Ask_NeedsTheDeskSituationAndAFeasibleEffect()
    {
        try
        {
            EmiLineEngine.AskSituationProbe = null;
            EmiLineEngine.EffectFeasibleProbe = _ => true;
            Assert.Null(DrawAsk());   // no desk, no offer

            EmiLineEngine.AskSituationProbe = () => true;
            EmiLineEngine.EffectFeasibleProbe = null;
            Assert.Null(DrawAsk());   // no offers surface, no offer

            EmiLineEngine.EffectFeasibleProbe = e => e == "none";
            Assert.Equal("a1", DrawAsk()?.Id);

            EmiLineEngine.AskSituationProbe = () => false;
            Assert.Null(DrawAsk());   // a session or video on screen
        }
        finally
        {
            EmiLineEngine.AskSituationProbe = null;
            EmiLineEngine.EffectFeasibleProbe = null;
        }
    }

    [Fact]
    public void Names_ReadRingTargetsThroughTheProbe()
    {
        try
        {
            EmiNames.TargetLabelProbe = id => id == "seam-test-room" ? "  Seam  Test Room " : null;
            Assert.Equal("seam test room", EmiNames.Feature("seam-test-room"));

            EmiNames.TargetLabelProbe = null;
            Assert.Null(EmiNames.Feature("seam-test-room"));
        }
        finally { EmiNames.TargetLabelProbe = null; }
    }

    [Fact]
    public void BarkMirror_FiresOnlyIntoALiveDesk_WithTheVideoName()
    {
        var fired = new List<(string Moment, object? Ctx)>();
        try
        {
            EmiBarkBridge.VideoTitleProbe = () => "Pink Spiral";
            EmiBarkBridge.DeskFire = null;
            EmiBarkBridge.Mirror("VideoStarted", null);
            Assert.Empty(fired);   // no desk

            EmiBarkBridge.DeskFire = () => (m, c) => fired.Add((m, c));
            EmiBarkBridge.Mirror("VideoStarted", null);

            var (moment, ctx) = Assert.Single(fired);
            Assert.Equal("videoRunning", moment);
            Assert.Equal(EmiNames.VideoName("Pink Spiral"), EmiLineEngine.ToCtx(ctx)["target"]);
        }
        finally
        {
            EmiBarkBridge.DeskFire = null;
            EmiBarkBridge.VideoTitleProbe = null;
        }
    }
}
