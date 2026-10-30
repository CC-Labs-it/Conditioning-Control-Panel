using System.Collections.Generic;
using ConditioningControlPanel.Services.Companion.Rain;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public sealed class RainPackRulesTests
{
    [Theory]
    [InlineData("joy", "jump")]
    [InlineData("surprise", "greeting")]
    [InlineData("affection", "idle")]
    [InlineData("confidence", "idle")]
    [InlineData("disappointment", "idle")]
    public void NewReactionsFoldToAvailableClips(string clip, string expected)
    {
        var have = new HashSet<string> { "idle", "greeting", "jump" };
        Assert.Equal(expected, RainPackEmotesFold.Resolve(clip, have));
        var template = JObject.Parse("{mood:{test:'" + clip + "'},clickEmotes:['" + clip + "']}");
        var folded = RainPackEmotesFold.FoldEmotes(template, have);
        Assert.Equal(expected, (string?)folded["mood"]?["test"]);
        Assert.Equal(expected, (string?)folded["clickEmotes"]?[0]);
        have.Add(clip);
        Assert.True(JToken.DeepEquals(template, RainPackEmotesFold.FoldEmotes(template, have)));
    }

    [Fact]
    public void SeventeenActionPacksMissTheOldCacheAndKeepOldHashesWellFormed()
    {
        const string look = "{items:{dress:'rose-flare'},rev:1}";
        var old = RainLookHash.Compute(look, 1, 1);
        Assert.Equal(2, RainLookHash.ClipSetVersion);
        Assert.NotEqual(old, RainLookHash.Compute(look));
        Assert.Equal(RainLookHash.Compute(look, 1, 2), RainLookHash.Compute(look));
        Assert.Equal(RainLookHash.Compute(look), RainLookHash.Compute("{rev:1,items:{dress:'rose-flare'}}"));
        Assert.True(RainLookHash.IsWellFormed(old));
        Assert.True(RainLookHash.IsWellFormed(RainLookHash.Compute(look)));
    }
}
