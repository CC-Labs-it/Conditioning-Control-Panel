using ConditioningControlPanel.Services.UI;
using Xunit;

namespace CCP.Core.Tests;
public class OverlayZOrderPolicyTests
{
    [Fact]
    public void UsesSameMonitorAndFallsBackWithoutAnchoringVideoToItself()
    {
        var primary = new object(); var secondary = new object(); var effect = new object();
        var anchors = new[] { (primary, 0), (secondary, 1) };
        Assert.Same(secondary, OverlayZOrderPolicy.ResolveVideoAnchor(anchors, effect, 1, primary));
        Assert.Same(primary, OverlayZOrderPolicy.ResolveVideoAnchor(anchors, effect, 2, primary));
        Assert.Same(primary, OverlayZOrderPolicy.ResolveVideoAnchor(anchors, effect, -1, primary));
        Assert.Null(OverlayZOrderPolicy.ResolveVideoAnchor(anchors, secondary, 1, primary));
        Assert.Null(OverlayZOrderPolicy.ResolveVideoAnchor(anchors, primary, 0, primary));
        Assert.Null(OverlayZOrderPolicy.ResolveVideoAnchor(System.Array.Empty<(object, int)>(), effect, 0, null));
    }
}
