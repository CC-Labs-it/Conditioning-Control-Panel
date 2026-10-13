using System;
using System.Collections.Generic;
using System.Linq;

namespace ConditioningControlPanel.Services.Chaos;

/// <summary>One flying DVD logo (WPF ChaosDvdOverlay per-launch state). Coordinates are DIPs in the
/// flock's area; <see cref="W"/>/<see cref="H"/> are the measured label box.</summary>
public sealed class ChaosDvdLogo
{
    public string Text = "";
    public double X, Y, W, H, Vx, Vy;
    public double FontScale = 1.0;
    public double RemainingSec;
    public int HueIndex;
    public bool IsThought;
    public int SplitBouncesLeft;
    public double Age;          // seconds since launch (fade-in)
    public double FadeOutSec = -1;   // >= 0 while retiring

    /// <summary>WPF: 0 -> 0.85 over 180 ms on launch, current -> 0 over 240 ms on retire.</summary>
    public double Opacity => FadeOutSec >= 0
        ? Math.Max(0, ChaosDvdFlock.PeakOpacity * (1 - FadeOutSec / 0.24))
        : ChaosDvdFlock.PeakOpacity * Math.Min(1, Age / 0.18);
}

/// <summary>What one frame did, for the head to draw and play.</summary>
public readonly record struct ChaosDvdFrame(
    IReadOnlyList<ChaosDvdLogo> Spawned, IReadOnlyList<ChaosDvdLogo> Recolored,
    IReadOnlyList<ChaosDvdLogo> Removed, bool BounceCue, bool LaunchCue);

/// <summary>
/// PORTED from ConditioningControlPanel/Chaos/ChaosDvdOverlay.cs: the Porn DVD toy / Intrusive
/// Thoughts logo maths, head-free. Launch (:94), Begin (:180), StepAll (:274), StepOne (:327) and
/// FadeOutAndRetire (:405), minus drawing: a DVD-screensaver label drifts across the area, bounces
/// off the edges with one hue step per bounce, and Casting Couch splits it on wall hits.
/// The Spanker smack (Redirect) and the rabbit split (SplitInTwo) are not here: their inputs (the
/// Spanker boon, chaos bubble darters) are not ported.
/// </summary>
public sealed class ChaosDvdFlock
{
    public const double BaseFont = 46;
    public const double BaseSpeed = 230;      // DIPs per second before the rank speed multiplier
    public const double PeakOpacity = 0.85;
    public const int MaxThoughts = 8;         // Intrusive Thoughts alive cap
    public const int MaxToyLogos = 8;         // Casting Couch alive cap

    /// <summary>The classic logo palette, advanced one step per bounce.</summary>
    public static readonly (byte R, byte G, byte B)[] Hues =
    {
        (0xFF, 0x4D, 0xC4), (0x7A, 0xE0, 0xFF), (0xFF, 0xD7, 0x00),
        (0x9C, 0xE8, 0xA0), (0xD2, 0x4D, 0xFF), (0xFF, 0x8A, 0x5C),
    };

    private readonly Random _rng;
    private readonly TimeProvider _time;
    private readonly List<ChaosDvdLogo> _active = new();
    private DateTimeOffset _lastBounceCue = DateTimeOffset.MinValue;

