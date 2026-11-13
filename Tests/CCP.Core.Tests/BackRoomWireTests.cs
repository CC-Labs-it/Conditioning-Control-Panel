using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.BackRoom;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

public class BackRoomWireTests
{
    [Fact]
    public void SettingsRespectMotionAndConsent()
    {
        var s = new AppSettings { MotionLevel = MotionLevel.Reduced, BackRoomFxIntensity = BackRoomFxIntensity.Full,
            BackRoomTunnel = true, BackRoomMediaSource = "online" };
        Assert.Equal("calm", BackRoomWire.IntensityWire(s, s.MotionLevel));
        Assert.Equal("full", BackRoomWire.IntensityChoiceWire(s));
        Assert.Equal("full", BackRoomWire.IntensityWire(s, MotionLevel.Full));
        Assert.True((bool)JObject.FromObject(BackRoomWire.GatesWire(s))["tunnel"]!);
        Assert.Equal("local", BackRoomWire.EffectiveMediaSource(s));
        s.RemoteMediaConsented = true;
        Assert.Equal("online", (string?)JObject.FromObject(BackRoomWire.MediaWire(s))["effective"]);
    }
}
