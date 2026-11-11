using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class GameMediaSourceTests
{
    [Fact]
    public void AutoResolvesAppSourceAndRequiresConsent()
    {
        var settings = new AppSettings { BackRoomMediaSource = "auto", MediaSource = "online" };
        Assert.Equal("local", GameMediaManifest.EffectiveSource(settings));
        settings.RemoteMediaConsented = true;
        Assert.Equal("online", GameMediaManifest.EffectiveSource(settings));
        settings.BackRoomMediaSource = "mixed";
        settings.RemoteMediaConsented = false;
        Assert.Equal("local", GameMediaManifest.EffectiveSource(settings));
    }

    [Fact]
    public void BundledSelectionSkipsLocalLibrary()
    {
        var library = new GameMediaManifest.Manifest();
        library.Images.Add(new GameMediaManifest.Entry("a.gif", "https://ccp.assets/images/a.gif"));
        var settings = new AppSettings { BackRoomMediaSource = "bundled" };
        var deal = JObject.FromObject(GameMediaManifest.Deal(library, "r", 1, 4, settings));
        Assert.Equal("bundled", (string?)deal["source"]);
        Assert.Empty((JArray)deal["gifs"]!);
        settings.BackRoomMediaSource = "local";
        Assert.Equal("local", (string?)JObject.FromObject(GameMediaManifest.Deal(library, "r", 1, 4, settings))["source"]);
    }
}
