using System;
using System.Diagnostics;
using System.Threading;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.SuperCore;

namespace ConditioningControlPanel.Services.Prizes;

/// <summary>
/// The worn fog look as the compositor draws it. Colour one is ALWAYS the filter's own colour (the user's
/// pick, else the mod's, else hot pink): the shop sells no fog colours (owner 2026-11-17). With Two-tone on
/// (<see cref="WornLooks.MixOn"/>) the second colour from slot <c>fog2</c> moves through it; a second colour
/// too close to the user's own steps on to the next palette colour (<see cref="FogLooks.SecondRow"/>).
/// Switching two-tone on or off, or a new second colour, cross-fades (a whole-screen colour never jumps).
/// Reads only <see cref="WornLooks"/> and follows <see cref="WornLooks.Changed"/>, which may fire on any
/// thread: the state is one immutable snapshot swapped whole, and every answer is computed from it and
/// the clock, so any thread may ask. Nothing worn = exactly today's flat colour.
/// </summary>
internal static class WornFog
{
    /// <summary>What to paint now: the main colour (always the filter's own), the second, and whether the two-tone path draws.</summary>
    public readonly record struct FogShown((byte R, byte G, byte B) Main, (byte R, byte G, byte B) Second, bool TwoTone);

    /// <summary>The picked second colour (null = two-tone off) and the fade from what was shown.</summary>
    private sealed record Look(string? Second, (byte R, byte G, byte B)? FromSecond, bool FromMix, double T0, long Stamp);

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
        _look = new Look(SecondWorn(), null, false, 0, 0);
        WornLooks.Changed += OnChanged;
    }

    /// <summary>The user's own second colour (#RRGGBB) or nothing. Tests swap it.</summary>
    internal static Func<string?> OwnSecond = () => { try { return CoreSettings.Current.FogSecondOwnColor; } catch { return null; } };

    /// <summary>The picked second colour while two-tone is on, else null: the user's own (#RRGGBB) when set,
    /// else the palette row.</summary>
    private static string? SecondWorn()
    {
        if (Read(WornLooks.Mix) == null) return null;
        var own = OwnSecond();
        return FogLooks.TryHex(own) != null ? own : Read(WornLooks.Fog2) ?? FogLooks.DefaultSecond;
    }

    private static void OnChanged(string slot)
    {
        if (slot is WornLooks.Fog2 or WornLooks.Mix) Retarget();
    }

    /// <summary>Read the worn rows again; a change starts a cross-fade from what is shown now.</summary>
    internal static void Retarget()
    {
        double now = Clock();
        var cur = Volatile.Read(ref _look);
        var second = SecondWorn();
        if (second == cur.Second) return;
        var shown = At(cur, _lastOwn, now);
        Volatile.Write(ref _look, new Look(second, shown.Second, shown.TwoTone, now, Interlocked.Increment(ref _stamp)));
    }

    /// <summary>The colours to paint over the filter's <paramref name="own"/> colour, right now.</summary>
    public static FogShown Shown((byte R, byte G, byte B) own)
    {
        _lastOwn = own;
        return At(Volatile.Read(ref _look), own, Clock());
    }

    /// <summary>Bumps on every change of the worn look: a layer compares it to know it must repaint.</summary>
    public static long Stamp => Volatile.Read(ref _look).Stamp;

    /// <summary>True while a cross-fade is under way (a layer repaints every frame until it ends).</summary>
    public static bool Easing
    {
        get
        {
            var l = Volatile.Read(ref _look);
            double dt = Clock() - l.T0;
            return l.FromSecond != null && dt >= 0 && dt < EaseSeconds();   // a clock behind the fade's start means it is over
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
        var toSecond = look.Second != null ? FogLooks.TryHex(look.Second) ?? FogLooks.SecondColour(look.Second, own) : own;
        if (look.FromSecond is not { } fromSecond) return new FogShown(own, toSecond, look.Second != null);
        // A clock that reads earlier than the fade's start (a swapped test clock) is a finished fade, never a stuck one.
        double dt = now - look.T0, k = dt < 0 ? 1 : FogLooks.EaseAt(dt, EaseSeconds());
        return new FogShown(own, FogLooks.Lerp(fromSecond, toSecond, k), look.Second != null || (k < 1 && look.FromMix));
    }

    private static double EaseSeconds()
    {
        bool gentle = false; double fade = 0;
        try { gentle = Safety.GentleVisuals.On; fade = Safety.GentleVisuals.FadeSeconds; } catch { }
        return FogLooks.EaseSeconds(gentle, fade);
    }
}
