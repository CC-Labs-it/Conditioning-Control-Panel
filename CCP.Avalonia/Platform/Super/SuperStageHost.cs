using System;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Platform.Super;

/// <summary>Owns the shared Super stage and follows the engine and Super switches.</summary>
internal static class SuperStageHost
{
    internal static SuperStage Stage { get; private set; } = new();
    internal static Func<bool> EngineRunning = () => CoreEngine.IsVisualRun;
    internal static Func<SuperEffect, bool> EffectOn = SuperAccess.IsOn;
    private static bool _subscribed;
    private static bool _haveCursor;
    private static double _cursorX, _cursorY, _speed;

    // WPF SuperStageHost.cs:109-155: the drawing host supplies physical pixels and elapsed time.
    // No timer is owned here: a future stage surface must stop calling this when it closes.
    internal static void StepPool(double dt, double x, double y, double dip, double shortSide, int size, bool gentle)
    {
        if (!Stage.PoolOn || dt <= 0) return;
        dt = Math.Min(dt, 0.1);
        if (_haveCursor)
        {
            double dx = x - _cursorX, dy = y - _cursorY;
            double speed = Math.Sqrt(dx * dx + dy * dy) / dt;
            _speed += (speed - _speed) * 0.3;
        }
        _cursorX = x; _cursorY = y; _haveCursor = true;
        Stage.Pool.ReachScale = WhirlpoolOptions.ReachScale(size);
        Stage.Pool.Step(dt, x, y, _speed, dip, shortSide, gentle);
    }

    private static void ResetCursor() { _haveCursor = false; _speed = 0; }

    // Tests must not leave the shared pool or its cursor history changed for the next test.
    internal static void WithIsolatedStageForTests(Action action)
    {
        var stage = Stage;
        var cursor = (_haveCursor, _cursorX, _cursorY, _speed);
        Stage = new SuperStage();
        ResetCursor();
        try { action(); }
        finally
        {
            Stage = stage;
            (_haveCursor, _cursorX, _cursorY, _speed) = cursor;
        }
    }


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
        }
        if (pool != Stage.PoolOn)
        {
            Stage.PoolOn = pool;
            Stage.Pool.Reset();
            ResetCursor();
        }
    }

    private static void OnChanged(SuperEffect effect)
    {
        if (effect is SuperEffect.Creep or SuperEffect.Vortex) Sync();
    }

    internal static void Stop()
    {
        Stage.SteamOn = false;
        Stage.PoolOn = false;
        Stage.Reset();
        ResetCursor();
    }
}
