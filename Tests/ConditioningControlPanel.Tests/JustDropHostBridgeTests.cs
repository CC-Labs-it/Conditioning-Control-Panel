using System.Linq;
using ConditioningControlPanel.Services.Haptics;
using ConditioningControlPanel.Services.JustDrop;
using Newtonsoft.Json.Linq;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>
/// The Just Drop host bridge (handoff-1121/JUSTDROP-WIRING-BRIEF.md, fixed contract): only the live
/// site may ask, every number off the wire is clamped, the asset host answers only ids it handed
/// out and never shows a path, and caps say what the contract says.
/// </summary>
public class JustDropHostBridgeTests
{
    [Theory]
    [InlineData("https://app.cclabs.app/express/play?order=X", true)]
    [InlineData("https://app.cclabs.app/dashboard/express", true)]
    [InlineData("http://app.cclabs.app/express/play", false)]
    [InlineData("https://app.cclabs.app:8443/express/play", false)]
    [InlineData("https://evil.app.cclabs.app.example/express/play", false)]
    [InlineData("https://cclabs.app/express/play", false)]
    [InlineData("https://user@app.cclabs.app/express/play", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_live_site_may_ask(string? source, bool expected)
        => Assert.Equal(expected, JustDropBridge.IsSiteSource(source));

    [Fact]
    public void The_envelope_must_be_the_pages_v1()
    {
        Assert.True(JustDropBridge.IsPageEnvelope(JObject.Parse("{source:'justdrop',v:1,type:'haptic'}")));
        Assert.False(JustDropBridge.IsPageEnvelope(JObject.Parse("{source:'justdrop',v:2,type:'haptic'}")));
        Assert.False(JustDropBridge.IsPageEnvelope(JObject.Parse("{source:'ccp-host',v:1,type:'haptic'}")));
        Assert.False(JustDropBridge.IsPageEnvelope(JObject.Parse("{source:'justdrop',v:'1',type:'haptic'}")));
    }

    [Fact]
    public void A_haptic_is_clamped()
    {
        var h = JustDropBridge.ParseHaptic(JObject.Parse("{item:'i_haptic',kind:'pulse',intensity:7,durationMs:99999}"))!.Value;
        Assert.Equal(1.0, h.Intensity);
        Assert.Equal(JustDropBridge.MaxHapticMs, h.DurationMs);
        var low = JustDropBridge.ParseHaptic(JObject.Parse("{item:'a_heart',kind:'wave',intensity:-1,durationMs:1}"))!.Value;
        Assert.Equal(0.0, low.Intensity);
        Assert.Equal(JustDropBridge.MinHapticMs, low.DurationMs);
        Assert.Equal(JustDropBridge.HapticKind.Wave, low.Kind);
    }

    [Theory]
    [InlineData("{item:'i_haptic',kind:'buzz',intensity:0.5,durationMs:200}")]
    [InlineData("{item:'',kind:'pulse',intensity:0.5,durationMs:200}")]
    [InlineData("{kind:'pulse',intensity:0.5,durationMs:200}")]
    [InlineData("{item:'i_haptic',kind:'pulse',intensity:'0.5',durationMs:200}")]
    [InlineData("{item:'i_haptic',kind:'pulse',intensity:0.5}")]
    public void A_bad_haptic_is_dropped(string json)
        => Assert.Null(JustDropBridge.ParseHaptic(JObject.Parse(json)));

    [Fact]
    public void A_stop_needs_no_numbers()
        => Assert.Equal(JustDropBridge.HapticKind.Stop,
            JustDropBridge.ParseHaptic(JObject.Parse("{item:'i_breath',kind:'stop'}"))!.Value.Kind);

    [Theory]
    [InlineData("{kind:'image',max:0}", false, 1)]
    [InlineData("{kind:'video',max:5000}", true, 300)]
    [InlineData("{kind:'video',max:12}", true, 12)]
    [InlineData("{kind:'image'}", false, 300)]
    public void An_assets_request_is_clamped(string json, bool video, int max)
    {
        var a = JustDropBridge.ParseAssets(JObject.Parse(json))!.Value;
        Assert.Equal(video, a.Video);
        Assert.Equal(max, a.Max);
    }

    [Fact]
    public void An_assets_request_of_another_kind_is_dropped()
        => Assert.Null(JustDropBridge.ParseAssets(JObject.Parse("{kind:'audio',max:3}")));

    [Fact]
    public void Caps_follow_the_contract()
    {
        var c = JustDropBridge.Caps(hapticsOpen: true, toyConnected: false, images: 4, videos: -2, mediaSource: "mixed");
        Assert.Equal("ccp-host", (string?)c["source"]);
        Assert.Equal(1, (int)c["v"]!);
        Assert.Equal("caps", (string?)c["type"]);
        Assert.False((bool)c["payload"]!["haptics"]!);
        Assert.Equal(4, (int)c["payload"]!["assets"]!["images"]!);
        Assert.Equal(0, (int)c["payload"]!["assets"]!["videos"]!);
        Assert.Equal("mixed", (string?)c["payload"]!["mediaSource"]);
        Assert.True((bool)JustDropBridge.Caps(true, true, 0, 0, "local")["payload"]!["haptics"]!);
    }

    [Theory]
    [InlineData("local", true, "local")]
    [InlineData("online", true, "online")]
    [InlineData("mixed", true, "mixed")]
    [InlineData("online", false, "local")]
    [InlineData("mixed", false, "local")]
    [InlineData("bogus", true, "local")]
    [InlineData(null, true, "local")]
    public void Media_source_is_the_apps_with_the_consent_collapse(string? setting, bool consent, string expected)
        => Assert.Equal(expected, JustDropBridge.EffectiveMediaSource(setting, consent));

    [Fact]
    public void The_asset_host_hands_out_opaque_ids_only()
    {
        var host = new JustDropAssetHost();
        var path = @"C:\Users\someone\assets\videos\secret name.mp4";
        var url = host.Take(path, new JustDropAssetHost.Entry { Ext = ".MP4", Video = true, DiskPath = path })!;
        Assert.StartsWith("https://justdrop-assets.ccp/", url);
        Assert.EndsWith(".mp4", url);
        Assert.DoesNotContain("secret", url);
        Assert.DoesNotContain("someone", url);
        Assert.Matches("^https://justdrop-assets\\.ccp/[0-9a-f]{32}\\.mp4$", url);
        Assert.Equal(url, host.Take(path, new JustDropAssetHost.Entry { Ext = ".mp4", Video = true, DiskPath = path }));
        Assert.Same(host.Resolve(url), host.Resolve(url + "?t=1"));
        Assert.Equal(path, host.Resolve(url)!.DiskPath);
    }

    [Fact]
    public void The_asset_host_refuses_what_it_never_handed_out()
    {
        var host = new JustDropAssetHost();
        var url = host.Take("a", new JustDropAssetHost.Entry { Ext = ".png", Video = false, DiskPath = "a" })!;
        var id = url.Substring("https://justdrop-assets.ccp/".Length, 32);
        Assert.Null(host.Resolve("https://justdrop-assets.ccp/" + new string('0', 32) + ".png"));
        Assert.Null(host.Resolve("https://justdrop-assets.ccp/" + id + ".mp4"));          // wrong extension
        Assert.Null(host.Resolve("http://justdrop-assets.ccp/" + id + ".png"));
        Assert.Null(host.Resolve("https://other.ccp/" + id + ".png"));
        Assert.Null(host.Resolve("https://justdrop-assets.ccp/C:/a.png"));
        Assert.Null(host.Resolve(null));
        host.Clear();
        Assert.Null(host.Resolve(url));   // a new window life knows nothing of the old ids
    }

    [Fact]
    public void The_asset_host_takes_only_what_the_page_can_show()
    {
        var host = new JustDropAssetHost();
        Assert.Null(host.Take("x.tiff", new JustDropAssetHost.Entry { Ext = ".tiff", Video = false, DiskPath = "x.tiff" }));
        Assert.Null(host.Take("x.avi", new JustDropAssetHost.Entry { Ext = ".avi", Video = true, DiskPath = "x.avi" }));
        Assert.Null(host.Take("x.mp4", new JustDropAssetHost.Entry { Ext = ".mp4", Video = false, DiskPath = "x.mp4" }));
    }

    [Fact]
    public void Answers_name_only_the_site_as_reader()
    {
        var h = JustDropAssetHost.Cors();
        Assert.Contains("Access-Control-Allow-Origin: https://app.cclabs.app", h);
        Assert.DoesNotContain("Access-Control-Allow-Origin: *", h);
    }

    [Fact]
    public void A_burst_of_haptics_is_cut_down()
    {
        // The director rides the Back Room limiter: a page flooding 40 pulses in one second gets a handful.
        var limit = new BackRoomHapticDirector.Limiter();
        var accepted = Enumerable.Range(0, 40).Count(i => limit.Accept(1000 + i * 25));
        Assert.InRange(accepted, 1, 20);
    }

    [Fact]
    public void Wave_plays_as_a_wave_and_pulse_as_a_flat_pulse()
    {
        Assert.Equal(ConditioningControlPanel.Models.VibrationMode.Wave, JustDropHapticDirector.ModeFor(JustDropBridge.HapticKind.Wave));
        Assert.Equal(ConditioningControlPanel.Models.VibrationMode.Constant, JustDropHapticDirector.ModeFor(JustDropBridge.HapticKind.Pulse));
        Assert.Equal(JustDropBridge.MaxHapticMs, JustDropHapticDirector.DurationFor(9000, buttplug: true));
    }
}
