using ConditioningControlPanel.Services.JustDrop;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class JustDropBridgePortTests
{
    [Fact]
    public void OriginAndEnvelopeMustMatchTheSite()
    {
        Assert.True(JustDropBridge.IsSiteSource("https://app.cclabs.app/express/play"));
        Assert.False(JustDropBridge.IsSiteSource("https://app.cclabs.app.evil.example/"));
        Assert.False(JustDropBridge.IsSiteSource("https://user@app.cclabs.app/"));
        Assert.False(JustDropBridge.IsSiteSource("https://app.cclabs.app:8443/"));
        Assert.True(JustDropBridge.IsPageEnvelope(JObject.Parse("{source:'justdrop',v:1}")));
        Assert.False(JustDropBridge.IsPageEnvelope(JObject.Parse("{source:'justdrop',v:'1'}")));
    }

    [Fact]
    public void HapticPayloadIsValidatedAndClamped()
    {
        var h = JustDropBridge.ParseHaptic(JObject.Parse("{item:'i_haptic',kind:'wave',intensity:7,durationMs:99999}"))!.Value;
        Assert.Equal(1, h.Intensity);
        Assert.Equal(10000, h.DurationMs);
        Assert.Equal(JustDropBridge.HapticKind.Wave, h.Kind);
        Assert.Null(JustDropBridge.ParseHaptic(JObject.Parse("{item:'i_haptic',kind:'pulse',intensity:'1',durationMs:50}")));
        Assert.Equal(JustDropBridge.HapticKind.Stop, JustDropBridge.ParseHaptic(JObject.Parse("{item:'i_haptic',kind:'stop'}"))!.Value.Kind);
    }

    [Fact]
    public void CapsAndAssetsFollowTheWireContract()
    {
        var caps = JustDropBridge.Caps(true, false, 4, -2, JustDropBridge.EffectiveMediaSource("mixed", false));
        Assert.Equal("ccp-host", (string?)caps["source"]);
        Assert.False((bool)caps["payload"]!["haptics"]!);
        Assert.Equal(0, (int)caps["payload"]!["assets"]!["videos"]!);
        Assert.Equal("local", (string?)caps["payload"]!["mediaSource"]);
        Assert.Equal(300, JustDropBridge.ParseAssets(JObject.Parse("{kind:'video',max:5000}"))!.Value.Max);
        Assert.Null(JustDropBridge.ParseAssets(JObject.Parse("{kind:'audio'}")));
    }
}
