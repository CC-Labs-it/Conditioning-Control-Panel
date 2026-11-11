using System;
using Avalonia.Media;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Services.UI.EdgeEffects;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/AmbientFxCanvas.EdgeFog.cs:184 (nav polish 11):
    /// the section edge's fog puffs and dust on one window-edge strip. The numbers are Core
    /// <see cref="EdgeFogMath"/>. WPF tinted one white sprite through an SKColorFilter; here the
    /// tint is the radial brush itself (big puffs the 0.28-core glow, small puffs and dust the dot),
    /// rebuilt only on a hue change and on each tick of a running crossfade, as WPF rebuilt its filter.
    /// </summary>
    public partial class AmbientFxCanvas
    {
        private struct FogPuff
        {
            public float Along, BaseDepth, Speed, Size, Stretch, Peak, Age, Life, Phase, PhaseSpd, Breathe;
            public bool Big;
        }

        private struct GrainMote
        {
            public float Along, BaseDepth, Speed, Size, Peak, Age, Life, Phase, PhaseSpd, Wander, Twinkle, TwinkleSpd;
        }

        private GrainMote[] _grain = Array.Empty<GrainMote>();
        private int _grainN;
        private float _grainT;

        private FogPuff[] _fog = Array.Empty<FogPuff>();
        private int _fogN;
        private float _fogBigT, _fogSmallT;
        private bool _fogSeeded;

        // The fog's own tint, crossfaded on a section change (SetEdgeFog). Kept apart from the
        // particle tint so the embers keep Retint's instant swap.
        private Color _fogFrom, _fogTo, _fogNow;
        private float _fogGainFrom = 1f, _fogGainTo = 1f, _fogGainNow = 1f;
        private float _fogFadeT, _fogFadeDur;
        private bool _fogTinted;
        private IBrush? _fogBigDot, _fogSmallDot, _grainDot;

        internal int GrainCount => _grainN;
        internal int FogPuffCount => _fogN;
        internal (Color Tint, float Gain) FogPaint => (_fogNow, _fogGainNow);

        private bool FogLayer => (_config.Layers & AmbientFxLayers.EdgeFog) != 0;

        /// <summary>Hand the fog a hue and a gain, crossfading over <paramref name="fadeMs"/>
        /// (0, or a canvas whose clock is not running, swaps at once). WPF :228.</summary>
        public void SetEdgeFog(Color hue, double gain, int fadeMs)
        {
            try
            {
                var to = Color.FromRgb(hue.R, hue.G, hue.B);
                float g = (float)Math.Clamp(gain, 0.0, 1.5);
                _config.EdgeFogGain = g;
                if (fadeMs <= 0 || !IsRunning || _fogFadeDur <= 0 && !_fogTinted)
                {
                    _fogFrom = _fogTo = _fogNow = to;
                    _fogGainFrom = _fogGainTo = _fogGainNow = g;
                    _fogFadeDur = 0;
                    RebuildFogTint();
                    InvalidateVisual();
                    return;
                }
                _fogFrom = _fogNow;
                _fogGainFrom = _fogGainNow;
                _fogTo = to;
                _fogGainTo = g;
                _fogFadeT = 0;
                _fogFadeDur = fadeMs / 1000f;
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.SetEdgeFog: {E}", ex.Message); }
        }

        private void RebuildFogTint()
        {
            _fogTinted = true;
            _fogBigDot = MakeDot(_fogNow, 0.28f);
            _fogSmallDot = MakeDot(_fogNow, 0f);
            // Dust wears the rail ring's vivid hue, lifted toward white so it glints over the fog.
            uint vivid = NavRailPaintRules.Vivid(_fogNow.ToUInt32());
            byte Lift(int shift) { int c = (int)((vivid >> shift) & 0xFF); return (byte)(c + (255 - c) * (float)EdgeFogMath.DustLift); }
            _grainDot = MakeDot(Color.FromRgb(Lift(16), Lift(8), Lift(0)), 0f);
        }

        private int GrainTarget()
        {
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            return EdgeFogMath.Target(longSide ? EdgeFogMath.DustLong : EdgeFogMath.DustShort,
                _liveBudget, _config.EdgeFogReduced);
        }

        private void SpawnGrain(bool prefill)
        {
            if (_grainN >= _grain.Length) return;
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            if (length <= 1) return;
            double r() => _rng.NextDouble();
            double speed = EdgeFogMath.DustSpeedMinPx + r() * (EdgeFogMath.DustSpeedMaxPx - EdgeFogMath.DustSpeedMinPx);
            double life = EdgeFogMath.DustLifeMin + r() * (EdgeFogMath.DustLifeMax - EdgeFogMath.DustLifeMin);
            double rate = EdgeFogMath.DustWanderRateMin + r() * (EdgeFogMath.DustWanderRateMax - EdgeFogMath.DustWanderRateMin);
            double tw = EdgeFogMath.DustTwinkleRateMin + r() * (EdgeFogMath.DustTwinkleRateMax - EdgeFogMath.DustTwinkleRateMin);
            if (_config.EdgeFogReduced)
            {
                speed *= EdgeFogMath.ReducedSpeed; life /= EdgeFogMath.ReducedSpeed;
                rate *= EdgeFogMath.ReducedSpeed; tw *= EdgeFogMath.ReducedSpeed;
            }
            if (r() < 0.5) speed = -speed;
            double wander = EdgeFogMath.DustWanderPxMin + r() * (EdgeFogMath.DustWanderPxMax - EdgeFogMath.DustWanderPxMin);
            _grain[_grainN++] = new GrainMote
            {
                Along = (float)EdgeFogMath.SpawnAlong(r(), r(), length),
                BaseDepth = (float)EdgeFogMath.DustDepth(r(), wander),
                Speed = (float)speed,
                Size = (float)(EdgeFogMath.DustSizeMinPx + r() * (EdgeFogMath.DustSizeMaxPx - EdgeFogMath.DustSizeMinPx)),
                Peak = (float)(EdgeFogMath.DustAlphaMin + r() * (EdgeFogMath.DustAlphaMax - EdgeFogMath.DustAlphaMin)),
                Life = (float)life,
                Age = prefill ? (float)(r() * life) : 0f,
                Phase = (float)(r() * Math.PI * 2),
                PhaseSpd = (float)rate,
                Wander = (float)wander,
                Twinkle = (float)(r() * Math.PI * 2),
                TwinkleSpd = (float)tw,
            };
        }

        private void StepGrain(float dt)
        {
            if (_grain.Length == 0) return;
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            for (int i = _grainN - 1; i >= 0; i--)
            {
                var d = _grain[i];
                d.Age += dt;
                d.Along = (float)EdgeFogMath.Advance(d.Along, d.Speed, dt);
                d.Phase += d.PhaseSpd * dt;
                d.Twinkle += d.TwinkleSpd * dt;
                if (EdgeFogMath.IsSpent(d.Age, d.Life, d.Along, length, 4)) _grain[i] = _grain[--_grainN];
                else _grain[i] = d;
            }
            if (!FogLayer || length <= 1) return;
            int target = GrainTarget();
            _grainT = _grainN >= target
                ? Math.Min(_grainT + dt, (float)EdgeFogMath.DustSpawnEverySeconds)
                : _grainT + dt;
            while (_grainN < target && _grainT > EdgeFogMath.DustSpawnEverySeconds && _grainN < _grain.Length)
            {
                _grainT -= (float)EdgeFogMath.DustSpawnEverySeconds;
                SpawnGrain(prefill: false);
            }
        }

        private void DrawGrain(DrawingContext ctx)
        {
            if (_grainN == 0 || _grainDot == null) return;
            double aw = Bounds.Width, ah = Bounds.Height;
            if (aw <= 1 || ah <= 1) return;
            var side = _config.EdgeSide;
            for (int i = 0; i < _grainN; i++)
            {
                var d = _grain[i];
                float a = (float)EdgeFogMath.DustAlpha(d.Peak, d.Age, d.Life, d.Twinkle, _fogGainNow);
                if (a <= 0.01f) continue;
                double depth = d.BaseDepth + d.Wander * Math.Sin(d.Phase);
                var (x, y) = EdgeFogMath.Position(side, d.Along, depth, aw, ah);
                // The soft dot drawn 2.2x the speck so its bright core is the speck (WPF :382).
                float s = d.Size * 2.2f;
                DrawSprite(ctx, _grainDot, (float)x, (float)y, s, s, a);
            }
        }

        /// <summary>Seed on (re)compose: the tint from the config, both layers pre-filled at
        /// random ages so the fog is there at once. WPF :388.</summary>
        private void ReseedFog()
        {
            _fogN = 0;
            _fogBigT = _fogSmallT = 0f;
            _grainN = 0;
            _grainT = 0f;
            if (!FogLayer || _particleBudget <= 0)
            {
                _fog = Array.Empty<FogPuff>();
                _grain = Array.Empty<GrainMote>();
                return;
            }

            var tint = _config.Tint ?? Env.ParticleColor;
            _fogFrom = _fogTo = _fogNow = Color.FromRgb(tint.R, tint.G, tint.B);
            _fogGainFrom = _fogGainTo = _fogGainNow = (float)Math.Clamp(_config.EdgeFogGain, 0.0, 1.5);
            _fogFadeDur = 0;
            RebuildFogTint();

            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            _fog = new FogPuff[EdgeFogMath.FullCount(true, longSide) + EdgeFogMath.FullCount(false, longSide)];
            _grain = new GrainMote[longSide ? EdgeFogMath.DustLong : EdgeFogMath.DustShort];
            _fogSeeded = false;
            PrefillFog();
        }

        private void PrefillFog()
        {
            if (_fog.Length == 0 || Bounds.Width <= 1 || Bounds.Height <= 1) return;
            _fogSeeded = true;
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            int big = EdgeFogMath.Target(EdgeFogMath.FullCount(true, longSide), _liveBudget, _config.EdgeFogReduced);
            int small = EdgeFogMath.Target(EdgeFogMath.FullCount(false, longSide), _liveBudget, _config.EdgeFogReduced);
            for (int i = 0; i < big; i++) SpawnFog(true, prefill: true);
            for (int i = 0; i < small; i++) SpawnFog(false, prefill: true);
            int dust = GrainTarget();
            for (int i = 0; i < dust; i++) SpawnGrain(prefill: true);
        }

        private int FogCount(bool big)
        {
            int n = 0;
            for (int i = 0; i < _fogN; i++) if (_fog[i].Big == big) n++;
            return n;
        }

        private void SpawnFog(bool big, bool prefill)
        {
            if (_fogN >= _fog.Length) return;
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            if (length <= 1) return;

            double r() => _rng.NextDouble();
            double size = big
                ? EdgeFogMath.BigSizeMinPx + r() * (EdgeFogMath.BigSizeMaxPx - EdgeFogMath.BigSizeMinPx)
                : EdgeFogMath.SmallSizeMinPx + r() * (EdgeFogMath.SmallSizeMaxPx - EdgeFogMath.SmallSizeMinPx);
            double speed = big
                ? EdgeFogMath.BigSpeedMinPx + r() * (EdgeFogMath.BigSpeedMaxPx - EdgeFogMath.BigSpeedMinPx)
                : EdgeFogMath.SmallSpeedMinPx + r() * (EdgeFogMath.SmallSpeedMaxPx - EdgeFogMath.SmallSpeedMinPx);
            if (_config.EdgeFogReduced) speed *= EdgeFogMath.ReducedSpeed;
            if (r() < EdgeFogMath.CounterShare) speed = -speed;
            double life = big
                ? EdgeFogMath.BigLifeMin + r() * (EdgeFogMath.BigLifeMax - EdgeFogMath.BigLifeMin)
                : EdgeFogMath.SmallLifeMin + r() * (EdgeFogMath.SmallLifeMax - EdgeFogMath.SmallLifeMin);
            if (_config.EdgeFogReduced) life /= EdgeFogMath.ReducedSpeed;
            double peak = big
                ? EdgeFogMath.BigAlphaMin + r() * (EdgeFogMath.BigAlphaMax - EdgeFogMath.BigAlphaMin)
                : EdgeFogMath.SmallAlphaMin + r() * (EdgeFogMath.SmallAlphaMax - EdgeFogMath.SmallAlphaMin);
            double stretch = big
                ? EdgeFogMath.BigStretchMin + r() * (EdgeFogMath.BigStretchMax - EdgeFogMath.BigStretchMin)
                : EdgeFogMath.SmallStretchMin + r() * (EdgeFogMath.SmallStretchMax - EdgeFogMath.SmallStretchMin);
            double breathe = EdgeFogMath.BreathePxMin + r() * (EdgeFogMath.BreathePxMax - EdgeFogMath.BreathePxMin);
            double maxDepth = EdgeFogMath.MaxDepth(size, breathe);
            double minDepth = Math.Max(-EdgeFogMath.DepthOutPx + breathe, size * EdgeFogMath.MinDepthShare);
            double depth = minDepth + r() * Math.Max(0, maxDepth - minDepth);
            double rate = EdgeFogMath.BreatheRateMin + r() * (EdgeFogMath.BreatheRateMax - EdgeFogMath.BreatheRateMin);
            if (_config.EdgeFogReduced) rate *= EdgeFogMath.ReducedSpeed;

            _fog[_fogN++] = new FogPuff
            {
                Along = (float)EdgeFogMath.SpawnAlong(r(), r(), length),
                BaseDepth = (float)depth,
                Speed = (float)speed,
                Size = (float)size,
                Stretch = (float)stretch,
                Peak = (float)peak,
                Life = (float)life,
                Age = prefill ? (float)(r() * life) : 0f,
                Phase = (float)(r() * Math.PI * 2),
                PhaseSpd = (float)rate,
                Breathe = (float)breathe,
                Big = big,
            };
        }

        /// <summary>One fog step: advance a running crossfade, age, drift and breathe every puff,
        /// retire the spent, refill each layer to its governed target. WPF :490.</summary>
        internal void StepFog(float dt)
        {
            if (_fogFadeDur > 0)
            {
                _fogFadeT += dt;
                float t = Math.Clamp(_fogFadeT / _fogFadeDur, 0f, 1f);
                float e = 1f - (1f - t) * (1f - t);
                _fogNow = Color.FromRgb(
                    (byte)(_fogFrom.R + (_fogTo.R - _fogFrom.R) * e),
                    (byte)(_fogFrom.G + (_fogTo.G - _fogFrom.G) * e),
                    (byte)(_fogFrom.B + (_fogTo.B - _fogFrom.B) * e));
                _fogGainNow = _fogGainFrom + (_fogGainTo - _fogGainFrom) * e;
                if (t >= 1f) _fogFadeDur = 0;
                RebuildFogTint();
            }

            if (_fog.Length == 0) return;
            if (!_fogSeeded) PrefillFog();
            StepGrain(dt);
            double length = EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
            for (int i = _fogN - 1; i >= 0; i--)
            {
                var p = _fog[i];
                p.Age += dt;
                p.Along = (float)EdgeFogMath.Advance(p.Along, p.Speed, dt);
                p.Phase += p.PhaseSpd * dt;
                if (EdgeFogMath.IsSpent(p.Age, p.Life, p.Along, length, p.Size * p.Stretch))
                    _fog[i] = _fog[--_fogN];
                else
                    _fog[i] = p;
            }

            if (!FogLayer || length <= 1) return;
            bool longSide = _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
            RefillFog(true, longSide, ref _fogBigT, dt);
            RefillFog(false, longSide, ref _fogSmallT, dt);
        }

        private void RefillFog(bool big, bool longSide, ref float clock, float dt)
        {
            int target = EdgeFogMath.Target(EdgeFogMath.FullCount(big, longSide), _liveBudget, _config.EdgeFogReduced);
            int have = FogCount(big);
            // A full layer banks one spawn's worth at most, so a retirement is replaced alone.
            clock = have >= target
                ? Math.Min(clock + dt, (float)EdgeFogMath.SpawnEverySeconds)
                : clock + dt;
            while (have < target && clock > EdgeFogMath.SpawnEverySeconds && _fogN < _fog.Length)
            {
                clock -= (float)EdgeFogMath.SpawnEverySeconds;
                SpawnFog(big, prefill: false);
                have++;
            }
        }

        /// <summary>Big puffs first, small over them, then the dust. WPF :549.</summary>
        private void DrawEdgeFog(DrawingContext ctx)
        {
            double aw = Bounds.Width, ah = Bounds.Height;
            if (_fogN > 0 && aw > 1 && ah > 1)
            {
                var side = _config.EdgeSide;
                bool horizontal = side is EdgeSide.Top or EdgeSide.Bottom;
                for (int pass = 0; pass < 2; pass++)
                {
                    bool big = pass == 0;
                    var dot = big ? _fogBigDot : _fogSmallDot;
                    for (int i = 0; i < _fogN; i++)
                    {
                        var p = _fog[i];
                        if (p.Big != big) continue;
                        float a = (float)EdgeFogMath.Alpha(p.Peak, p.Age, p.Life, _fogGainNow);
                        if (a <= 0.003f) continue;
                        var (x, y) = EdgeFogMath.Position(side, p.Along, EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase), aw, ah);
                        float span = p.Size * p.Stretch;
                        DrawSprite(ctx, dot, (float)x, (float)y, horizontal ? span : p.Size, horizontal ? p.Size : span, a);
                    }
                }
            }
            DrawGrain(ctx);
        }

        /// <summary>Test seam: the (x, y, size) in DIPs of every live puff.</summary>
        internal (double X, double Y, double Size, bool Big)[] FogPuffsForTests()
        {
            if (!_fogSeeded) PrefillFog();
            var r = new (double, double, double, bool)[_fogN];
            for (int i = 0; i < _fogN; i++)
            {
                var p = _fog[i];
                var (x, y) = EdgeFogMath.Position(_config.EdgeSide, p.Along,
                    EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase), Bounds.Width, Bounds.Height);
                r[i] = (x, y, p.Size, p.Big);
            }
            return r;
        }

        /// <summary>Reduced motion may tick a fog layer marked EdgeFogReduced on a tier that
        /// allows ambient motion and particles; every other layer keeps the Full-only gate. WPF :580.</summary>
        private bool ReducedFogMayRun() =>
            FogLayer && _config.EdgeFogReduced
            && Env.Level == MotionLevel.Reduced
            && Env.AllowAmbientMotion(_tier) && _particleBudget > 0;
    }
}
