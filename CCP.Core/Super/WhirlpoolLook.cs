using System;

namespace ConditioningControlPanel.SuperCore;

/// <summary>
/// What the Whirlpool wears and when it is drawn again, shared by both heads (WPF
/// <c>WhirlpoolLayer</c>, Avalonia <c>WhirlpoolOverlay</c>): the Loom spirals it melts through
/// (owner, 2026-11-15: a newly rolled one every so often, or the one saved Loom spiral the player
/// pinned; never the player's own spiral file), the pulse phase, the 30 fps repaint gate and the
/// base strength. No WPF, no Skia, no clock of its own: the head steps it once a frame.
/// </summary>
public sealed class WhirlpoolLook
{
    /// <summary>A pool this small draws nothing (and <c>WhirlpoolDraw</c> skips it).</summary>
    public const double NothingSize = 0.004;
    /// <summary>A live pool repaints at most this often; one held at nothing every <see cref="IdleSeconds"/>.</summary>
    public const double FrameSeconds = 1 / 30.0, IdleSeconds = 0.5;

    private string _pinKey = "";
    private double _sinceFrame;

    public WhirlpoolLook(Random rng) { Morph = new LoomMorph(rng); }

    public LoomMorph Morph { get; }
    /// <summary>Runs the Loom spiral's pulse and wobble.</summary>
    public double Phase { get; private set; }

    /// <summary>
    /// One frame: follow the player's pinned spiral (<paramref name="pinKey"/> = a saved Loom slug,
    /// empty = always new ones; <paramref name="find"/> turns a slug into its spec), then, only while
    /// the pool <paramref name="showing"/>, melt and pulse (slower under Gentle visuals). A pool
    /// nobody sees does not burn through spirals.
    /// </summary>
    public void Step(double dt, bool showing, bool gentle, string? pinKey, int newEvery, Func<string, LoomSpec?> find)
    {
        var key = pinKey ?? "";
        if (key != _pinKey)
        {
            _pinKey = key;
            LoomSpec? pin = null;
            try { if (key.Length > 0) pin = find(key); }
            catch (Exception ex) { Serilog.Log.Debug("Whirlpool spiral pick: {E}", ex.Message); }
            Morph.Pin(pin);
        }
        if (!showing) return;
        Morph.Hold = WhirlpoolOptions.ClampHold(newEvery);
        Phase += dt * (gentle ? 0.06 : 0.12);
        Morph.Step(gentle ? dt * 0.6 : dt);
    }

    /// <summary>The repaint gate: true when the surface should draw again. Nothing to draw while the
    /// mouse keeps the pool at nothing, so then only a slow keep-alive.</summary>
    public bool Due(double dt, double poolSize)
    {
        _sinceFrame += Math.Min(dt, 0.1);
        if (_sinceFrame < (poolSize <= NothingSize ? IdleSeconds : FrameSeconds)) return false;
        _sinceFrame = 0;
        return true;
    }

    /// <summary>Does the pool reach into this monitor (device pixels)? A monitor it does not reach has nothing to draw.</summary>
    public static bool Reaches(Whirlpool pool, double left, double top, double right, double bottom)
    {
        double reach = pool.Radius + 2;
        return !(pool.X + reach < left || pool.X - reach > right || pool.Y + reach < top || pool.Y - reach > bottom);
    }

    /// <summary>The base spiral's own opacity setting (percent) as the pool's strength, 0..1.</summary>
    public static double Strength(double spiralOpacityPercent) => Math.Clamp(spiralOpacityPercent / 100.0, 0, 1);

    /// <summary>The saved Loom spiral with this slug, or null.</summary>
    public static LoomSpec? FindSaved(string slug)
    {
        foreach (var (s, _, json) in ConditioningControlPanel.Services.Chaos.DtrhLoomStore.List())
            if (s == slug) return ConditioningControlPanel.Services.Super.LoomSpecJson.Parse(json);
        return null;
    }
}
