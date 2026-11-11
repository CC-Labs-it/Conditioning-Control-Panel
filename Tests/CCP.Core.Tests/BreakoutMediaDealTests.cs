using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.GoonGame;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class BreakoutMediaDealTests
{
    [Fact]
    public void RoomChannelsCanUseTheAppWideSelection()
    {
        var subs = Enumerable.Range(0, 12).Select(i => "channel" + i).ToArray();
        Assert.Equal(12, GoonOnlineMediaRules.CleanSubs(subs, 64).Count);
        Assert.Equal(8, GoonOnlineMediaRules.CleanSubs(subs).Count);
    }

    [Fact]
    public void DealHonorsSourceRatioAndColdFallback()
    {
        var local = new GameMediaManifest.Manifest();
        var remote = new List<GameMediaManifest.Entry>();
        for (int i = 0; i < 13; i++)
        {
            local.Images.Add(new GameMediaManifest.Entry("local", $"/local/{i}.gif"));
            remote.Add(new GameMediaManifest.Entry("remote", $"/remote/{i}.webm"));
        }
        JObject Deal(string source, int ratio, IReadOnlyList<GameMediaManifest.Entry> clips) =>
            JObject.FromObject(GameMediaManifest.BreakoutDeal(local, "r", 42, 13, source, ratio, clips));
        var online = Deal("online", 30, remote);
        Assert.Equal("online", (string?)online["source"]);
        Assert.All(online["gifs"]!, g => Assert.Equal("online", (string?)g["src"]));
        Assert.Equal(2, Deal("online", 30, remote.Take(2).ToArray())["gifs"]!.Count());
        Assert.All(Deal("local", 30, remote)["gifs"]!, g => Assert.Equal("pool", (string?)g["src"]));
        Assert.Empty(Deal("bundled", 30, remote)["gifs"]!);
        Assert.All(Deal("online", 30, new List<GameMediaManifest.Entry>())["gifs"]!, g => Assert.Equal("pool", (string?)g["src"]));
        int low = Deal("mixed", 5, remote)["gifs"]!.Count(g => (string?)g["src"] == "online");
        int high = Deal("mixed", 95, remote)["gifs"]!.Count(g => (string?)g["src"] == "online");
        Assert.True(low < high);
        Assert.InRange(low, 0, 4);
        Assert.InRange(high, 9, 13);
    }
}
