using System;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Shapes;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Views.AvatarTube;
using ConditioningControlPanel.Services.Descent;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Helpers;
using Xunit;
using AvApp = ConditioningControlPanel.Avalonia.App;

namespace CCP.Avalonia.Tests;

public class AvatarTubeFuseCandleTests
{
    [Fact]
    public Task CandleFollowsPhasesAndStopsOnClose() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var oldFuse = AvApp.DescentCountdown;
        var settings = CoreSettings.Current;
        var oldGentle = settings.GentleVisuals;
        settings.GentleVisuals = true;
        using var fuse = new DescentCountdownService();
        AvApp.DescentCountdown = fuse;
        var tube = new AvatarTubeWindow((Window?)null);
        try
        {
            tube.Show();
            var host = tube.FindControl<Grid>("FuseCandleHost")!;
            var halo = tube.FindControl<Ellipse>("FuseCandleHalo")!;
            var flame = tube.FindControl<Path>("FuseCandleFlame")!;
            Assert.False(host.IsVisible);
            Raise(fuse, DescentFusePhase.Candle);
            Assert.True(host.IsVisible);
            Assert.Equal(0.5, halo.Opacity);
            Assert.Equal(1, ((ScaleTransform)flame.RenderTransform!).ScaleY);
            Raise(fuse, DescentFusePhase.Vigil);
            Assert.True(host.IsVisible);
            Raise(fuse, DescentFusePhase.Zero);
            Assert.False(host.IsVisible);
            Raise(fuse, DescentFusePhase.Candle);
            tube.Close();
            Raise(fuse, DescentFusePhase.Dark);
            Assert.True(host.IsVisible);
        }
        finally
        {
            tube.Close();
            AvApp.DescentCountdown = oldFuse;
            settings.GentleVisuals = oldGentle;
        }
        return Task.CompletedTask;
    });

    [Fact]
    public Task FlickerUsesWpfPeriodsAndStops() => AvaloniaTestDispatcher.RunAsync(() =>
    {
        var settings = CoreSettings.Current;
        var saved = (settings.MotionLevel, settings.GentleVisuals, settings.PerformanceMode,
            settings.AutoPerformanceMode, settings.LighterEffects, UiMotionPreference.OsProbe, AvApp.DescentCountdown);
        settings.MotionLevel = MotionLevel.Full;
        settings.GentleVisuals = false;
        settings.PerformanceMode = settings.AutoPerformanceMode = false;
        settings.LighterEffects = LighterEffectsMode.Off;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        using var fuse = new DescentCountdownService();
        AvApp.DescentCountdown = fuse;
        var tube = new AvatarTubeWindow((Window?)null);
        try
        {
            tube.Show();
            Raise(fuse, DescentFusePhase.Vigil);
            var loop = (BeatLoop?)typeof(AvatarTubeWindow).GetField("_candleLoop", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(tube);
            Assert.NotNull(loop);
            var step = (Action<double>)typeof(BeatLoop).GetField("_step", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(loop)!;
            var halo = tube.FindControl<Ellipse>("FuseCandleHalo")!;
            var scale = (ScaleTransform)tube.FindControl<Path>("FuseCandleFlame")!.RenderTransform!;
            step(1.7);
            Assert.Equal(0.62, halo.Opacity, 5);
            step(2.3);
            Assert.Equal(1.08, scale.ScaleY, 5);
            Raise(fuse, DescentFusePhase.Zero);
            Assert.False(loop.IsRunning);
            Assert.Equal(0.5, halo.Opacity);
            Assert.Equal(1, scale.ScaleY);
        }
        finally
        {
            tube.Close();
            (settings.MotionLevel, settings.GentleVisuals, settings.PerformanceMode, settings.AutoPerformanceMode,
                settings.LighterEffects, UiMotionPreference.OsProbe, AvApp.DescentCountdown) = saved;
            UiMotionPreference.CacheStamp = long.MinValue;
        }
        return Task.CompletedTask;
    });

    private static void Raise(DescentCountdownService fuse, DescentFusePhase phase)
    {
        var handlers = (EventHandler<DescentFusePhaseChangedEventArgs>?)typeof(DescentCountdownService)
            .GetField("PhaseChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(fuse);
        handlers?.Invoke(fuse, new DescentFusePhaseChangedEventArgs(DescentFusePhase.Dark, phase));
    }
}
