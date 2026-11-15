using System;
using System.Collections.Generic;
using System.Diagnostics;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Super;

/// <summary>
/// The app's one <see cref="SuperStage"/> and the two surfaces that draw it: Steam (the pink fog,
/// star on the pink filter) and the Whirlpool (star on the spiral). Every other Super effect talks
/// to <see cref="Stage"/>: it asks what the pool is doing to its things and tells the stage when
/// something of its own goes off. All the rules live in CCP.Core/Super; this class only feeds them
/// the cursor and the clock and switches the surfaces with the overlay engine.
/// Lives in Core: each head seeds <see cref="PostToUi"/>, <see cref="EngineUp"/>,
/// <see cref="CursorProvider"/> and the two surface switches (WPF: compositor layers,
/// Avalonia: overlay windows). Unseeded, nothing shows and nothing steps.
/// Rides the overlay service like the old Creep did, so the engine stop that panic, Lockdown's
/// end and the emergency exit go through takes it down at once; <see cref="Stop"/> is also
/// called straight from the panic stop pass.
/// </summary>
public static class SuperStageHost
{
    public static SuperStage Stage { get; } = new();

    /// <summary>Runs the action on the head's UI thread.</summary>
    public static Action<Action> PostToUi { get; set; } = a => a();
    /// <summary>The overlay engine is running and this head can draw over the desktop.</summary>
    public static Func<bool> EngineUp { get; set; } = () => false;
    /// <summary>Cursor in virtual-desktop device pixels, or null when the head cannot read it.</summary>
    public static Func<(int X, int Y)?> CursorProvider { get; set; } = () => null;
    /// <summary>Show (true) or hide (false) the head's Steam surface. UI thread.</summary>
    public static Action<bool> SteamSurface { get; set; } = _ => { };
    /// <summary>Show (true) or hide (false) the head's Whirlpool surface. UI thread.</summary>
    public static Action<bool> PoolSurface { get; set; } = _ => { };

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
        PostToUi(() =>
        {
            try
            {
                bool engine = EngineUp();
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
                SteamSurface(steam);
                PoolSurface(pool);
                if (steam || pool) _lastStep = 0;
            }
            catch (Exception ex)
            {
                Serilog.Log.Warning(ex, "SuperStage: sync failed");
            }
        });
    }

    /// <summary>Gone now (panic, engine stop). Safe to call any number of times. UI thread.</summary>
    public static void Stop()
    {
        try { SteamSurface(false); PoolSurface(false); }
        catch (Exception ex) { Serilog.Log.Debug("SuperStage: stop: {E}", ex.Message); }
        Stage.SteamOn = false;
        Stage.PoolOn = false;
        Stage.Reset();
        _steamAge = 0;
        _haveCursor = false;
    }

    /// <summary>A surface saw this monitor: remember its pixels-per-DIP for the pool's tuning.</summary>
    public static void NoteScreen(System.Drawing.Rectangle boundsPx, double dip)
    {
        for (int i = 0; i < Screens.Count; i++)
            if (Screens[i].Bounds == boundsPx) { if (Screens[i].Dip != dip) Screens[i] = (boundsPx, dip); return; }
        Screens.Add((boundsPx, dip));
        if (Screens.Count > 16) Screens.RemoveAt(0);
    }

    /// <summary>
    /// One step of the shared stage. Both surfaces call it from their frame; whichever comes first
    /// in a tick does the work and the other finds nothing left to do. UI thread.
    /// </summary>
    public static void Step()
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
        (int X, int Y)? read = null;
        try { read = CursorProvider(); } catch { }
        if (read is { } cur)
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
        var set = CoreSettings.Service?.Current;
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
    public static double SteamLevel(double ageSeconds)
        => SteamOptions.Level(ageSeconds, SteamOptions.CreepSeconds(SteamOptions.Normal));
}
