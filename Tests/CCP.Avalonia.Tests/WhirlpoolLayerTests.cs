using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Super;
using ConditioningControlPanel.SuperCore;
using Xunit;

namespace CCP.Avalonia.Tests;

public class WhirlpoolLayerTests
{
    [Fact]
    public void PoolStepsOnlyWhenOnAndUsesSizeAndGentleSpeed() => AvaloniaTestDispatcher.Run(() =>
    {
        var stage = SuperStageHost.Stage;
        try
        {
            SuperStageHost.Stop();
            SuperStageHost.StepPool(0.1, 100, 100, 1, 1000, 150, false);
            Assert.Equal(0, stage.Pool.Size);
            stage.PoolOn = true;
            double before = stage.Pool.Angle;
            SuperStageHost.StepPool(0.1, 100, 100, 1, 1000, 150, false);
            Assert.True(stage.Pool.Size > 0);
            Assert.Equal(WhirlpoolOptions.ReachScale(150), stage.Pool.ReachScale);
            double normalTurn = stage.Pool.Angle - before;
            SuperStageHost.Stop();
            stage.PoolOn = true;
            double angle = stage.Pool.Angle;
            SuperStageHost.StepPool(0.1, 100, 100, 1, 1000, 50, true);
            Assert.Equal(WhirlpoolOptions.ReachScale(50), stage.Pool.ReachScale);
            Assert.Equal(normalTurn / 2, stage.Pool.Angle - angle, 8);
            SuperStageHost.StepPool(0.1, 1000, 1000, 1, 1000, 50, true);
            Assert.Equal(0, stage.Pool.Size);
            SuperStageHost.Stop();
            Assert.False(stage.PoolOn);
            Assert.Equal(0, stage.Pool.Size);
        }
        finally { SuperStageHost.Stop(); }
    });
}
