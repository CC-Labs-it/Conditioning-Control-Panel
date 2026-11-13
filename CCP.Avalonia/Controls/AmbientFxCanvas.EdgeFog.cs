using System;
using System.Linq;
using Avalonia;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ConditioningControlPanel.Services.UI;
using ConditioningControlPanel.Services.UI.EdgeEffects;
using Serilog;
using MotionLevel = ConditioningControlPanel.Models.MotionLevel;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/AmbientFxCanvas.EdgeFog.cs:184 (nav polish 11):
    /// the section edge's fog puffs and dust on one window-edge strip, numbers from Core
    /// <see cref="EdgeFogMath"/>. WPF drew its white 160/128 px sprites through an SKColorFilter
    /// under SKBlendMode.Plus on the SKElement's own surface; here each sprite is baked in the tint
    /// (when the hue moves, as WPF rebuilt its filter) and drawn Plus inside a layer (DrawEdgeFog).
    /// </summary>
    public partial class AmbientFxCanvas
    {
        private struct FogPuff { public float Along, BaseDepth, Speed, Size, Stretch, Peak, Age, Life, Phase, PhaseSpd, Breathe; public bool Big; }
        private struct GrainMote { public float Along, BaseDepth, Speed, Size, Peak, Age, Life, Phase, PhaseSpd, Wander, Twinkle, TwinkleSpd; }

        private FogPuff[] _fog = Array.Empty<FogPuff>();
        private GrainMote[] _grain = Array.Empty<GrainMote>();
        private int _fogN, _grainN;
        private float _fogBigT, _fogSmallT, _grainT;
        private bool _fogSeeded, _fogTinted;

        // The fog's own tint, crossfaded by SetEdgeFog; the embers keep Retint's instant swap.
        private Color _fogFrom, _fogTo, _fogNow;
        private float _fogGainFrom = 1f, _fogGainTo = 1f, _fogGainNow = 1f, _fogFadeT, _fogFadeDur;
        private Color? _fogBakedFor;
        private RenderTargetBitmap? _fogBigDot, _fogSmallDot, _grainDot;

        internal int GrainCount => _grainN; internal int FogPuffCount => _fogN;
        internal (Color Tint, float Gain) FogPaint => (_fogNow, _fogGainNow);

        private bool FogLayer => (_config.Layers & AmbientFxLayers.EdgeFog) != 0;
        private bool FogLongSide => _config.EdgeSide is EdgeSide.Top or EdgeSide.Bottom;
        private double FogLength => EdgeFogMath.Length(_config.EdgeSide, Bounds.Width, Bounds.Height);
        private float Rand(double min, double max) => (float)(min + _rng.NextDouble() * (max - min));
        private float SlowIfReduced(float v) => _config.EdgeFogReduced ? v * (float)EdgeFogMath.ReducedSpeed : v;

        /// <summary>Hand the fog a hue and a gain, crossfading over <paramref name="fadeMs"/>
        /// (0, or a canvas whose clock is not running, swaps at once). WPF :228.</summary>
        public void SetEdgeFog(Color hue, double gain, int fadeMs)
        {
            try
            {
                var to = Color.FromRgb(hue.R, hue.G, hue.B);
                float g = (float)Math.Clamp(gain, 0.0, 1.5);
                _config.EdgeFogGain = g;
                if (fadeMs <= 0 || !IsRunning || _fogFadeDur <= 0 && !_fogTinted) { SetFogTint(to, g); InvalidateVisual(); return; }
                (_fogFrom, _fogGainFrom, _fogTo, _fogGainTo) = (_fogNow, _fogGainNow, to, g);
                (_fogFadeT, _fogFadeDur) = (0, fadeMs / 1000f);
            }
            catch (Exception ex) { Log.Debug("AmbientFxCanvas.SetEdgeFog: {E}", ex.Message); }
        }

        private void SetFogTint(Color c, float gain)
        {
            (_fogFrom, _fogTo, _fogNow, _fogGainFrom, _fogGainTo, _fogGainNow) = (c, c, c, gain, gain, gain);
            (_fogFadeDur, _fogTinted) = (0, true);
        }

        /// <summary>Seed on (re)compose: the tint from the config, both layers pre-filled at
        /// random ages so the fog is there at once. WPF :388.</summary>
        private void ReseedFog()
        {
            (_fogN, _grainN, _fogBigT, _fogSmallT, _grainT, _fogSeeded) = (0, 0, 0f, 0f, 0f, false);
            if (!FogLayer || _particleBudget <= 0) { _fog = Array.Empty<FogPuff>(); _grain = Array.Empty<GrainMote>(); return; }
            var tint = _config.Tint ?? Env.ParticleColor;
            SetFogTint(Color.FromRgb(tint.R, tint.G, tint.B), (float)Math.Clamp(_config.EdgeFogGain, 0.0, 1.5));
            _fog = new FogPuff[EdgeFogMath.FullCount(true, FogLongSide) + EdgeFogMath.FullCount(false, FogLongSide)];
            _grain = new GrainMote[FogLongSide ? EdgeFogMath.DustLong : EdgeFogMath.DustShort];
            PrefillFog();
        }

        private int FogTarget(bool big) => EdgeFogMath.Target(EdgeFogMath.FullCount(big, FogLongSide), _liveBudget, _config.EdgeFogReduced);
        private int GrainTarget() => EdgeFogMath.Target(FogLongSide ? EdgeFogMath.DustLong : EdgeFogMath.DustShort, _liveBudget, _config.EdgeFogReduced);

        private void PrefillFog()
        {
            if (_fog.Length == 0 || Bounds.Width <= 1 || Bounds.Height <= 1) return;
            _fogSeeded = true;
            for (int i = FogTarget(true); i > 0; i--) SpawnFog(true, prefill: true);
            for (int i = FogTarget(false); i > 0; i--) SpawnFog(false, prefill: true);
            for (int i = GrainTarget(); i > 0; i--) SpawnGrain(prefill: true);
        }

        private void SpawnFog(bool big, bool prefill)
        {
            double length = FogLength;
            if (_fogN >= _fog.Length || length <= 1) return;
            float size = big ? Rand(EdgeFogMath.BigSizeMinPx, EdgeFogMath.BigSizeMaxPx) : Rand(EdgeFogMath.SmallSizeMinPx, EdgeFogMath.SmallSizeMaxPx);
            float speed = SlowIfReduced(big ? Rand(EdgeFogMath.BigSpeedMinPx, EdgeFogMath.BigSpeedMaxPx) : Rand(EdgeFogMath.SmallSpeedMinPx, EdgeFogMath.SmallSpeedMaxPx));
            if (_rng.NextDouble() < EdgeFogMath.CounterShare) speed = -speed;
            float life = big ? Rand(EdgeFogMath.BigLifeMin, EdgeFogMath.BigLifeMax) : Rand(EdgeFogMath.SmallLifeMin, EdgeFogMath.SmallLifeMax);
            if (_config.EdgeFogReduced) life /= (float)EdgeFogMath.ReducedSpeed;
            float breathe = Rand(EdgeFogMath.BreathePxMin, EdgeFogMath.BreathePxMax);
            double minDepth = Math.Max(-EdgeFogMath.DepthOutPx + breathe, size * EdgeFogMath.MinDepthShare);
            _fog[_fogN++] = new FogPuff
            {
                Along = (float)EdgeFogMath.SpawnAlong(_rng.NextDouble(), _rng.NextDouble(), length),
                BaseDepth = Rand(minDepth, minDepth + Math.Max(0, EdgeFogMath.MaxDepth(size, breathe) - minDepth)),
                Speed = speed, Size = size, Life = life, Age = prefill ? Rand(0, life) : 0f, Breathe = breathe, Big = big,
                Stretch = big ? Rand(EdgeFogMath.BigStretchMin, EdgeFogMath.BigStretchMax) : Rand(EdgeFogMath.SmallStretchMin, EdgeFogMath.SmallStretchMax),
                Peak = big ? Rand(EdgeFogMath.BigAlphaMin, EdgeFogMath.BigAlphaMax) : Rand(EdgeFogMath.SmallAlphaMin, EdgeFogMath.SmallAlphaMax),
                Phase = Rand(0, Math.PI * 2), PhaseSpd = SlowIfReduced(Rand(EdgeFogMath.BreatheRateMin, EdgeFogMath.BreatheRateMax)),
            };
        }

        private void SpawnGrain(bool prefill)
        {
            double length = FogLength;
            if (_grainN >= _grain.Length || length <= 1) return;
            float speed = SlowIfReduced(Rand(EdgeFogMath.DustSpeedMinPx, EdgeFogMath.DustSpeedMaxPx));
            float life = Rand(EdgeFogMath.DustLifeMin, EdgeFogMath.DustLifeMax);
            if (_config.EdgeFogReduced) life /= (float)EdgeFogMath.ReducedSpeed;
            float wander = Rand(EdgeFogMath.DustWanderPxMin, EdgeFogMath.DustWanderPxMax);
            _grain[_grainN++] = new GrainMote
            {
                Along = (float)EdgeFogMath.SpawnAlong(_rng.NextDouble(), _rng.NextDouble(), length),
                BaseDepth = (float)EdgeFogMath.DustDepth(_rng.NextDouble(), wander),
                Speed = _rng.NextDouble() < 0.5 ? -speed : speed, Life = life, Age = prefill ? Rand(0, life) : 0f, Wander = wander,
                Size = Rand(EdgeFogMath.DustSizeMinPx, EdgeFogMath.DustSizeMaxPx),
                Peak = Rand(EdgeFogMath.DustAlphaMin, EdgeFogMath.DustAlphaMax),
                Phase = Rand(0, Math.PI * 2), PhaseSpd = SlowIfReduced(Rand(EdgeFogMath.DustWanderRateMin, EdgeFogMath.DustWanderRateMax)),
                Twinkle = Rand(0, Math.PI * 2),
                TwinkleSpd = SlowIfReduced(Rand(EdgeFogMath.DustTwinkleRateMin, EdgeFogMath.DustTwinkleRateMax)),
            };
        }

        /// <summary>One fog step: advance a running crossfade, age, drift and breathe every puff and
        /// speck, retire the spent, refill each layer to its governed target. WPF :490.</summary>
        internal void StepFog(float dt)
        {
            if (_fogFadeDur > 0)
            {
                _fogFadeT += dt;
                float t = Math.Clamp(_fogFadeT / _fogFadeDur, 0f, 1f), e = 1f - (1f - t) * (1f - t);
                byte Mix(byte a, byte b) => (byte)(a + (b - a) * e);
                _fogNow = Color.FromRgb(Mix(_fogFrom.R, _fogTo.R), Mix(_fogFrom.G, _fogTo.G), Mix(_fogFrom.B, _fogTo.B));
                _fogGainNow = _fogGainFrom + (_fogGainTo - _fogGainFrom) * e;
                if (t >= 1f) _fogFadeDur = 0;
            }
            if (_fog.Length == 0) return;
            if (!_fogSeeded) PrefillFog();
            double length = FogLength;
            for (int i = _grainN - 1; i >= 0; i--)
            {
                ref var d = ref _grain[i];
                d.Age += dt;
                d.Along = (float)EdgeFogMath.Advance(d.Along, d.Speed, dt);
                d.Phase += d.PhaseSpd * dt;
                d.Twinkle += d.TwinkleSpd * dt;
                if (EdgeFogMath.IsSpent(d.Age, d.Life, d.Along, length, 4)) _grain[i] = _grain[--_grainN];
            }
            for (int i = _fogN - 1; i >= 0; i--)
            {
                ref var p = ref _fog[i];
                p.Age += dt;
                p.Along = (float)EdgeFogMath.Advance(p.Along, p.Speed, dt);
                p.Phase += p.PhaseSpd * dt;
                if (EdgeFogMath.IsSpent(p.Age, p.Life, p.Along, length, p.Size * p.Stretch)) _fog[i] = _fog[--_fogN];
            }
            if (!FogLayer || length <= 1) return;
            for (int n = Due(GrainTarget(), _grainN, ref _grainT, EdgeFogMath.DustSpawnEverySeconds, dt); n > 0; n--) SpawnGrain(false);
            int big = 0;
            for (int i = 0; i < _fogN; i++) if (_fog[i].Big) big++;
            int small = _fogN - big;
            for (int n = Due(FogTarget(true), big, ref _fogBigT, EdgeFogMath.SpawnEverySeconds, dt); n > 0; n--) SpawnFog(true, false);
            for (int n = Due(FogTarget(false), small, ref _fogSmallT, EdgeFogMath.SpawnEverySeconds, dt); n > 0; n--) SpawnFog(false, false);
        }

        /// <summary>Spawns due on one layer's clock. A full layer banks one spawn's worth at most,
        /// so a retirement is replaced alone.</summary>
        private static int Due(int target, int have, ref float clock, double every, float dt)
        {
            clock = have >= target ? Math.Min(clock + dt, (float)every) : clock + dt;
            int n = 0;
            for (; have + n < target && clock > every; n++) clock -= (float)every;
            return n;
        }

        private void BakeFogDots()
        {
            static RenderTargetBitmap Bake(Color c, float core, int px)
            {
                var bmp = new RenderTargetBitmap(new PixelSize(px, px));
                using (var ctx = bmp.CreateDrawingContext())
                    ctx.DrawEllipse(MakeDot(c, core), null, new Point(px / 2.0, px / 2.0), px / 2.0, px / 2.0);
                return bmp;
            }
            _fogBigDot?.Dispose(); _fogSmallDot?.Dispose(); _grainDot?.Dispose();
            _fogBakedFor = _fogNow;
            _fogBigDot = Bake(_fogNow, 0.28f, 160);   // WPF GlowSprite
            _fogSmallDot = Bake(_fogNow, 0f, 128);    // WPF Dot
            // Dust wears the rail ring's vivid hue, lifted toward white so it glints over the fog.
            uint vivid = NavRailPaintRules.Vivid(_fogNow.ToUInt32());
            byte Lift(int shift) { int c = (int)((vivid >> shift) & 0xFF); return (byte)(c + (255 - c) * (float)EdgeFogMath.DustLift); }
            _grainDot = Bake(Color.FromRgb(Lift(16), Lift(8), Lift(0)), 0f, 128);
        }

        /// <summary>Free the baked fog sprites (render target surfaces) when the strip leaves the tree;
        /// the next draw bakes them again.</summary>
        private void DisposeFogDots()
        {
            _fogBigDot?.Dispose(); _fogSmallDot?.Dispose(); _grainDot?.Dispose();
            (_fogBigDot, _fogSmallDot, _grainDot, _fogBakedFor) = (null, null, null, null);
        }

        /// <summary>Big puffs first, small over them, then the dust (WPF :549/:337). WPF summed them
        /// Plus on its own surface and composited that over the window; the opacity mask is that
        /// surface here (a bare Plus would add the window under the strip too).</summary>
        private void DrawEdgeFog(DrawingContext ctx)
        {
            double aw = Bounds.Width, ah = Bounds.Height;
            if (_fogN + _grainN == 0 || aw <= 1 || ah <= 1) return;
            if (_fogBakedFor != _fogNow) BakeFogDots();
            using var layer = ctx.PushOpacityMask(Brushes.White, new Rect(0, 0, aw, ah));
            using var plus = ctx.PushRenderOptions(new RenderOptions { BitmapBlendingMode = BitmapBlendingMode.Plus });
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < _fogN; i++)
                {
                    var p = _fog[i];
                    if (p.Big != (pass == 0)) continue;
                    float a = (float)EdgeFogMath.Alpha(p.Peak, p.Age, p.Life, _fogGainNow);
                    if (a <= 0.003f) continue;
                    var (x, y) = EdgeFogMath.Position(_config.EdgeSide, p.Along, EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase), aw, ah);
                    float span = p.Size * p.Stretch;
                    DrawImageSprite(ctx, p.Big ? _fogBigDot : _fogSmallDot, x, y, FogLongSide ? span : p.Size, FogLongSide ? p.Size : span, a);
                }
            for (int i = 0; i < _grainN; i++)
            {
                var d = _grain[i];
                float a = (float)EdgeFogMath.DustAlpha(d.Peak, d.Age, d.Life, d.Twinkle, _fogGainNow);
                if (a <= 0.01f) continue;
                var (x, y) = EdgeFogMath.Position(_config.EdgeSide, d.Along, d.BaseDepth + d.Wander * Math.Sin(d.Phase), aw, ah);
                // The soft dot drawn 2.2x the speck so its bright core is the speck (WPF :382).
                DrawImageSprite(ctx, _grainDot, x, y, d.Size * 2.2, d.Size * 2.2, a);
            }
        }

        private static void DrawImageSprite(DrawingContext ctx, IImage? img, double cx, double cy, double w, double h, float alpha)
        {
            if (img == null) return;
            using (ctx.PushOpacity(Math.Min(alpha, 1f)))
                ctx.DrawImage(img, new Rect(cx - w / 2, cy - h / 2, w, h));
        }

        /// <summary>Test seam: the (x, y, size) in DIPs of every live puff.</summary>
        internal (double X, double Y, double Size, bool Big)[] FogPuffsForTests()
        {
            if (!_fogSeeded) PrefillFog();
            return _fog.Take(_fogN).Select(p => EdgeFogMath.Position(_config.EdgeSide, p.Along, EdgeFogMath.Depth(p.BaseDepth, p.Breathe, p.Phase),
                Bounds.Width, Bounds.Height) is var (x, y) ? (x, y, (double)p.Size, p.Big) : default).ToArray();
        }

        /// <summary>Test seam: <paramref name="n"/> copies of one 40 px puff, mid-life, no dust.</summary>
        internal void PinFogForTests(int n, float along, float depth, float peak)
        {
            _grainN = 0;
            for (_fogN = 0; _fogN < n;)
                _fog[_fogN++] = new FogPuff { Along = along, BaseDepth = depth, Size = 40, Stretch = 1, Peak = peak, Life = 2, Age = 1, Big = true };
        }

        /// <summary>Reduced motion may tick a fog layer marked EdgeFogReduced on a tier that
        /// allows ambient motion and particles; every other layer keeps the Full-only gate. WPF :580.</summary>
        private bool ReducedFogMayRun() =>
            FogLayer && _config.EdgeFogReduced && Env.Level == MotionLevel.Reduced
            && Env.AllowAmbientMotion(_tier) && _particleBudget > 0;
    }
}
