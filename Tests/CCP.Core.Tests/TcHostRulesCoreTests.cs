using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Tc;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>The one seam the Core move added to TcHostRules: the library cap's shape lives here (the WPF head's
/// TcOwnMediaRules.Cap returns it), and a head that cannot answer <c>own-media-list</c> leaves it out.</summary>
public class TcHostRulesCoreTests
{
    [Fact]
    public void Init_offers_the_library_by_default_as_WPF_did()
    {
        var own = (JObject)TcHostRules.InitFrame("en", MotionLevel.Full, false, null, TcRules.Caps("small", false))["caps"]!["ownMedia"]!;
        Assert.True((bool)own["library"]!);
        Assert.Equal("https://ccp.assets", (string?)own["mediaOrigin"]);
        Assert.Null(own["folder"]);
        Assert.True(JToken.DeepEquals(own, TcHostRules.OwnMediaCap()));
    }

    [Fact]
    public void A_head_without_the_library_leaves_the_cap_out()
    {
        var caps = (JObject)TcHostRules.InitFrame("en", MotionLevel.Full, false, null, TcRules.Caps("full", false), ownMedia: false)["caps"]!;
        Assert.Null(caps["ownMedia"]);
        Assert.Equal("full", (string?)caps["edition"]);
    }

    [Fact]
    public void Each_call_builds_a_fresh_cap()
        => Assert.NotSame(TcHostRules.OwnMediaCap(), TcHostRules.OwnMediaCap());
}
