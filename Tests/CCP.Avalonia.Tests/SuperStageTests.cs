using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super.Draw;
using SkiaSharp;
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

    [Fact]
    public void ClockAndPointerStepPoolAndPushFog() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldClock = SuperStageHost.Clock; var oldPointer = SuperStageHost.Pointer;
        var set = ConditioningControlPanel.CoreSettings.Current;
        var old = (set.WhirlpoolDragsFog, set.SteamWipeSize);
        long ticks = 1; PixelPoint? cursor = new PixelPoint(200, 200);
        try
        {
            SuperStageHost.Stop();
            SuperStageHost.Clock = () => ticks;
            SuperStageHost.Pointer = () => cursor;
            set.WhirlpoolDragsFog = false; set.SteamWipeSize = 100;
            var stage = SuperStageHost.Stage; stage.SteamOn = stage.PoolOn = true;
            SuperStageHost.NoteScreen(new PixelRect(0, 0, 1000, 1000), 2);
            var field = stage.Field("step", 0, 0, 1000, 1000);
            Array.Fill(field.Fog, 1f);
            SuperStageHost.Step();
            ticks += Stopwatch.Frequency / 500; SuperStageHost.Step();
            Assert.Equal(0, SuperStageHost.LastDt);
            ticks += Stopwatch.Frequency / 50; SuperStageHost.Step();
            Assert.True(stage.Pool.Size > 0);
            Assert.Equal(2, stage.Pool.Dip);
            Assert.True(field.Mem.Any(x => x > 0));
            Assert.True(field.Mem[32 * field.W + 46] > 0); // 87.5 px from cursor, inside 136 px reach
            Assert.Equal(0, field.Mem[32 * field.W + 58]); // outside the reach
            cursor = new PixelPoint(300, 200);
            ticks += Stopwatch.Frequency; SuperStageHost.Step();
            Assert.Equal(0.1, SuperStageHost.LastDt, 5);
            Assert.Equal(300, SuperStageHost.CursorSpeedPx, 4);
            Assert.True(field.Vx.Any(x => Math.Abs(x) > 0.01));
            cursor = null;
            double age = SuperStageHost.SteamAge;
            ticks += Stopwatch.Frequency / 10; SuperStageHost.Step();
            Assert.True(SuperStageHost.SteamAge > age);
        }
        finally
        {
            SuperStageHost.Stop(); SuperStageHost.Clock = oldClock; SuperStageHost.Pointer = oldPointer;
            (set.WhirlpoolDragsFog, set.SteamWipeSize) = old;
        }
    });

    [Fact]
    public void SurfacesFollowSwitchesAndPanicClosesEveryMonitor() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEngine = SuperStageHost.EngineRunning; var oldEffect = SuperStageHost.EffectOn;
        var oldComposited = SuperStageOverlay.Composited; var oldConfigure = SuperStageOverlay.Configure;
        bool running = false, on = true;
        SuperStageOverlay[] opened = Array.Empty<SuperStageOverlay>();
        try
        {
            SuperStageHost.Stop();
            SuperStageHost.EngineRunning = () => running;
            SuperStageHost.EffectOn = _ => on;
            SuperStageOverlay.ScreensForTests = new[] { new PixelRect(0, 0, 300, 200), new PixelRect(300, 0, 300, 200) };
            SuperStageOverlay.Configure = (_, _) => false;
            SuperStageOverlay.Composited = () => true;
            running = true; SuperStageHost.Sync();
            Assert.Empty(SuperStageOverlay.Windows);
            SuperStageOverlay.Configure = (_, _) => true; SuperStageHost.Sync();
            Assert.Equal(2, SuperStageOverlay.Windows.Count);
            Assert.All(SuperStageOverlay.Windows, w => { Assert.True(w.IsVisible); Assert.False(w.Focusable); Assert.False(w.ShowActivated); Assert.False(w.IsHitTestVisible); Assert.False(w.ShowInTaskbar); });
            on = false; SuperAccess.RaiseChanged(SuperEffect.Creep);
            Assert.Empty(SuperStageOverlay.Windows);
            on = true; SuperStageHost.Sync();
            Assert.Equal(2, SuperStageOverlay.Windows.Count);
            opened = SuperStageOverlay.Windows.ToArray();
            PanicSurfaces.StopAll("super-stage-window-test");
            Assert.Empty(SuperStageOverlay.Windows);
            Assert.All(opened, w => Assert.False(w.IsVisible));
            Assert.False(SuperStageHost.Stage.SteamOn);
            running = false; SuperStageHost.Sync();
            Assert.Empty(SuperStageOverlay.Windows);
        }
        finally
        {
            SuperStageHost.Stop();
            foreach (var w in opened) if (w.IsVisible) w.Close();
            SuperStageHost.EngineRunning = oldEngine; SuperStageHost.EffectOn = oldEffect;
            SuperStageOverlay.Composited = oldComposited; SuperStageOverlay.Configure = oldConfigure; SuperStageOverlay.ScreensForTests = null;
        }
    });

    [Fact]
    public void RefusesWhenTheDesktopIsNotComposited() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEngine = SuperStageHost.EngineRunning; var oldEffect = SuperStageHost.EffectOn;
        var oldComposited = SuperStageOverlay.Composited; var oldConfigure = SuperStageOverlay.Configure;
        try
        {
            SuperStageHost.Stop();
            SuperStageHost.EngineRunning = () => true; SuperStageHost.EffectOn = _ => true;
            SuperStageOverlay.ScreensForTests = new[] { new PixelRect(0, 0, 300, 200) };
            SuperStageOverlay.Configure = (_, _) => true;
            SuperStageOverlay.Composited = () => false;
            SuperStageHost.Sync();
            Assert.Empty(SuperStageOverlay.Windows);
        }
        finally
        {
            SuperStageHost.Stop(); SuperStageHost.EngineRunning = oldEngine; SuperStageHost.EffectOn = oldEffect;
            SuperStageOverlay.Composited = oldComposited; SuperStageOverlay.Configure = oldConfigure; SuperStageOverlay.ScreensForTests = null;
        }
    });

    [Fact]
    public void FogRendersPixelsOnMonitor() => AvaloniaTestDispatcher.Run(() =>
    {
        SuperStageHost.Stop();
        var oldComposited = SuperStageOverlay.Composited;
        try
        {
            var rect = new PixelRect(0, 0, 160, 100);
            SuperStageHost.Stage.SteamOn = true;
            SuperStageOverlay.Composited = () => false; SuperStageOverlay.Sync();
            var field = SuperStageHost.Stage.Field(rect.ToString(), 0, 0, 160, 100);
            Array.Fill(field.Fog, 1f);
            using var bitmap = new SKBitmap(160, 100);
            using var canvas = new SKCanvas(bitmap);
            using var fog = new SteamDraw();
            canvas.Clear(SKColors.Transparent);
            SuperStageOverlay.RenderMonitor(canvas, fog, rect, new Size(160, 100), 0, 1);
            Assert.True(bitmap.Pixels.Any(p => p.Alpha > 0));
        }
        finally { SuperStageHost.Stop(); SuperStageOverlay.Composited = oldComposited; }
    });

    [Fact]
    public void PoolRendersPixelsOnItsTargetMonitor() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var old = (settings.SpiralTargetMonitor, settings.GentleVisuals);
        var oldComposited = SuperStageOverlay.Composited;
        try
        {
            SuperStageHost.Stop();
            settings.SpiralTargetMonitor = 0; settings.GentleVisuals = false;
            SuperStageHost.Stage.PoolOn = true;
            SuperStageOverlay.Composited = () => false; SuperStageOverlay.Sync();
            SuperStageHost.Stage.Pool.Step(3, 200, 200, 0, 1, 400);
            using var bitmap = new SKBitmap(400, 400);
            using var canvas = new SKCanvas(bitmap);
            using var fog = new SteamDraw();
            canvas.Clear(SKColors.Transparent);
            SuperStageOverlay.RenderMonitor(canvas, fog, new PixelRect(0, 0, 400, 400), new Size(400, 400), 0, 1);
            Assert.True(bitmap.Pixels.Any(p => p.Alpha > 0));
        }
        finally
        {
            SuperStageHost.Stop(); SuperStageOverlay.Composited = oldComposited;
            (settings.SpiralTargetMonitor, settings.GentleVisuals) = old;
        }
    });
}
