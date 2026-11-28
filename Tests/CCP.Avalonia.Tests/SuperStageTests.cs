using System;
using System.Linq;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Services.Super;
using Xunit;

namespace CCP.Avalonia.Tests;

/// <summary>The Core stage's lifetime on this head: engine stop and panic reset it at once
/// (WPF MainWindow.xaml.cs panic pass calls SuperStageHost.Stop straight).</summary>
public class SuperStageTests
{
    [Fact]
    public void PanicResetsTheStageAfterTheEngineStops() => AvaloniaTestDispatcher.Run(() =>
    {
        var stage = SuperStageHost.Stage;
        try
        {
            SuperStageHost.Stop();
            stage.SteamOn = stage.PoolOn = true;
            stage.Pool.Step(1, 5, 5, 0, 1, 1000, false);
            Assert.True(stage.Pool.Size > 0);
            Assert.Contains(PanicSurfaces.All, s => s.Id == "super-stage" && Array.IndexOf(s.After!, "engine") >= 0);
            PanicSurfaces.StopAll("super-stage-test");
            Assert.False(stage.SteamOn);
            Assert.False(stage.PoolOn);
            Assert.Equal(0, stage.Pool.Size);
        }
        finally { SuperStageHost.Stop(); }
    });

    [Fact]
    public void PanicStopsActiveSteamAndDropsItsField() => AvaloniaTestDispatcher.Run(() =>
    {
        var stage = SuperStageHost.Stage;
        try
        {
            SuperStageHost.Stop();
            SteamOverlay.Seed();
            SuperStageHost.SteamSurface(true);
            stage.SteamOn = true;
            var field = stage.Field("panic-steam", 0, 0, 100, 100);
            PanicSurfaces.All.Single(s => s.Id == "super-stage").Stop(null);
            Assert.False(stage.SteamOn);
            Assert.False(SteamOverlay.On);
            Assert.NotSame(field, stage.Field("panic-steam", 0, 0, 100, 100));
        }
        finally { SuperStageHost.Stop(); }
    });
}
