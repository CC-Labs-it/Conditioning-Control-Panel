using System.Collections.Generic;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using Xunit;
using App = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class ChaosPayloadFiringTests
{
    [Fact]
    public void CascadeUsesWpfParametersAndGlobalDuration()
    {
        var old = EffectPayload.GlobalDurationMult;
        var oldFirer = EffectPayload.Firer;
        try
        {
            EffectPayload.GlobalDurationMult = 2.5;
            double[]? actual = null;
            EffectPayload.Firer = p => App.FireChaosPayload(p, (rate, duration, size, speed, opacity, scale) =>
                actual = new[] { rate, duration, size, speed, opacity, scale });
            new GifCascadePayload { DurationMult = 3 }.Fire();
            Assert.Equal(new[] { 1.67, 15.0, 400.0, 3.6, 0.9, 0.45 }, actual);
        }
        finally { EffectPayload.GlobalDurationMult = old; EffectPayload.Firer = oldFirer; }
    }

    [Fact]
    public void OtherPayloadDoesNotStartCascade()
    {
        bool called = false;
        App.FireChaosPayload(new FlashPayload(), (_, _, _, _, _, _) => called = true);
        Assert.False(called);
    }

    [Fact]
    public void EmptyPoolDoesNotCreateOverlay()
    {
        ChaosGifCascadeOverlay.CloseActive();
        ChaosGifCascadeOverlay.ShowWithFiles(new List<string>(), 1.67, 6, 400, 3.6, 0.9, 0.45);
        Assert.Null(ChaosGifCascadeOverlay.Active);
    }
}
