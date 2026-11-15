using System.Threading.Tasks;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.SuperCore;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using CCP.Avalonia.Testing;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Avalonia.Views.Windows;
using Xunit;

namespace CCP.Avalonia.Tests;

public sealed class BouncingTextScrawlTests
{
    [Fact]
    public async Task Panic_stops_standalone_bouncing_text_even_without_an_engine()
    {
        await AvaloniaTestDispatcher.RunAsync(() =>
        {
            if (Application.Current == null)
                ConditioningControlPanel.Avalonia.Program.BuildAvaloniaApp().UseSkia()
                    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false }).SetupWithoutStarting();
            var saved = (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests);
            var host = new Window();
            try
            {
                host.Show();
                BouncingTextOverlay.SkipPlatformChecksForTests = true;
                BouncingTextOverlay.ManualFramesForTests = true;
                BouncingTextOverlay.Start(host);
                Assert.True(BouncingTextOverlay.IsRunning);
                var logo = BouncingTextOverlay.Engine.Logos[0];
                logo.Fling.Phase = 1;
                Assert.Single(PanicSurfaces.All, s => s.Id == "bouncing-text").Stop(null);
                Assert.False(BouncingTextOverlay.IsRunning);
                Assert.Equal(0, logo.Fling.Phase);
            }
            finally
            {
                BouncingTextOverlay.Stop();
                host.Close();
                (BouncingTextOverlay.SkipPlatformChecksForTests, BouncingTextOverlay.ManualFramesForTests) = saved;
            }
            return Task.CompletedTask;
        });
    }
    [Fact]
    public void Pool_catches_and_throws_the_word()
    {
        var stage = new SuperStage { PoolOn = true };
        stage.Pool.Step(14, 500, 500, 0, 1, 1000);
        BouncingTextOverlay.Engine.SetBounds(0, 0, 1000, 1000);
        var l = new BouncingTextEngine.Logo { PosX = 450, PosY = 480, TextWidth = 100, TextHeight = 40, VelX = 100, Cruise = 100 };
        Assert.True(BouncingTextOverlay.StepStage(l, stage, 1, 0.01));
        Assert.Equal(1, l.Fling.Phase);
        Assert.False(BouncingTextOverlay.StepStage(l, stage, 1, 1.6));
        Assert.Equal(2, l.Fling.Phase);
        Assert.True(System.Math.Sqrt(l.VelX * l.VelX + l.VelY * l.VelY) > l.Cruise * 2);
    }

    [Fact]
    public void Word_wipes_fog_and_sweeps_its_box()
    {
        var stage = new SuperStage { SteamOn = true };
        var field = stage.Field("test", 0, 0, 1000, 1000);
        System.Array.Fill(field.Fog, 1f);
        double left = -1;
        stage.Swept += (x, _, _, _) => left = x;
        var l = new BouncingTextEngine.Logo { PosX = 450, PosY = 480, TextWidth = 100, TextHeight = 40, VelX = 100, Cruise = 100 };
        Assert.False(BouncingTextOverlay.StepStage(l, stage, 1, 0.1));
        Assert.Equal(450, left);
        Assert.Contains(field.Fog, f => f < 1);
        Assert.All(field.Mem, m => Assert.Equal(0, m));
    }
}
