using System;
using ConditioningControlPanel.Services.Webcam;
using Xunit;

namespace CCP.Core.Tests;

public sealed class MouthGesturesTests
{
    private readonly MouthGestures _detector = new();
    private DateTime _now = new(2026, 1, 1);
    private bool Mouth(double mar, int ms = 33, double sensitivity = 0.5)
    {
        _now = _now.AddMilliseconds(ms);
        return _detector.UpdateMouth(mar, sensitivity, _now);
    }
    private void Seed(double baseline = 0.1)
    {
        for (int i = 0; i < 90; i++) Assert.False(Mouth(baseline));
    }
    private int Hold(double mar, int frames = 30, double sensitivity = 0.5)
    {
        int fires = 0;
        for (int i = 0; i < frames; i++) if (Mouth(mar, sensitivity: sensitivity)) fires++;
        return fires;
    }

    [Fact]
    public void SpeechAndSingleFrameSpikesDoNotFire()
    {
        Seed();
        Assert.Equal(0, Hold(0.25));
        Assert.False(Mouth(0.8));
        Assert.Equal(0, Hold(0.1));
    }

    [Fact]
    public void MouthRequires250MsAndFiresOnceUntilReclosed()
    {
        Seed();
        Assert.False(Mouth(0.6)); // median still closed
        Assert.False(Mouth(0.6)); // opens
        Assert.False(Mouth(0.6, 249));
        Assert.True(Mouth(0.6, 1));
        Assert.Equal(0, Hold(0.6, 100)); // baseline must not drift upward during a hold
        Hold(0.1);
        Assert.Equal(1, Hold(0.6));
    }

    [Fact]
    public void CooldownRequires800MsEvenAfterReopening()
    {
        Seed();
        Mouth(0.6); Mouth(0.6);
        Assert.True(Mouth(0.6, 250));
        Mouth(0.1, 1); Mouth(0.1, 1);
        Mouth(0.6, 1); Mouth(0.6, 1);
        Assert.False(Mouth(0.6, 795));
        Assert.True(Mouth(0.6, 1));
    }

    [Fact]
    public void RestartResetsBaselineButPreservesTheWpfCooldown()
    {
        Seed(); Mouth(0.6); Mouth(0.6);
        Assert.True(Mouth(0.6, 250));
        _detector.Reset();
        for (int i = 0; i < 15; i++) Assert.False(Mouth(0.1, 1));
        Mouth(0.6, 1); Mouth(0.6, 1);
        Assert.False(Mouth(0.6, 250));
        Assert.True(Mouth(0.6, 533));
    }

    [Fact]
    public void HighRestingBaselineDoesNotLatchOpen()
    {
        Seed(0.3);
        Assert.Equal(1, Hold(0.7));
        Assert.Equal(0, Hold(0.3));
        Assert.False(_detector.IsOpen);
        Assert.Equal(1, Hold(0.7));
    }

    [Fact]
    public void SensitivityScalesEntryAndAbsoluteFloorRescuesDimFrames()
    {
        Seed(0.3);
        Assert.Equal(0, Hold(0.4, sensitivity: 0));
        _detector.Reset(); Seed(0.3);
        Assert.Equal(1, Hold(0.4)); // below 1.65 x baseline; absolute floor rescues it
        _detector.Reset(); Seed();
        Assert.Equal(1, Hold(0.25, sensitivity: 1));
    }

    [Fact]
    public void TongueIsGatedByMouthAndNeeds350MsWithHysteresis()
    {
        Seed();
        Assert.False(_detector.UpdateTongue(1, _now));
        Assert.False(_detector.UpdateTongue(1, _now.AddSeconds(1)));
        Hold(0.6);
        Assert.False(_detector.UpdateTongue(0.29, _now));
        Assert.False(_detector.UpdateTongue(0.20, _now.AddMilliseconds(349)));
        Assert.True(_detector.UpdateTongue(0.20, _now.AddMilliseconds(350)));
        Assert.False(_detector.UpdateTongue(0.9, _now.AddSeconds(1)));
        Assert.False(_detector.UpdateTongue(0.18, _now.AddSeconds(2))); // strict leave boundary
        Assert.False(_detector.UpdateTongue(0.28, _now.AddSeconds(3))); // strict enter boundary
        Assert.False(_detector.UpdateTongue(0.29, _now.AddSeconds(4)));
        Assert.True(_detector.UpdateTongue(0.29, _now.AddMilliseconds(4350)));
    }

    [Fact]
    public void TongueCooldownAndFaceLossCancelPendingGestures()
    {
        Seed(); Hold(0.6);
        Assert.False(_detector.UpdateTongue(1, _now));
        Assert.True(_detector.UpdateTongue(1, _now.AddMilliseconds(350)));
        Assert.False(_detector.UpdateTongue(0, _now.AddMilliseconds(351)));
        Assert.False(_detector.UpdateTongue(1, _now.AddMilliseconds(352)));
        Assert.False(_detector.UpdateTongue(1, _now.AddMilliseconds(1049)));
        Assert.True(_detector.UpdateTongue(1, _now.AddMilliseconds(1050)));
        _detector.FaceMissing();
        Assert.False(_detector.IsOpen);
        Assert.False(_detector.UpdateTongue(1, _now.AddSeconds(4)));
        Assert.False(Mouth(0.6)); // newly acquired face must start another hold
        Assert.False(Mouth(0.6, 249));
        Assert.True(Mouth(0.6, 1));
    }
}
