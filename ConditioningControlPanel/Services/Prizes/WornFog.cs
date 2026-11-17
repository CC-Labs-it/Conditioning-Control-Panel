using System;
using System.Diagnostics;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// The worn fog look as the compositor draws it: the pink filter's tint and Steam ask
/// <see cref="Shown"/> (or <see cref="Over"/>) with the filter's own colour and get the worn colour,
/// and with Two-tone on (<see cref="WornLooks.MixOn"/>) the second colour too, cross-faded when the look
/// changes (a whole-screen colour never jumps, and the mix grows in and out). Reads only
/// <see cref="WornLooks"/> and follows <see cref="WornLooks.Changed"/>, which may fire on any thread:
/// the state is one immutable snapshot swapped whole, and every answer is computed from it and the
/// clock, so any thread may ask. Nothing worn = the free Pink row, no mix = exactly today's look.
/// </summary>
internal static class WornFog
{
    /// <summary>What to paint now: the main colour, the second, and whether the two-tone path draws.</summary>
    public readonly record struct FogShown((byte R, byte G, byte B) Main, (byte R, byte G, byte B) Second, bool TwoTone);

    private sealed record Look(string? Main, string? Second, (byte R, byte G, byte B)? FromMain,
        (byte R, byte G, byte B) FromSecond, bool FromMix, double T0, long Stamp);

    private static long _stamp;
    private static Look _look;
    private static (byte R, byte G, byte B) _lastOwn = (255, 105, 180);
    private static double _mixT = 9, _mixClock = -1;

    /// <summary>Reads a worn slot. Tests swap it (the shared WornLooks.Source stays untouched).</summary>
    internal static Func<string, string?> Read = WornLooks.Get;

    /// <summary>Seconds on a monotonic clock. Tests swap it.</summary>
    internal static Func<double> Clock = () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    static WornFog()
    {
        var (main, second) = Rows();
        _look = new Look(main, second, null, default, false, 0, 0);
        WornLooks.Changed += OnChanged;
    }

    private static (string? Main, string? Second) Rows()
    {
        var main = Read(WornLooks.Fog);
        var second = Read(WornLooks.Mix) != null ? FogLooks.SecondRow(main, Read(WornLooks.Fog2)) : null;
        return (main, second);
    }

    private static void OnChanged(string slot)
    {
        if (slot is WornLooks.Fog or WornLooks.Fog2 or WornLooks.Mix) Retarget();
    }

    /// <summary>Read the worn rows again; a change starts a cross-fade from what is shown now.</summary>
    internal static void Retarget()
    {
        double now = Clock();
        var cur = Volatile.Read(ref _look);
        var (main, second) = Rows();
        if (main == cur.Main && second == cur.Second) return;
        var shown = At(cur, _lastOwn, now);
        Volatile.Write(ref _look, new Look(main, second, shown.Main, shown.Second, shown.TwoTone, now, Interlocked.Increment(ref _stamp)));
    }

    /// <summary>The colours to paint over the filter's <paramref name="own"/> colour, right now.</summary>
    public static FogShown Shown((byte R, byte G, byte B) own)
    {
        _lastOwn = own;
        return At(Volatile.Read(ref _look), own, Clock());
    }

    /// <summary>The main colour only (the flat tint's colour while two-tone is off).</summary>
    public static (byte R, byte G, byte B) Over((byte R, byte G, byte B) own) => Shown(own).Main;

    /// <summary>The colour the fog settles on (no cross-fade, no mix): for the window fallback, which repaints only on a change.</summary>
    public static (byte R, byte G, byte B) Target((byte R, byte G, byte B) own) => FogLooks.Wear(Volatile.Read(ref _look).Main, own);

    /// <summary>Bumps on every change of the worn look: a layer compares it to know it must repaint.</summary>
    public static long Stamp => Volatile.Read(ref _look).Stamp;

    /// <summary>True while a cross-fade is under way (a layer repaints every frame until it ends).</summary>
    public static bool Easing
    {
        get
        {
            var l = Volatile.Read(ref _look);
            double dt = Clock() - l.T0;
            return l.FromMain != null && dt >= 0 && dt < EaseSeconds();   // a clock behind the fade's start means it is over
        }
    }

    /// <summary>True while two-tone draws (on, or still fading out).</summary>
    public static bool TwoTone => Shown(_lastOwn).TwoTone;

    /// <summary>The mix's own clock, shared by every layer so the tint and Steam agree on where the
    /// colours are. Advances by real time times <paramref name="speed"/> (motion Off = 0: a still frame,
    /// Reduced = 0.5); asking twice in one frame does not advance it twice. Compositor thread.</summary>
    public static double MixTime(double speed)
    {
        double now = Clock();
        if (_mixClock >= 0) _mixT += Math.Clamp(now - _mixClock, 0, 0.1) * Math.Max(0, speed);
        _mixClock = now;
        return _mixT;
    }

    /// <summary>The mix speed for a motion level: Off = still, Reduced (and Gentle) = half.</summary>
    public static double MixSpeed(MotionLevel level) => level switch
    {
        MotionLevel.Off => 0,
        MotionLevel.Reduced => 0.5,
        _ => 1,
    };

    private static FogShown At(Look look, (byte R, byte G, byte B) own, double now)
    {
        var toMain = FogLooks.Wear(look.Main, own);
        var toSecond = look.Second != null ? FogLooks.Wear(look.Second, own) : toMain;
        if (look.FromMain is not { } fromMain) return new FogShown(toMain, toSecond, look.Second != null);
        // A clock that reads earlier than the fade's start (a swapped test clock) is a finished fade, never a stuck one.
        double dt = now - look.T0, k = dt < 0 ? 1 : FogLooks.EaseAt(dt, EaseSeconds());
        return new FogShown(FogLooks.Lerp(fromMain, toMain, k), FogLooks.Lerp(look.FromSecond, toSecond, k),
            look.Second != null || (k < 1 && look.FromMix));
    }

    private static double EaseSeconds()
    {
        bool gentle = false; double fade = 0;
        try { gentle = Safety.GentleVisuals.On; fade = Safety.GentleVisuals.FadeSeconds; } catch { }
        return FogLooks.EaseSeconds(gentle, fade);
    }
}
