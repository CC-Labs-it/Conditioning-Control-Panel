using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Chaos;
using Newtonsoft.Json.Linq;
using Xunit;

namespace CCP.Core.Tests;

public class DtrhBarkRouteTests
{
    // WPF DtrhHostService.cs:729-786 and BarkService.cs:488-591.
    public static IEnumerable<object[]> Routes()
    {
        yield return new object[] { "ending-soon", "ChaosEndingSoon", "{}" };
        yield return new object[] { "wave-cleared", "ChaosWaveCleared", "{wave:2}" };
        yield return new object[] { "wave-escalated", "ChaosWaveEscalated", "{wave:2}" };
        yield return new object[] { "act-changed", "ChaosActChanged", "{act:3,wave:2}" };
        yield return new object[] { "benign-popped", "ChaosBenignPopped", "{variant_id:'v',payload:'p',combo:4}" };
        yield return new object[] { "defused", "ChaosBubbleDefused", "{combo:4,payload:'v',difficulty:'hard'}" };
        yield return new object[] { "detonated", "ChaosBubbleDetonated", "{payload:'v',strength:0.5,shield_absorbed:false,run_detonations:6,combo:4,difficulty:'hard'}" };
        yield return new object[] { "detonated-absorbed", "ChaosBubbleDetonatedAbsorbed", "{payload:'v',strength:0.5,shield_absorbed:true,run_detonations:6,combo:4,difficulty:'hard',shields_left:7}" };
        yield return new object[] { "darter-caught", "ChaosDarterCaught", "{points:8,combo:4,quick:false}" };
        yield return new object[] { "rabbit-caught", "ChaosDarterCaught", "{points:9,combo:0,quick:true}" };
        yield return new object[] { "freeze-caught", "ChaosFreezeCaught", "{points:8,combo:4}" };
        yield return new object[] { "combo-milestone", "ChaosComboMilestone", "{combo:4,difficulty:'hard'}" };
        yield return new object[] { "combo-big", "ChaosComboBig", "{combo:4,threshold:10}" };
        yield return new object[] { "boon-picked", "ChaosBoonPicked", "{boon:'n'}" };
        yield return new object[] { "curse-picked", "ChaosCursePicked", "{boon:'n',rarity:'rare',run_mult_bonus:1.2}" };
        yield return new object[] { "boon-skipped", "ChaosBoonSkipped", "{shields_now:7}" };
        yield return new object[] { "draft-autopick", "ChaosDraftAutopick", "{}" };
        yield return new object[] { "focus-low", "ChaosFocusLow", "{}" };
        yield return new object[] { "defuse-first", "ChaosDefuseFirst", "{}" };
        yield return new object[] { "defuse-nofocus", "ChaosDefuseNoFocus", "{}" };
        yield return new object[] { "defuse-release", "ChaosDefuseRelease", "{}" };
        yield return new object[] { "click-detonate", "ChaosClickDetonate", "{}" };
        yield return new object[] { "tease-debut", "ChaosTeaseDebut", "{}" };
        yield return new object[] { "tease-clicked", "ChaosTeaseClicked", "{}" };
        yield return new object[] { "tease-denied", "ChaosTeaseDenied", "{denied_count:11}" };
        yield return new object[] { "tease-denied-streak", "ChaosTeaseDeniedStreak", "{denied_count:11}" };
        yield return new object[] { "gold-first", "ChaosGoldFirst", "{}" };
        yield return new object[] { "dollhouse-first-open", "ChaosDollhouseFirstOpen", "{}" };
        yield return new object[] { "reveal-flash", "ChaosRevealFlash", "{element:'i'}" };
        yield return new object[] { "lesson-complete", "ChaosLessonComplete", "{lesson_id:'i'}" };
        yield return new object[] { "duo-demo", "ChaosDuoDemo", "{}" };
        yield return new object[] { "crafted", "ChaosFirstTime", "{bonus_id:'i'}" };
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public void MapsWpfTriggerAndContext(string name, string trigger, string expected)
    {
        var o = JObject.Parse("{variant:'v',payload:'p',combo:4,difficulty:'hard',strength:0.5,runDetonations:6,shields:7,points:8,quick:false,gold:9,wave:2,act:3,threshold:10,name:'n',rarity:'rare',mult:1.2,count:11,id:'i'}");
        o["event"] = name;
        var result = DtrhBarkRoute.Route(o)!;
        Assert.Equal(trigger, result.Trigger);
        var values = JObject.Parse(expected);
        Assert.Equal(values.Count, result.Values.Count);
        foreach (var property in values.Properties())
        {
            Assert.True(result.Values.TryGetValue(property.Name, out var actual), property.Name);
            if (property.Value.Type is JTokenType.Integer or JTokenType.Float)
                Assert.Equal((double)property.Value, Convert.ToDouble(actual));
            else Assert.Equal(((JValue)property.Value).Value, actual);
        }
    }

    [Theory]
    [InlineData("effect-fired")]
    [InlineData("unknown")]
    public void IgnoresEventsWithoutVoice(string name) => Assert.Null(DtrhBarkRoute.Route(new JObject { ["event"] = name }));
}
