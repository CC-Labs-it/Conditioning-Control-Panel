using System.Collections.Generic;
using ConditioningControlPanel.Services.EmiDesk;
using Xunit;

namespace CCP.Core.Tests;

/// <summary>
/// The head seams EMI's names and bark mirror read now that they live in Core. Each stands in
/// for an <c>App.*</c> read the WPF copy made; unset must read exactly like WPF's null service
/// (no ring target = no name, no desk = no fire). The ask probes are EmiLineEngineAskProbeTests.
/// </summary>
public sealed class EmiCoreSeamsTests
{
    [Fact]
    public void Names_ReadRingTargetsThroughTheProbe()
    {
        try
        {
            EmiNames.TargetLabelProbe = id => id == "seam-test-room" ? "  Seam  Test Room " : null;
            Assert.Equal("seam test room", EmiNames.Feature("seam-test-room"));

            EmiNames.TargetLabelProbe = null;
            Assert.Null(EmiNames.Feature("seam-test-room"));
        }
        finally { EmiNames.TargetLabelProbe = null; }
    }

    [Fact]
    public void BarkMirror_FiresOnlyIntoALiveDesk_WithTheVideoName()
    {
        var fired = new List<(string Moment, object? Ctx)>();
        try
        {
            EmiBarkBridge.VideoTitleProbe = () => "Pink Spiral";
            EmiBarkBridge.DeskFire = null;
            EmiBarkBridge.Mirror("VideoStarted", null);
            Assert.Empty(fired);   // no desk

            EmiBarkBridge.DeskFire = () => (m, c) => fired.Add((m, c));
            EmiBarkBridge.Mirror("VideoStarted", null);

            var (moment, ctx) = Assert.Single(fired);
            Assert.Equal("videoRunning", moment);
            Assert.Equal(EmiNames.VideoName("Pink Spiral"), EmiLineEngine.ToCtx(ctx)["target"]);
        }
        finally
        {
            EmiBarkBridge.DeskFire = null;
            EmiBarkBridge.VideoTitleProbe = null;
        }
    }
}