    public ChaosDvdFlock(Random? rng = null, TimeProvider? time = null)
    {
        _rng = rng ?? new Random();
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The bounce area (WPF SystemParameters.WorkArea), in DIPs.</summary>
    public double Left, Top, Right = 1280, Bottom = 720;

    /// <summary>Label box for (text, font size); the head measures the outlined text.</summary>
    public Func<string, double, (double W, double H)> Measure = (t, s) => (t.Length * s * 0.6, s * 1.3);

    public IReadOnlyList<ChaosDvdLogo> Active => _active;
    public bool AnyActive => _active.Count > 0;
    /// <summary>Thoughts don't count: they lit the Porn DVD button mid-cooldown in WPF.</summary>
    public bool AnyToyActive => _active.Any(l => !l.IsThought);

    /// <summary>WPF Launch: 1-2 logos; a thought (<paramref name="text"/> set) respects the alive cap.</summary>
    public IReadOnlyList<ChaosDvdLogo> Launch(double durationSec, double speedMult, double scale, int count = 1,
                                              string? text = null, int splitBounces = 0)
    {
        var made = new List<ChaosDvdLogo>();
        for (int i = 0; i < Math.Clamp(count, 1, 2); i++)
        {
            if (text != null && _active.Count(l => l.IsThought) >= MaxThoughts) break;
            made.Add(Begin(durationSec, speedMult, scale, text, null, null, null, null, splitBounces));
        }
        return made;
    }

    private ChaosDvdLogo Begin(double durationSec, double speedMult, double scale, string? text,
                               double? startX, double? startY, double? vx, double? vy, int splitBounces)
    {
        var l = new ChaosDvdLogo
        {
            RemainingSec = Math.Max(1, durationSec),
            IsThought = text != null,
            FontScale = Math.Clamp(scale, 0.5, 1.5),
            SplitBouncesLeft = Math.Max(0, splitBounces),
            HueIndex = _rng.Next(Hues.Length),
            Text = text ?? "PORN",
        };
        double speed = BaseSpeed * Math.Clamp(speedMult, 0.3, 2.0);
        double angle = _rng.NextDouble() * Math.PI / 3 + Math.PI / 9;   // 20°..80°, never axis-flat
        l.Vx = vx ?? speed * Math.Cos(angle) * (_rng.Next(2) == 0 ? 1 : -1);
        l.Vy = vy ?? speed * Math.Sin(angle) * (_rng.Next(2) == 0 ? 1 : -1);
        (l.W, l.H) = Measure(l.Text, BaseFont * l.FontScale);
        l.X = startX ?? Left + _rng.NextDouble() * Math.Max(1, Right - Left - l.W);
        l.Y = startY ?? Top + _rng.NextDouble() * Math.Max(1, Bottom - Top - l.H);
        _active.Add(l);
        return l;
    }

    /// <summary>One frame (WPF StepAll + StepOne): dt already clamped by the caller at 0.1 s.
    /// Splits are appended and first move next frame.</summary>
    public ChaosDvdFrame Step(double dt)
    {
        var spawned = new List<ChaosDvdLogo>();
        var recolored = new List<ChaosDvdLogo>();
        var removed = new List<ChaosDvdLogo>();
        bool cue = false, launchCue = false;
        for (int i = _active.Count - 1; i >= 0; i--)
        {
            var l = _active[i];
            l.Age += dt;
            if (l.FadeOutSec >= 0)
            {
                l.FadeOutSec += dt;
                if (l.FadeOutSec >= 0.24) { _active.RemoveAt(i); removed.Add(l); }
                continue;
            }
            l.RemainingSec -= dt;
            if (l.RemainingSec <= 0) { l.FadeOutSec = 0; continue; }

            double x = l.X + l.Vx * dt, y = l.Y + l.Vy * dt;
            bool bounced = false;
            if (x <= Left) { x = Left; l.Vx = Math.Abs(l.Vx); bounced = true; }
            else if (x + l.W >= Right) { x = Right - l.W; l.Vx = -Math.Abs(l.Vx); bounced = true; }
            if (y <= Top) { y = Top; l.Vy = Math.Abs(l.Vy); bounced = true; }
            else if (y + l.H >= Bottom) { y = Bottom - l.H; l.Vy = -Math.Abs(l.Vy); bounced = true; }
            l.X = x;
            l.Y = y;
            if (!bounced) continue;

            l.HueIndex = (l.HueIndex + 1) % Hues.Length;
            recolored.Add(l);
            // Soft retro boing, throttled so two logos hugging a corner can't machine-gun it.
            var now = _time.GetUtcNow();
            if ((now - _lastBounceCue).TotalMilliseconds >= 250) { _lastBounceCue = now; cue = true; }

            // Casting Couch: a diverged twin peels off, both keep one fewer split (2 -> two -> four).
            if (l.SplitBouncesLeft > 0 && !l.IsThought)
            {
                l.SplitBouncesLeft--;
                if (_active.Count(o => !o.IsThought) < MaxToyLogos)
                {
                    double spd = Math.Sqrt(l.Vx * l.Vx + l.Vy * l.Vy);
                    double ang = Math.Atan2(l.Vy, l.Vx) + (_rng.Next(2) == 0 ? 0.61 : -0.61);   // ~35° divergence
                    spawned.Add(Begin(l.RemainingSec, 1.0, l.FontScale, null, l.X, l.Y,
                                      Math.Cos(ang) * spd, Math.Sin(ang) * spd, l.SplitBouncesLeft));
                    launchCue = true;
                }
            }
        }
        return new ChaosDvdFrame(spawned, recolored, removed, cue, launchCue);
    }

    /// <summary>Run teardown (WPF CloseActive): every logo gone at once.</summary>
    public void Clear() => _active.Clear();
}
