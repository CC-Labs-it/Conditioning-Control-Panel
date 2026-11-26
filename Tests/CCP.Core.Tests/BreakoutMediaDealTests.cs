using System.Collections.Generic;
using System.Linq;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.GoonGame;
using ConditioningControlPanel.Services.BackRoom;
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
        BackRoomMediaDeal Deal(string source, int ratio, IReadOnlyList<GameMediaManifest.Entry> clips) =>
            GameMediaManifest.BreakoutMedia(local, 42, 13, source, ratio, clips, System.Array.Empty<BackRoomWord>());
        var online = Deal("online", 30, remote);
        Assert.Equal("online", online.Source);
        Assert.All(online.Gifs, g => Assert.Equal("online", g.Src));
        Assert.Equal(2, Deal("online", 30, remote.Take(2).ToArray()).Gifs.Count);
        Assert.All(Deal("local", 30, remote).Gifs, g => Assert.Equal("pool", g.Src));
        Assert.Empty(Deal("bundled", 30, remote).Gifs);
        Assert.All(Deal("online", 30, new List<GameMediaManifest.Entry>()).Gifs, g => Assert.Equal("pool", g.Src));
        int low = Deal("mixed", 5, remote).Gifs.Count(g => g.Src == "online");
        int high = Deal("mixed", 95, remote).Gifs.Count(g => g.Src == "online");
        Assert.True(low < high);
        Assert.InRange(low, 0, 4);
        Assert.InRange(high, 9, 13);
    }
}
