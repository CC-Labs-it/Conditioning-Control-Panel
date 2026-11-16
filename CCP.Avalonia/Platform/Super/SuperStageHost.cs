using System;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Avalonia.Platform.Super;

/// <summary>Owns the shared Super stage and follows the engine and Super switches.</summary>
internal static class SuperStageHost
{
    internal static SuperStage Stage { get; set; } = new();
    internal static Func<bool> EngineRunning = () => CoreEngine.IsVisualRun;
    internal static Func<SuperEffect, bool> EffectOn = SuperAccess.IsOn;
    private static bool _subscribed;
    private static DispatcherTimer? _timer;
    private static long _lastStep;
    private static PixelPoint? _lastCursor;
    private static double _speed;
    internal static TimeProvider Clock = TimeProvider.System;
    internal static Func<(PixelPoint Position, double Dip, double ShortSide)?> Pointer = ReadPointer;
    internal static bool IsStepping => _timer?.IsEnabled == true;
    /// <summary>True while a surface draws the pool. WPF steps the stage from WhirlpoolLayer.Update
    /// (WhirlpoolLayer.cs:70) and only with the compositor up (SuperStageHost.cs:54), so with nothing
    /// drawing it the pool stays dormant here too. The Whirlpool surface sets it and calls Sync.</summary>
    internal static Func<bool> PoolDrawn = () => false;

    private static readonly bool Wayland = OperatingSystem.IsLinux()
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"));

    private static (PixelPoint Position, double Dip, double ShortSide)? ReadPointer()
    {
        // XWayland keeps the last X position while the pointer is over a native Wayland window:
        // only a root child (an X surface under the pointer) makes the position current.
        PixelPoint? point;
        if (OperatingSystem.IsWindows()) point = X11Pointer.Read()?.At;
        else { point = X11Pointer.Read(out var overX)?.At; if (Wayland && !overX) point = null; }
        if (point is not { } p) return null;
        var window = (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;
        var screen = window?.Screens.ScreenFromPoint(p);
        return (p, screen?.Scaling ?? 1, screen == null ? 1080 : Math.Min(screen.Bounds.Width, screen.Bounds.Height));
    }

    // WPF SuperStageHost.cs:114-147: cursor speed and real elapsed time, capped at 100 ms.
    internal static void Step()
    {
        if (!Stage.PoolOn) return;
        var now = Clock.GetTimestamp();
        var dt = Math.Min(0.1, Clock.GetElapsedTime(_lastStep, now).TotalSeconds);
        if (dt <= 0) return;
        _lastStep = now;
        var sample = Pointer();
        if (sample is not { } at) { _lastCursor = null; _speed = 0; Stage.Pool.Reset(); return; }   // no position: no pool
        if (_lastCursor is { } before)
        {
            double dx = at.Position.X - before.X, dy = at.Position.Y - before.Y;
            _speed += (Math.Sqrt(dx * dx + dy * dy) / dt - _speed) * 0.3;
        }
        _lastCursor = at.Position;
        var s = CoreSettings.Current;
        Stage.DragsFog = s.WhirlpoolDragsFog;
        Stage.DragsPictures = s.WhirlpoolDragsPictures;
        Stage.DragsBubbles = s.WhirlpoolDragsBubbles;
        Stage.Pool.ReachScale = WhirlpoolOptions.ReachScale(s.WhirlpoolSize);
        Stage.Pool.Step(dt, at.Position.X, at.Position.Y, _speed, at.Dip, at.ShortSide, s.GentleVisuals);
    }

    private static void StopClock()
    {
        _timer?.Stop(); _timer = null;
        _lastCursor = null; _speed = 0;
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
        }
        if (pool && PoolDrawn() && _timer == null)
        {
            _lastStep = Clock.GetTimestamp();
            _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => Step());
            _timer.Start();
        }
        else if (!pool || !PoolDrawn()) StopClock();
    }

    // Tests: the stage, its clock and cursor history; the returned action puts them back.
    internal static Action SnapshotForTests()
    {
        var saved = (Stage, _timer, _lastStep, _lastCursor, _speed);
        return () =>
        {
            if (!ReferenceEquals(_timer, saved._timer)) { _timer?.Stop(); saved._timer?.Start(); }
            (Stage, _timer, _lastStep, _lastCursor, _speed) = saved;
        };
    }

    private static void OnChanged(SuperEffect effect)
    {
        if (effect is SuperEffect.Creep or SuperEffect.Vortex) Sync();
    }

    internal static void Stop()
    {
        StopClock();
        Stage.SteamOn = false;
        Stage.PoolOn = false;
        Stage.Reset();
    }
}
