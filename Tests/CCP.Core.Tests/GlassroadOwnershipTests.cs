using System;
using System.Linq;
using ConditioningControlPanel.Services.Race;
using Xunit;
using Newtonsoft.Json.Linq;

namespace CCP.Core.Tests;

public class GlassroadOwnershipTests
{
    [Fact]
    public void OnlyKartGrantsLeaveTheHost_SortedDistinctAndBounded()
    {
        var grants = new[] { "cos.rain.hair.ink", "cos.kart.body.petal", "cos.kart.body.petal",
            "cos.kart.umbrella.lace", "backroom.x", "cos.kart." + new string('a', 60), "COS.kart.body.x" };
        Assert.Equal(new[] { "cos.kart.body.petal", "cos.kart.umbrella.lace" },
            GlassroadOwnership.KartGrants(grants).Values<string>().ToArray());
        Assert.Empty(GlassroadOwnership.KartGrants(null));
        var many = Enumerable.Range(0, 100).Select(i => $"cos.kart.body.{i:000}").Reverse();
        var rows = GlassroadOwnership.KartGrants(many);
        Assert.Equal(64, rows.Count);
        Assert.Equal("cos.kart.body.000", (string?)rows.First);
        Assert.Equal("cos.kart.body.063", (string?)rows.Last);
        var push = GlassroadOwnership.OwnedFrame(grants);
        Assert.Equal("owned", (string?)push["type"]);
        Assert.Equal(GlassroadOwnership.KartGrants(grants).ToString(), push["grants"]!.ToString());
        Assert.Empty(GlassroadOwnership.OwnedFrame(null)["grants"]!);
    }
}
