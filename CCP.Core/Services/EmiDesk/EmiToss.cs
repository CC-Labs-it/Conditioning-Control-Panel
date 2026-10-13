using System;

namespace ConditioningControlPanel.Services.EmiDesk;

/// <summary>
/// The pure half of the toss (WPF <c>Windows/EmiDesk/EmiDeskWindow.Toss.cs</c>): the zone split,
/// the campus numbers and the landing squash curve, so both heads land her the same way.
/// </summary>
public static class EmiTossRules
{
    /// <summary>A fling is at least this fast (DIP/s). CAMPUS <c>FLING_SPEED</c> 1.5 px/ms.</summary>
    public const double FlingSpeedDip = 1500.0;

    /// <summary>...held at least this long. CAMPUS <c>FLING_MS</c>.</summary>
    public const int FlingHoldMs = 120;

    /// <summary>A let-go this soon after she was last flying still counts as a throw.</summary>
    public const int FlingGraceMs = 150;

    /// <summary>The ordinary landing: emi.css <c>bounce</c>, scale(1.08, .94) at 40 %, 340 ms.</summary>
    public const double LandSquashX = 1.08, LandSquashY = 0.94;
    public const int LandMs = 340;

    /// <summary>The thrown landing: emi.css <c>thud</c>, a stretch on the way in, then scale(1.10, .88), 280 ms.</summary>
    public const double ThudSquashX = 1.10, ThudSquashY = 0.88;
    public const int ThudMs = 280;

    /// <summary>The pickup face.</summary>
    public const string PickupFace = "o_o";

    /// <summary>Where the landing squash pivots: her feet, 0.45 of her height below BodyRoot's
    /// 0.55 transform origin.</summary>
    public const double FeetBelowOrigin = 0.45;

    /// <summary>top / mid / bottom third of a span, by the point's y. Degenerate spans are "mid".</summary>
    public static string ZoneRow(double y, double top, double height)
    {
        if (height <= 0) return "mid";
        double f = (y - top) / height;
        return f < 1.0 / 3.0 ? "top" : f >= 2.0 / 3.0 ? "bottom" : "mid";
    }

    /// <summary>
    /// The landing squash at progress <paramref name="p"/> (0..1): WPF's key frames, segment by
    /// segment, with WPF's <c>CubicEase</c> EaseOut into the squash and <c>ElasticEase</c> EaseOut
    /// (Oscillations 1, Springiness 4) back to rest. Starts and ends at (1, 1).
    /// </summary>
    public static (double X, double Y) LandingScale(bool thrown, double p)
    {
        p = Math.Clamp(p, 0, 1);
        if (thrown)
        {
            if (p <= 0.20) return Seg(1, 1, 0.95, 1.06, CubicOut(p / 0.20));
            if (p <= 0.55) return Seg(0.95, 1.06, ThudSquashX, ThudSquashY, CubicOut((p - 0.20) / 0.35));
            return Seg(ThudSquashX, ThudSquashY, 1, 1, ElasticOut((p - 0.55) / 0.45));
        }
        if (p <= 0.40) return Seg(1, 1, LandSquashX, LandSquashY, CubicOut(p / 0.40));
        return Seg(LandSquashX, LandSquashY, 1, 1, ElasticOut((p - 0.40) / 0.60));
    }

    private static (double, double) Seg(double x0, double y0, double x1, double y1, double e)
        => (x0 + (x1 - x0) * e, y0 + (y1 - y0) * e);

    /// <summary>WPF CubicEase, EaseOut.</summary>
    internal static double CubicOut(double t) => 1 - Math.Pow(1 - t, 3);

    /// <summary>WPF ElasticEase(Oscillations 1, Springiness 4), EaseOut: 1 - EaseIn(1 - t).</summary>
    internal static double ElasticOut(double t)
    {
        double u = 1 - t;
        double expo = (Math.Exp(4 * u) - 1) / (Math.Exp(4) - 1);
        return 1 - expo * Math.Sin((Math.PI * 2 + Math.PI * 0.5) * u);
    }
}

/// <summary>
/// Throw detection for one drag (WPF Toss.cs <c>OnPickedUp</c> / <c>NoteTossFrame</c> /
/// <c>OnPutDown</c>): fast enough for long enough, and a let-go inside the grace still counts.
/// </summary>
public sealed class EmiFlingDetector
{
    private double _lastY, _vy;
    private DateTime _fastSince = DateTime.MinValue;
    private DateTime _flingUntil = DateTime.MinValue;

    /// <summary>The press just became a real drag; <paramref name="y"/> is her top in DIPs.</summary>
    public void PickUp(double y)
    {
        _lastY = y; _vy = 0;
        _fastSince = DateTime.MinValue;
        _flingUntil = DateTime.MinValue;
    }

    /// <summary>One wobble frame: her top <paramref name="y"/> (DIPs), the frame's clamped
    /// <paramref name="dt"/> (s), the smoothed horizontal velocity and the wobble's low-pass keep.</summary>
    public void Frame(double y, double dt, double vx, double keep, DateTime now)
    {
        double raw = (y - _lastY) / dt;
        _lastY = y;
        _vy = _vy * keep + raw * (1.0 - keep);
        double speed = Math.Sqrt(vx * vx + _vy * _vy);
        if (speed >= EmiTossRules.FlingSpeedDip)
        {
            if (_fastSince == DateTime.MinValue) _fastSince = now;
            if ((now - _fastSince).TotalMilliseconds >= EmiTossRules.FlingHoldMs)
                _flingUntil = now.AddMilliseconds(EmiTossRules.FlingGraceMs);
        }
        else
        {
            _fastSince = DateTime.MinValue;
        }
    }

    /// <summary>The drag ended: true when it was a throw. Resets for the next drag.</summary>
    public bool PutDown(DateTime now)
    {
        bool flung = now < _flingUntil;
        _flingUntil = DateTime.MinValue;
        _fastSince = DateTime.MinValue;
        return flung;
    }
}
