using System;
using System.Collections.Generic;
using System.Diagnostics;
using Avalonia;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services.Safety;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Platform.Super;

/// <summary>Owns the shared Super stage and follows the engine and Super switches.</summary>
internal static class SuperStageHost
{
    internal static SuperStage Stage { get; } = new();
    internal static Func<bool> EngineRunning = () => CoreEngine.IsVisualRun;
    internal static Func<SuperEffect, bool> EffectOn = SuperAccess.IsOn;
    private static bool _subscribed;
    internal static Func<long> Clock = Stopwatch.GetTimestamp;
    internal static Func<PixelPoint?> Pointer = () => OperatingSystem.IsWindows() ? Win32Overlay.CursorPosition() : OperatingSystem.IsLinux() ? X11Pointer.Read()?.At : null;
    private static long _lastStep;
    private static bool _haveStep, _haveCursor;
    private static int _x, _y;
    private static double _speed, _steamAge;
    private static readonly List<(PixelRect Bounds, double Scale)> Screens = new();
    internal static double LastDt { get; private set; }
    internal static double CursorSpeedPx => _speed;
    internal static (double X, double Y) Cursor => (_x, _y);
    internal static double SteamAge => _steamAge;

    internal static void Sync()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Sync); return; }
        if (!_subscribed)
        {
            _subscribed = true;
            SuperAccess.Changed += OnChanged;
        }
        bool running = EngineRunning();
        bool steam = running && EffectOn(SuperEffect.Creep);
        bool pool = running && EffectOn(SuperEffect.Vortex);
        if (steam != Stage.SteamOn)
        {
            Stage.SteamOn = steam;
            Stage.DropFields();
            _steamAge = 0;
        }
        if (pool != Stage.PoolOn)
        {
            Stage.PoolOn = pool;
            Stage.Pool.Reset();
        }
        if (steam || pool) { _haveStep = false; SuperStageOverlay.Sync(); }
        else SuperStageOverlay.CloseAll();
    }

    private static void OnChanged(SuperEffect effect)
    {
        if (effect is SuperEffect.Creep or SuperEffect.Vortex) Sync();
    }

    internal static void Stop()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(Stop); return; }
        SuperStageOverlay.CloseAll();
        Stage.SteamOn = false;
        Stage.PoolOn = false;
        Stage.Reset();
        _haveStep = _haveCursor = false;
        _lastStep = 0; _steamAge = 0; _speed = 0; LastDt = 0;
        Screens.Clear();
    }

    internal static void ClearScreens() => Screens.Clear();

    internal static void NoteScreen(PixelRect bounds, double scale)
    {
        for (int i = 0; i < Screens.Count; i++)
            if (Screens[i].Bounds == bounds) { Screens[i] = (bounds, scale); return; }
        Screens.Add((bounds, scale));
        if (Screens.Count > 16) Screens.RemoveAt(0);
    }

    internal static void Step()
    {
        long now = Clock();
        if (!_haveStep) { _haveStep = true; _lastStep = now; LastDt = 0; return; }
        double dt = (now - _lastStep) / (double)Stopwatch.Frequency;
        if (dt < 0.004) { LastDt = 0; return; }
        _lastStep = now;
        dt = Math.Min(dt, 0.1);
        LastDt = dt;
        int fromX = _x, fromY = _y;
        bool hadCursor = _haveCursor;
        var cursor = Pointer();
        if (cursor is { } cur)
        {
            if (_haveCursor)
            {
                double dx = cur.X - _x, dy = cur.Y - _y;
                double speed = Math.Sqrt(dx * dx + dy * dy) / dt;
                _speed += (speed - _speed) * 0.3;
            }
            _x = cur.X; _y = cur.Y; _haveCursor = true;
        }
        double dip = 1, shortSide = 1080;
        foreach (var screen in Screens)
            if (screen.Bounds.Contains(new PixelPoint(_x, _y)))
            { dip = screen.Scale > 0 ? screen.Scale : 1; shortSide = Math.Min(screen.Bounds.Width, screen.Bounds.Height); break; }
        var set = CoreSettings.Current;
        Stage.DragsFog = set.WhirlpoolDragsFog;
        Stage.DragsPictures = set.WhirlpoolDragsPictures;
        Stage.DragsBubbles = set.WhirlpoolDragsBubbles;
        Stage.Pool.ReachScale = WhirlpoolOptions.ReachScale(set.WhirlpoolSize);
        if (Stage.PoolOn) Stage.Pool.Step(dt, _x, _y, _speed, dip, shortSide, GentleVisuals.On);
        if (Stage.SteamOn)
        {
            _steamAge += dt;
            Stage.StepSteam(dt, SteamOptions.Level(_steamAge, SteamOptions.CreepSeconds((int)set.SuperCreepSpeed)), 1);
            if (cursor != null && !(_speed / dip < Whirlpool.RestSpeed && Stage.PoolLive && Stage.DragsFog))
            {
                double reach = 68 * dip * Models.AppSettings.ClampSteamWipeSize(set.SteamWipeSize) / 100.0;
                if (hadCursor) Stage.Push(fromX, fromY, _x, _y, reach, dt, true);
                else Stage.Wipe(_x, _y, reach, dt, true);
            }
        }
    }
}
