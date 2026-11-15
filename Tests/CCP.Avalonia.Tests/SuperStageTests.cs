using System;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Platform.Super;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

public class SuperStageTests
{
    [Fact]
    public void SyncTracksEngineAndChangesAndPanicResetsStage() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEngine = SuperStageHost.EngineRunning;
        var oldEffect = SuperStageHost.EffectOn;
        bool running = false, steam = true, pool = true;
        SuperStageHost.EngineRunning = () => running;
        SuperStageHost.EffectOn = e => e == SuperEffect.Creep ? steam : pool;
        try
        {
            SuperStageHost.Stop();
            SuperStageHost.Sync();
            Assert.False(SuperStageHost.Stage.SteamOn);
            Assert.False(SuperStageHost.Stage.PoolOn);
            running = true;
            SuperStageHost.Sync();
            Assert.True(SuperStageHost.Stage.SteamOn);
            Assert.True(SuperStageHost.Stage.PoolOn);
            var field = SuperStageHost.Stage.Field("test", 0, 0, 100, 100);
            steam = false;
            SuperAccess.RaiseChanged(SuperEffect.Creep);
            Assert.False(SuperStageHost.Stage.SteamOn);
            Assert.NotSame(field, SuperStageHost.Stage.Field("test", 0, 0, 100, 100));
            SuperStageHost.Stage.Pool.Step(1, 5, 5, 0, 1, 1000, false);
            Assert.True(SuperStageHost.Stage.Pool.Size > 0);
            Assert.Contains(PanicSurfaces.All, s => s.Id == "super-stage" && Array.IndexOf(s.After!, "engine") >= 0);
            PanicSurfaces.StopAll("super-stage-test");
            Assert.False(SuperStageHost.Stage.PoolOn);
            Assert.Equal(0, SuperStageHost.Stage.Pool.Size);
            running = false;
            SuperStageHost.Sync();
            Assert.False(SuperStageHost.Stage.PoolOn);
        }
        finally
        {
            SuperStageHost.Stop();
            SuperStageHost.EngineRunning = oldEngine;
            SuperStageHost.EffectOn = oldEffect;
        }
    });
}
