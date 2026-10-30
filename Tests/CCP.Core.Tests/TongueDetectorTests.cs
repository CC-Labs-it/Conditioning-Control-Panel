using System;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Core.Tests;

public sealed class TongueDetectorTests
{
    private readonly TongueDetector _detector = new();
    private readonly DateTime _now = new(2026, 1, 1);

    [Fact]
    public void TongueIsGatedByMouthAndNeeds350MsWithHysteresis()
    {
        Assert.False(_detector.Update(false, 1, _now));
        Assert.False(_detector.Update(false, 1, _now.AddSeconds(1)));
        Assert.False(_detector.Update(true, 0.29, _now));
        Assert.False(_detector.Update(true, 0.20, _now.AddMilliseconds(349)));
        Assert.True(_detector.Update(true, 0.20, _now.AddMilliseconds(350)));
        Assert.False(_detector.Update(true, 0.9, _now.AddSeconds(1)));
        Assert.False(_detector.Update(true, 0.18, _now.AddSeconds(2))); // strict leave boundary
        Assert.False(_detector.Update(true, 0.28, _now.AddSeconds(3))); // strict enter boundary
        Assert.False(_detector.Update(true, 0.29, _now.AddSeconds(4)));
        Assert.True(_detector.Update(true, 0.29, _now.AddMilliseconds(4350)));
    }

    [Fact]
    public void TongueCooldownAndFaceLossCancelPendingGestures()
    {
        Assert.False(_detector.Update(true, 1, _now));
        Assert.True(_detector.Update(true, 1, _now.AddMilliseconds(350)));
        Assert.False(_detector.Update(true, 0, _now.AddMilliseconds(351)));
        Assert.False(_detector.Update(true, 1, _now.AddMilliseconds(352)));
        Assert.False(_detector.Update(true, 1, _now.AddMilliseconds(1049)));
        Assert.True(_detector.Update(true, 1, _now.AddMilliseconds(1050)));
        Assert.False(_detector.Update(true, 1, _now.AddSeconds(3)));
        _detector.FaceMissing();
        Assert.False(_detector.Update(true, 1, _now.AddSeconds(4)));          // a new hold starts
        Assert.False(_detector.Update(true, 1, _now.AddMilliseconds(4349)));
        Assert.True(_detector.Update(true, 1, _now.AddMilliseconds(4350)));
    }
}
