using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using System.Collections.Generic;
using ConditioningControlPanel.SuperCore;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Super.Draw;
using SkiaSharp;
using CCP.Avalonia.Testing;
using SuperStageHost = ConditioningControlPanel.Avalonia.Platform.Super.SuperStageHost;
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
            Assert.Equal(4, SuperStageOverlay.Windows.Count);
            Assert.All(SuperStageOverlay.Windows, w => { Assert.True(w.IsVisible); Assert.False(w.Focusable); Assert.False(w.ShowActivated); Assert.False(w.IsHitTestVisible); Assert.False(w.ShowInTaskbar); });
            on = false; SuperAccess.RaiseChanged(SuperEffect.Creep);
            Assert.Empty(SuperStageOverlay.Windows);
            on = true; SuperStageHost.Sync();
            Assert.Equal(4, SuperStageOverlay.Windows.Count);
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
        var oldEngine = SuperStageHost.EngineRunning; var oldEffect = SuperStageHost.EffectOn; var oldPointer = SuperStageHost.Pointer;
        var oldComposited = SuperStageOverlay.Composited; var oldConfigure = SuperStageOverlay.Configure;
        var oldRaise = SuperStageOverlay.Raise; var oldEffects = PinkFilterOverlay.EffectWindows;
        bool oldUnder = ConditioningControlPanel.CoreSettings.Current.PinkFilterUnderEffects;
        var raised = new List<Window>(); var effect = new Window();
        try
        {
            SuperStageHost.Stop();
            SuperStageHost.EngineRunning = () => true; SuperStageHost.EffectOn = _ => true; SuperStageHost.Pointer = () => null;
            SuperStageOverlay.ScreensForTests = new[] { new PixelRect(0, 0, 300, 200) };
            SuperStageOverlay.Configure = (_, _) => true;
            SuperStageOverlay.Composited = () => false;
            SuperStageOverlay.Raise = w => { raised.Add(w); return true; };
            PinkFilterOverlay.EffectWindows = () => new[] { effect };
            ConditioningControlPanel.CoreSettings.Current.PinkFilterUnderEffects = true;
            SuperStageHost.Sync(); SuperStageOverlay.Tick(null, EventArgs.Empty);
            Assert.Empty(SuperStageOverlay.Windows);
            Assert.Empty(raised);
        }
        finally
        {
            SuperStageHost.Stop(); SuperStageHost.EngineRunning = oldEngine; SuperStageHost.EffectOn = oldEffect; SuperStageHost.Pointer = oldPointer;
            SuperStageOverlay.Composited = oldComposited; SuperStageOverlay.Configure = oldConfigure; SuperStageOverlay.ScreensForTests = null;
            SuperStageOverlay.Raise = oldRaise; PinkFilterOverlay.EffectWindows = oldEffects;
            ConditioningControlPanel.CoreSettings.Current.PinkFilterUnderEffects = oldUnder; effect.Close();
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

    [Fact]
    public void SuperOnlyStepsBaseAsideAndTimedHoldsRestoreIt() => AvaloniaTestDispatcher.Run(() =>
    {
        var oldEngine = SuperStageHost.EngineRunning; var oldEffect = SuperStageHost.EffectOn;
        var oldClock = SuperStageHost.Clock; var oldPointer = SuperStageHost.Pointer; var oldReplaces = SuperStageOverlay.Replaces;
        var oldProvider = ConditioningControlPanel.CoreSettings.ServiceProvider;
        var service = new ConditioningControlPanel.Services.SettingsService();
        ConditioningControlPanel.CoreSettings.ServiceProvider = () => service;
        var settings = service.Current; var oldMotion = (settings.GentleVisuals, settings.MotionLevel);
        var oldComposited = SuperStageOverlay.Composited; var oldConfigure = SuperStageOverlay.Configure;
        long ticks = 1;
        var pink = PinkFilterOverlay.PayloadHold.Acquire(0.5);
        var spiral = SpiralOverlay.PayloadHold.Acquire(0.5);
        try
        {
            SuperStageHost.Stop(); settings.MotionLevel = ConditioningControlPanel.Models.MotionLevel.Full;
            SuperStageHost.EngineRunning = () => true; SuperStageHost.EffectOn = _ => true;
            SuperStageHost.Clock = () => ticks; SuperStageHost.Pointer = () => null; SuperStageOverlay.Replaces = _ => true;
            SuperStageOverlay.ScreensForTests = new[] { new PixelRect(0, 0, 300, 200) };
            SuperStageOverlay.Composited = () => true; SuperStageOverlay.Configure = (_, _) => true;
            SuperStageHost.Sync();
            void Advance()
            {
                for (int i = 0; i < 22; i++) { ticks += Stopwatch.Frequency / 10; SuperStageOverlay.Tick(null, EventArgs.Empty); }
            }
            Advance();
            Assert.Equal(1, PinkFilterOverlay.SuperVeil);
            Assert.Equal(1, SpiralOverlay.SuperVeil);
            pink(); spiral(); Advance();
            Assert.Equal(0, PinkFilterOverlay.SuperVeil);
            Assert.Equal(0, SpiralOverlay.SuperVeil);
            SuperStageOverlay.Replaces = _ => false; Advance();
            Assert.Equal(1, PinkFilterOverlay.SuperVeil);
            Assert.Equal(1, SpiralOverlay.SuperVeil);
            SuperStageOverlay.Replaces = _ => true; settings.GentleVisuals = true;
            for (int i = 0; i < 5; i++) { ticks += Stopwatch.Frequency / 10; SuperStageOverlay.Tick(null, EventArgs.Empty); }
            Assert.InRange(PinkFilterOverlay.SuperVeil, 0.3, 0.7);
            Assert.InRange(SpiralOverlay.SuperVeil, 0.3, 0.7);
            settings.GentleVisuals = false; Advance();
            SuperStageHost.EffectOn = _ => false; SuperStageHost.Sync();
            Assert.Equal(0, PinkFilterOverlay.SuperVeil);
            SuperStageOverlay.StepRestore(10);
            Assert.Equal(1, PinkFilterOverlay.SuperVeil);
            Assert.Equal(1, SpiralOverlay.SuperVeil);
        }
        finally
        {
            pink(); spiral(); SuperStageHost.Stop();
            SuperStageHost.EngineRunning = oldEngine; SuperStageHost.EffectOn = oldEffect; SuperStageHost.Clock = oldClock; SuperStageHost.Pointer = oldPointer;
            SuperStageOverlay.Replaces = oldReplaces; SuperStageOverlay.Composited = oldComposited;
            SuperStageOverlay.Configure = oldConfigure; SuperStageOverlay.ScreensForTests = null;
            (settings.GentleVisuals, settings.MotionLevel) = oldMotion;
            ConditioningControlPanel.CoreSettings.ServiceProvider = oldProvider;
        }
    });

    [Fact]
    public void ReturningFogWritesWordsAndTheAfterglowLine() => AvaloniaTestDispatcher.Run(() =>
    {
        var set = ConditioningControlPanel.CoreSettings.Current;
        var old = (set.SteamWordsInFog, set.GentleVisuals);
        var oldComposited = SuperStageOverlay.Composited;
        SuperStageHost.Stop();
        try
        {
            set.SteamWordsInFog = true; set.GentleVisuals = false;
            SuperStageHost.Stage.SteamOn = true;
            SuperStageOverlay.Composited = () => false; SuperStageOverlay.Sync();
            SuperStageOverlay.Line = "the line";
            var rect = new PixelRect(0, 0, 400, 240);
            var field = SuperStageHost.Stage.Field(rect.ToString(), 0, 0, 400, 240);
            Array.Fill(field.Fog, 1f); Array.Fill(field.Mem, 1f);
            var words = new SteamWords(7); words.SetPool(new[] { "soft" });
            using var bitmap = new SKBitmap(400, 240);
            using var canvas = new SKCanvas(bitmap);
            using var fog = new SteamDraw(); using var draw = new SteamWordsDraw();
            for (int i = 0; i < 18; i++)
                SuperStageOverlay.RenderMonitor(canvas, fog, rect, new Size(400, 240), 0, 1, false, words, draw, 0.1);
            Assert.True(words.Any);
            Assert.Contains(words.Slots, slot => slot.IsLine && slot.Text == "the line" && slot.Alpha > 0);
            Assert.Contains(words.Slots, slot => !slot.IsLine && slot.Text == "soft" && slot.Alpha > 0);
            Assert.True(draw.Layouts > 0);
            set.SteamWordsInFog = false;
            for (int i = 0; i < 35; i++)
                SuperStageOverlay.RenderMonitor(canvas, fog, rect, new Size(400, 240), 0, 1, false, words, draw, 0.1);
            Assert.False(words.Any);
        }
        finally
        {
            SuperStageHost.Stop(); SuperStageOverlay.Composited = oldComposited;
            (set.SteamWordsInFog, set.GentleVisuals) = old;
        }
    });

    [Fact]
    public void FogIsBelowEffectsAndPoolIsAboveThem() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        bool oldUnder = settings.PinkFilterUnderEffects;
        var oldEngine = SuperStageHost.EngineRunning; var oldEffect = SuperStageHost.EffectOn;
        var oldComposited = SuperStageOverlay.Composited; var oldConfigure = SuperStageOverlay.Configure;
        var oldRaise = SuperStageOverlay.Raise; var oldEffects = PinkFilterOverlay.EffectWindows;
        var order = new List<Window>(); var effect = new Window();
        try
        {
            SuperStageHost.Stop(); settings.PinkFilterUnderEffects = true;
            SuperStageHost.EngineRunning = () => true; SuperStageHost.EffectOn = _ => true;
            SuperStageOverlay.ScreensForTests = new[] { new PixelRect(0, 0, 300, 200) };
            SuperStageOverlay.Composited = () => true; SuperStageOverlay.Configure = (_, _) => true;
            SuperStageOverlay.Raise = w => { order.Add(w); return true; };
            PinkFilterOverlay.EffectWindows = () => new[] { effect };
            SuperStageHost.Sync(); order.Clear(); SuperStageOverlay.Reorder();
            var windows = SuperStageOverlay.Windows;
            Assert.Equal(2, windows.Count);
            Assert.Equal(new Window[] { windows[0], effect, windows[1] }, order);
        }
        finally
        {
            SuperStageHost.Stop(); effect.Close(); settings.PinkFilterUnderEffects = oldUnder;
            SuperStageHost.EngineRunning = oldEngine; SuperStageHost.EffectOn = oldEffect;
            SuperStageOverlay.Composited = oldComposited; SuperStageOverlay.Configure = oldConfigure; SuperStageOverlay.Raise = oldRaise;
            PinkFilterOverlay.EffectWindows = oldEffects; SuperStageOverlay.ScreensForTests = null;
        }
    });

    [Fact]
    public void FinishedAfterglowLineMarksTheFog() => AvaloniaTestDispatcher.Run(() =>
    {
        var settings = ConditioningControlPanel.CoreSettings.Current;
        var oldSettings = (settings.AfterglowLineWords, settings.GlobalTargetMonitor);
        var oldEngine = SuperStageHost.EngineRunning; var oldEffect = SuperStageHost.EffectOn;
        var oldComposited = SuperStageOverlay.Composited; var oldConfigure = SuperStageOverlay.Configure;
        var oldAfterglow = SuperStageOverlay.AfterglowOn;
        try
        {
            SuperStageHost.Stop(); settings.AfterglowLineWords = 2; settings.GlobalTargetMonitor = 1;
            SuperStageHost.EngineRunning = () => true; SuperStageHost.EffectOn = _ => true;
            SuperStageOverlay.AfterglowOn = () => true;
            var first = new PixelRect(0, 0, 300, 200); var rect = new PixelRect(300, 0, 300, 200);
            SuperStageOverlay.ScreensForTests = new[] { first, rect };
            SuperStageOverlay.Composited = () => true; SuperStageOverlay.Configure = (_, _) => true;
            SuperStageHost.Sync();
            var other = SuperStageHost.Stage.Field(first.ToString(), 0, 0, 300, 200);
            var field = SuperStageHost.Stage.Field(rect.ToString(), 300, 0, 300, 200);
            SuperStageOverlay.WordShown("ONE"); SuperStageOverlay.WordShown("TWO");
            SuperStageOverlay.StepAfterglow(1.3);
            Assert.Equal("one two", SuperStageOverlay.Line);
            // The Core field clamps out-of-bounds bands at the edge, so compare their centers.
            Assert.True(field.Mem[(field.H / 2) * field.W + field.W / 2] > 0);
            Assert.Equal(0, other.Mem[(other.H / 2) * other.W + other.W / 2]);
        }
        finally
        {
            SuperStageHost.Stop(); (settings.AfterglowLineWords, settings.GlobalTargetMonitor) = oldSettings;
            SuperStageHost.EngineRunning = oldEngine; SuperStageHost.EffectOn = oldEffect;
            SuperStageOverlay.Composited = oldComposited; SuperStageOverlay.Configure = oldConfigure;
            SuperStageOverlay.AfterglowOn = oldAfterglow; SuperStageOverlay.ScreensForTests = null;
        }
    });
}
