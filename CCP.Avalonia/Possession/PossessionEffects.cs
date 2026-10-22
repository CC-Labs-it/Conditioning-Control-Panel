// PORTED from ConditioningControlPanel/Services/Possession/Effects/PossessionEffectBase.cs (the deck
// metadata, Amp/Rand/Sign) and the four transform-only effects NudgeEffect.cs, DriftEffect.cs,
// BreatheEffect.cs and WobbleEffect.cs (numbers and roles unchanged, cited per class).
//
// WPF drove each effect with DoubleAnimations on the lease's transforms (PossAnim.Pulse/Oscillate).
// Here an effect is a pure function of elapsed milliseconds: Frame(ms) writes the lease parts, the
// runtime calls it from the window's frame clock. Same curves (cubic ease out/in, sine ping-pong),
// no Animation objects (P82).
//
// Not ported (possession-head-b/c): the ember charge + attribution (EmberAttribution, Ghost) and the
// remaining effects - see PossessionRuntime.cs.

using System;
using System.Collections.Generic;
using ConditioningControlPanel.Services.Possession;

namespace ConditioningControlPanel.Avalonia.Possession
{
    internal abstract class HeadEffect
    {
        public abstract string Id { get; }
        public abstract PossessionRung MinRung { get; }
        public virtual PossessionIntensity MinIntensity => PossessionIntensity.Gentle;
        public virtual bool UsesFlicker => false;
        public abstract double Weight { get; }
        public abstract TimeSpan HoldFor { get; }
        public abstract IReadOnlyList<PossessionRole> Roles { get; }

        /// <summary>The Core deck's view of this effect (WPF PossessionDeckMeta.MetaOf).</summary>
        public PossessionEffectMeta Meta => new(Id, MinRung, MinIntensity, UsesFlicker, Weight, Roles);

        protected PossessionLease Lease = null!;
        private Random _rng = Random.Shared;
        private bool _photosafe;

        internal void Begin(PossessionLease lease, Random rng, bool photosafe)
        {
            Lease = lease;
            _rng = rng;
            _photosafe = photosafe;
            Start();
        }

        /// <summary>Roll this run's numbers (WPF ApplyCoreAsync, minus the animation).</summary>
        protected abstract void Start();

        /// <summary>Write the lease parts for <paramref name="ms"/> since the effect began.</summary>
        internal abstract void Frame(double ms);

        protected double Amp(double v) => _photosafe ? v * 0.5 : v;
        protected double Rand(double min, double max) => min + _rng.NextDouble() * (max - min);
        protected int Sign() => _rng.Next(2) == 0 ? -1 : 1;
        protected bool Chance(int percent) => _rng.Next(100) < percent;

        // WPF CubicEase / SineEase curves (PossAnim.EaseOut, EaseIn, EaseInOut, Sine).
        internal static double CubicOut(double f) => 1 - Math.Pow(1 - f, 3);
        internal static double CubicIn(double f) => f * f * f;
        internal static double CubicInOut(double f) => f < 0.5 ? 4 * f * f * f : 1 - Math.Pow(-2 * f + 2, 3) / 2;
        internal static double SineInOut(double f) => (1 - Math.Cos(Math.PI * f)) / 2;

        /// <summary>WPF PossAnim.Pulse: from -> peak (rise, ease out) -> to (fall, ease in), then holds.</summary>
        protected static double Pulse(double ms, double peak, double riseMs, double fallMs, double to)
        {
            if (ms <= 0) return 0;
            if (ms < riseMs) return peak * CubicOut(ms / riseMs);
            if (ms < riseMs + fallMs) return peak + (to - peak) * CubicIn((ms - riseMs) / fallMs);
            return to;
        }

        /// <summary>WPF PossAnim.Oscillate: from -> to over a half period, auto-reversed forever, sine.</summary>
        protected static double Oscillate(double ms, double from, double to, double halfMs)
        {
            double phase = Math.Max(0, ms) / halfMs;
            long k = (long)Math.Floor(phase);
            double f = phase - k;
            if ((k & 1) == 1) f = 1 - f;
            return from + (to - from) * SineInOut(f);
        }
    }

    /// <summary>WPF NudgeEffect.cs:21-45: 3-4.5 x 2-3.5 px shove, 35% overshoot, 130/190 ms, held 2 s.</summary>
    internal sealed class NudgeHeadEffect : HeadEffect
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.Button, PossessionRole.Toggle };
        private double _dx, _dy;
        public override string Id => "nudge";
        public override PossessionRung MinRung => PossessionRung.Settle;
        public override double Weight => 3;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(2);
        public override IReadOnlyList<PossessionRole> Roles => _roles;

        protected override void Start()
        {
            _dx = Amp(Rand(3.0, 4.5)) * Sign();
            _dy = Amp(Rand(2.0, 3.5)) * Sign();
        }

        internal override void Frame(double ms)
        {
            Lease.Translate.X = Pulse(ms, _dx * 1.35, 130, 190, _dx);
            Lease.Translate.Y = Pulse(ms, _dy * 1.35, 130, 190, _dy);
        }
    }

    /// <summary>WPF DriftEffect.cs:14-40: 6-8 px slow sine (2.5 s half period), sideways 70%, held 5 s.</summary>
    internal sealed class DriftHeadEffect : HeadEffect
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Label, PossessionRole.Title };
        private double _reach;
        private bool _sideways;
        public override string Id => "drift";
        public override PossessionRung MinRung => PossessionRung.Drift;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(5);
        public override IReadOnlyList<PossessionRole> Roles => _roles;

        protected override void Start()
        {
            _reach = Amp(Rand(6, 8)) * Sign();
            _sideways = Chance(70);
        }

        internal override void Frame(double ms)
        {
            double v = Oscillate(ms, 0, _reach, 2500);
            if (_sideways) Lease.Translate.X = v; else Lease.Translate.Y = v;
        }
    }

    /// <summary>WPF BreatheEffect.cs:17-45: 3% swell from the centre, 1.6 s half period, held 3.5 s.</summary>
    internal sealed class BreatheHeadEffect : HeadEffect
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Card, PossessionRole.Button };
        private double _peak;
        public override string Id => "breathe";
        public override PossessionRung MinRung => PossessionRung.Settle;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromMilliseconds(3500);
        public override IReadOnlyList<PossessionRole> Roles => _roles;

        protected override void Start()
        {
            Lease.SetOrigin(0.5, 0.5);
            _peak = 1.0 + Amp(0.03);
        }

        internal override void Frame(double ms)
        {
            double s = Oscillate(ms, 1.0, _peak, 1600);
            Lease.Scale.ScaleX = s;
            Lease.Scale.ScaleY = s;
        }
    }

    /// <summary>WPF WobbleEffect.cs:14-38: the timer glass rocks +-2 degrees at 1.3 Hz (385 ms half
    /// swing), held 6 s. The VALUE is never touched; only the glass moves.</summary>
    internal sealed class WobbleHeadEffect : HeadEffect
    {
        private static readonly PossessionRole[] _roles = { PossessionRole.Timer };
        private double _swing;
        public override string Id => "wobble";
        public override PossessionRung MinRung => PossessionRung.Melt;
        public override double Weight => 2;
        public override TimeSpan HoldFor => TimeSpan.FromSeconds(6);
        public override IReadOnlyList<PossessionRole> Roles => _roles;

        protected override void Start()
        {
            Lease.SetOrigin(0.5, 0.5);
            _swing = Amp(2);
        }

        internal override void Frame(double ms) => Lease.Rotate.Angle = Oscillate(ms, -_swing, _swing, 385);
    }
}
