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
        var shared = SuperStageHost.Stage;
        var pool = shared.Pool;
        var beforeState = (shared.PoolOn, shared.SteamOn, pool.X, pool.Y, pool.Size,
            pool.Angle, pool.Dip, pool.ShortSide, pool.ReachScale);
        SuperStageHost.WithIsolatedStageForTests(() =>
        {
            var stage = SuperStageHost.Stage;
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
        });
        Assert.Throws<System.InvalidOperationException>(() =>
            SuperStageHost.WithIsolatedStageForTests(() => throw new System.InvalidOperationException()));
        Assert.Same(shared, SuperStageHost.Stage);
        Assert.Equal(beforeState, (shared.PoolOn, shared.SteamOn, pool.X, pool.Y, pool.Size,
            pool.Angle, pool.Dip, pool.ShortSide, pool.ReachScale));
    });
}
