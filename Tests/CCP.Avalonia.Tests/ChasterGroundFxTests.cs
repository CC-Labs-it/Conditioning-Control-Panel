using System;
using System.Collections.Generic;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using CCP.Avalonia.Testing;
using ConditioningControlPanel;
using ConditioningControlPanel.Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Tabs;
using ConditioningControlPanel.Avalonia.Views.Windows;
using ConditioningControlPanel.Models;
using Xunit;

namespace CCP.Avalonia.Tests;

public class ChasterGroundFxTests
{
    [Fact]
    public void GroundRunsOnArrivalAndHoldsOnMotionChangeAndNavigation() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = CoreSettings.Current;
        var old = (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode);
        var oldProbe = UiMotionPreference.OsProbe;
        var oldTime = BeatLoop.Time;
        var clock = new Clock();
        settings.MotionLevel = MotionLevel.Full;
        settings.PerformanceMode = settings.AutoPerformanceMode = false;
        UiMotionPreference.OsProbe = () => true;
        UiMotionPreference.CacheStamp = long.MinValue;
        BeatLoop.Time = clock;
        var shell = new MainShellWindow();
        try
        {
            shell.Show(); shell.Activate(); Settle();
            shell.ShowTab("chaster"); Settle();
            var view = shell.Named<ChasterTabView>("ChasterTab")!;
            var spiral = view.FindControl<Path>("SpiralPath")!;
            var turn = Assert.IsType<RotateTransform>(view.FindControl<Grid>("SpiralLayer")!.RenderTransform);
            var art = Assert.IsType<ScaleTransform>(view.FindControl<Image>("HeroArt")!.RenderTransform);
            var ambient = view.FindControl<AmbientFxCanvas>("GroundAmbient")!;
            var registry = (Dictionary<string, List<AmbientFxCanvas>>)typeof(MainShellWindow)
                .GetField("_tabFxCanvases", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(shell)!;
            Assert.NotNull(spiral.Data);
            Assert.Contains(ambient, registry["chaster"]);
            Assert.Equal(AmbientFxLayers.FogDrift | AmbientFxLayers.DustField, ambient.Layers);
            Assert.True(ambient.IsTicking);
            clock.Step(5);
            Assert.True(turn.Angle > 0);
            Assert.True(art.ScaleX > 1);
            settings.MotionLevel = MotionLevel.Reduced;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            var held = turn.Angle;
            clock.Step(5);
            Assert.Equal(held, turn.Angle);
            Assert.Equal(1, art.ScaleX);
            settings.MotionLevel = MotionLevel.Off;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            clock.Step(5);
            Assert.Equal(held, turn.Angle);
            Assert.Equal(1, art.ScaleX);
            settings.MotionLevel = MotionLevel.Full;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
            clock.Step(5);
            Assert.True(turn.Angle > held);
            shell.ShowTab("settings"); Settle();
            held = turn.Angle;
            clock.Step(5);
            Assert.Equal(held, turn.Angle);
            Assert.False(ambient.IsTicking);
            shell.ShowTab("chaster"); Settle();
            Assert.Single(registry["chaster"]);
            Assert.True(ambient.IsTicking);
        }
        finally
        {
            shell.Close(); Settle();
            (settings.MotionLevel, settings.PerformanceMode, settings.AutoPerformanceMode) = old;
            UiMotionPreference.OsProbe = oldProbe;
            UiMotionPreference.CacheStamp = long.MinValue;
            BeatLoop.Time = oldTime;
            AmbientFxCanvas.Env.RaiseMotionGateChanged();
        }
    });

    private static void Settle() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
    private sealed class Clock : TimeProvider
    {
        private long _time;
        public override long GetTimestamp() => _time;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public void Step(double seconds) { _time += TimeSpan.FromSeconds(seconds).Ticks; BeatLoop.StepAll(); }
    }
}
