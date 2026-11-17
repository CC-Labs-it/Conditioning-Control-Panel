using System;
using System.Diagnostics;
using System.Threading;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// The worn fog colour as the compositor draws it: the pink filter's tint and Steam ask
/// <see cref="Over"/> with the filter's own colour and get the worn one back, cross-faded when the
/// look changes (a whole-screen colour never jumps). Reads only <see cref="WornLooks"/> and follows
/// <see cref="WornLooks.Changed"/>, which may fire on any thread: the state is one immutable
/// snapshot swapped whole, and every answer is computed from it and the clock, so any thread may ask.
/// Nothing worn = the free Pink row = the filter's own colour, exactly today's look.
/// </summary>
internal static class WornFog
{
    private sealed record Look(string? Main, (byte R, byte G, byte B)? FromMain, double T0, long Stamp);

    private static long _stamp;
    private static Look _look;
    private static (byte R, byte G, byte B) _lastOwn = (255, 105, 180);

    /// <summary>Reads a worn slot. Tests swap it (the shared WornLooks.Source stays untouched).</summary>
    internal static Func<string, string?> Read = WornLooks.Get;

    /// <summary>Seconds on a monotonic clock. Tests swap it.</summary>
    internal static Func<double> Clock = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    static WornFog()
    {
        _look = new Look(Read(WornLooks.Fog), null, 0, 0);
        WornLooks.Changed += OnChanged;
    }

    private static void OnChanged(string slot)
    {
        if (slot is WornLooks.Fog or WornLooks.Fog2 or WornLooks.Mix) Retarget();
    }

    /// <summary>Read the worn rows again; a changed colour starts a cross-fade from what is shown now.</summary>
    internal static void Retarget()
    {
        double now = Clock();
        var cur = Volatile.Read(ref _look);
        var shown = MainAt(cur, _lastOwn, now);
        var next = Read(WornLooks.Fog);
        if (next == cur.Main) return;
        Volatile.Write(ref _look, new Look(next, shown, now, Interlocked.Increment(ref _stamp)));
    }

    /// <summary>The colour to paint over the filter's <paramref name="own"/> colour, right now.</summary>
    public static (byte R, byte G, byte B) Over((byte R, byte G, byte B) own)
    {
        _lastOwn = own;
        return MainAt(Volatile.Read(ref _look), own, Clock());
    }

    /// <summary>The colour the fog settles on (no cross-fade): for the window fallback, which repaints only on a change.</summary>
    public static (byte R, byte G, byte B) Target((byte R, byte G, byte B) own) => FogLooks.Wear(Volatile.Read(ref _look).Main, own);

    /// <summary>Bumps on every change of the worn colour: a layer compares it to know it must repaint.</summary>
    public static long Stamp => Volatile.Read(ref _look).Stamp;

    /// <summary>True while a cross-fade is under way (a layer repaints every frame until it ends).</summary>
    public static bool Easing
    {
        get
        {
            var l = Volatile.Read(ref _look);
            return l.FromMain != null && Clock() - l.T0 < EaseSeconds();
        }
    }

    private static (byte R, byte G, byte B) MainAt(Look look, (byte R, byte G, byte B) own, double now)
    {
        var to = FogLooks.Wear(look.Main, own);
        if (look.FromMain is not { } from) return to;
        return FogLooks.Lerp(from, to, FogLooks.EaseAt(now - look.T0, EaseSeconds()));
    }

    private static double EaseSeconds()
    {
        bool gentle = false; double fade = 0;
        try { gentle = Safety.GentleVisuals.On; fade = Safety.GentleVisuals.FadeSeconds; } catch { }
        return FogLooks.EaseSeconds(gentle, fade);
    }
}
