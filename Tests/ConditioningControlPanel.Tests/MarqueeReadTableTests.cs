using System.Linq;
using ConditioningControlPanel.Services.Banner;
using Xunit;

namespace ConditioningControlPanel.Tests;

/// <summary>The WPF act table against the Core ids (the rest of MarqueeReadTests moved to CCP.Core.Tests).</summary>
public class MarqueeReadTableTests
{
    [Fact]
    public void The_table_and_the_ids_are_the_same_five_acts()
    {
        var ids = new[] { MarqueeReadActs.GlitchHeal, MarqueeReadActs.NumberFirst, MarqueeReadActs.Teletype,
                          MarqueeReadActs.NeonWarmUp, MarqueeReadActs.Redaction };
        var built = MarqueeReadEffects.Build().Select(a => a.Id).ToArray();
        Assert.Equal(ids.OrderBy(s => s).ToArray(), built.OrderBy(s => s).ToArray());
        Assert.Equal(5, built.Length);
    }
}
