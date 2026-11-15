using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Helpers;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// The app's one <see cref="SuperStage"/> and the two layers that draw it: Steam (the pink fog,
/// star on the pink filter) and the Whirlpool (star on the spiral). Every other Super effect talks
/// to <see cref="Stage"/>: it asks what the pool is doing to its things and tells the stage when
/// something of its own goes off. All the rules live in CCP.Core/Super; this class only feeds them
/// the cursor and the clock and switches the layers with the overlay engine.
/// Rides the overlay service like the old Creep did, so the engine stop that panic, Lockdown's
/// end and the emergency exit go through takes it down at once; <see cref="Stop"/> is also
/// called straight from the panic stop pass.
/// </summary>
public static class SuperStageHost
{
    public static SuperStage Stage { get; } = new();

    private static SteamLayer? _steam;
    private static WhirlpoolLayer? _pool;
    private static bool _subscribed;
    private static long _lastStep;
    private static double _steamAge;
    private static int _lastX, _lastY;
    private static bool _haveCursor;
    private static double _speedPx;
    private static readonly List<(System.Drawing.Rectangle Bounds, double Dip)> Screens = new();

    /// <summary>Cursor in virtual-desktop device pixels, as of the last step.</summary>
    public static (double X, double Y) Cursor => (_lastX, _lastY);
    /// <summary>Smoothed cursor speed, px/s.</summary>
    public static double CursorSpeedPx => _speedPx;
    /// <summary>Seconds of the last stage step (0 on the frames nothing stepped).</summary>
    public static double LastDt { get; private set; }

    /// <summary>Show or hide to match the engine and the two stars. Any thread.</summary>
    public static void Sync()
    {
        if (!_subscribed)
        {
            _subscribed = true;
            SuperAccess.Changed += e => { if (e is SuperEffect.Creep or SuperEffect.Vortex) Sync(); };
        }
        DispatcherHelper.RunOnUI(() =>
        {
            try
            {
                bool engine = App.Overlay?.IsRunning == true && App.CompositorEnabled && App.Compositor != null;
                bool steam = engine && SuperAccess.IsOn(SuperEffect.Creep);
                bool pool = engine && SuperAccess.IsOn(SuperEffect.Vortex);
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
                if (steam)
                {
                    if (_steam == null) { _steam = new SteamLayer(App.Compositor!); App.Compositor!.RegisterLayer(_steam); }
                    _steam.Show();
                }
                else _steam?.Hide();
                if (pool)
                {
                    if (_pool == null) { _pool = new WhirlpoolLayer(App.Compositor!); App.Compositor!.RegisterLayer(_pool); }
                    _pool.Show();
                }
                else _pool?.Hide();
                if (steam || pool) _lastStep = 0;
            }
            catch (Exception ex)
            {
                App.Logger?.Warning(ex, "SuperStage: sync failed");
            }
        });
    }

    /// <summary>Gone now (panic, engine stop). Safe to call any number of times. UI thread.</summary>
    public static void Stop()
    {
        _steam?.Hide();
        _pool?.Hide();
        Stage.SteamOn = false;
        Stage.PoolOn = false;
        Stage.Reset();
        _steamAge = 0;
        _haveCursor = false;
    }

    /// <summary>A layer saw this monitor: remember its pixels-per-DIP for the pool's tuning.</summary>
    internal static void NoteScreen(System.Drawing.Rectangle boundsPx, double dip)
    {
        for (int i = 0; i < Screens.Count; i++)
            if (Screens[i].Bounds == boundsPx) { if (Screens[i].Dip != dip) Screens[i] = (boundsPx, dip); return; }
        Screens.Add((boundsPx, dip));
        if (Screens.Count > 16) Screens.RemoveAt(0);
    }

    /// <summary>
    /// One step of the shared stage. Both layers call it from their Update; whichever comes first
    /// in an engine tick does the work and the other finds nothing left to do. UI thread.
    /// </summary>
    internal static void Step()
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastStep == 0) { _lastStep = now; LastDt = 0; return; }
        double dt = (now - _lastStep) / (double)Stopwatch.Frequency;
        if (dt < 0.004) { LastDt = 0; return; }
        _lastStep = now;
        dt = Math.Min(dt, 0.1);
        LastDt = dt;

        int fromX = _lastX, fromY = _lastY;
        bool hadCursor = _haveCursor;
        if (GetCursorPos(out var cur))
        {
            if (_haveCursor)
            {
                double sp = Math.Sqrt((double)(cur.X - _lastX) * (cur.X - _lastX) + (double)(cur.Y - _lastY) * (cur.Y - _lastY)) / dt;
                _speedPx += (sp - _speedPx) * 0.3;
            }
            _lastX = cur.X; _lastY = cur.Y; _haveCursor = true;
        }

        double dip = 1, shortSide = 1080;
        foreach (var s in Screens)
            if (s.Bounds.Contains(_lastX, _lastY)) { dip = s.Dip; shortSide = Math.Min(s.Bounds.Width, s.Bounds.Height); break; }

        bool gentle = Safety.GentleVisuals.On;
        // The gold-box options, read live so a slider moves the effect at once.
        var set = App.Settings?.Current;
        Stage.DragsFog = set?.WhirlpoolDragsFog != false;
        Stage.DragsPictures = set?.WhirlpoolDragsPictures != false;
        Stage.DragsBubbles = set?.WhirlpoolDragsBubbles != false;
        Stage.Pool.ReachScale = WhirlpoolOptions.ReachScale(set?.WhirlpoolSize ?? WhirlpoolOptions.SizeDefault);
        if (Stage.PoolOn) Stage.Pool.Step(dt, _lastX, _lastY, _speedPx, dip, shortSide, gentle);

        if (Stage.SteamOn)
        {
            _steamAge += dt;
            Stage.StepSteam(dt, SteamOptions.Level(_steamAge, SteamOptions.CreepSeconds((int)(set?.SuperCreepSpeed ?? CreepSpeed.Normal))), 1.0);
            // The cursor wipes the fog while it moves. At rest under a live pool the pool owns the
            // spot (it keeps its own eye clear), so the two never fight. A pool that leaves the fog
            // alone does not clear its eye either, so then the cursor keeps wiping.
            bool resting = _speedPx / dip < Whirlpool.RestSpeed;
            // A moving cursor does not rub the fog out, it shoves it: the stage gets the stroke
            // (where it was, where it is) and works out the speed.
            if (!(resting && Stage.PoolLive && Stage.DragsFog))
            {
                // 68 at 100%: the ragged rim sits inside the radius, so this reads like the old 58.
                double reach = 68 * dip * Models.AppSettings.ClampSteamWipeSize(set?.SteamWipeSize ?? 100) / 100.0;
                if (hadCursor) Stage.Push(fromX, fromY, _lastX, _lastY, reach, dt, mark: true);
                else Stage.Wipe(_lastX, _lastY, reach, dt, mark: true);
            }
        }
    }

    /// <summary>How far in the fog has crept at the stock creep speed (about four minutes): thin at
    /// the edges at first, the whole screen later. The live step reads the player's Creep speed.</summary>
    internal static double SteamLevel(double ageSeconds)
        => SteamOptions.Level(ageSeconds, SteamOptions.CreepSeconds(SteamOptions.Normal));

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT point);
}
