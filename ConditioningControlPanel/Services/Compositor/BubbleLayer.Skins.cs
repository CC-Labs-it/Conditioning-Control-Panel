using ConditioningControlPanel.BubbleCore;
using SkiaSharp;

namespace ConditioningControlPanel.Services.Compositor;

/// <summary>
/// Bubble skins on the compositor path: the interior drawn over the base sprite inside the glass
/// (it rides the sprite's scale, Froth size and squash), and the pop burst that outlives the bubble.
/// Shapes and motion come from <see cref="BubbleSkins"/> (CCP.Core); this file only draws. A soap
/// bubble never reaches any of it (<see cref="BubbleItem.Skin"/> stays null). UI thread only.
/// </summary>
public sealed partial class BubbleLayer
{
    /// <summary>Glass radius as a share of half the sprite side. Measured on bubble.png: the glass spans 736 of
    /// 1024 px. Froth's GlassOfSprite (0.45) is its reach and is wider than the glass, so it is not used here.</summary>
    internal const float GlassOfHalf = 0.72f;
    /// <summary>Most skin pop bursts alive at once; a newer pop retires the oldest.</summary>
    public const int MaxLiveSkinBursts = 12;
    /// <summary>Pop bursts repaint at the field's own cadence (about 30 fps), never at refresh rate.</summary>
    private const long SkinFxStepMs = 33;

    private sealed class SkinFx
    {
        public float X, Y, R, Life;
        public long StartMs;
        /// <summary>The skin's pre-burst: the first <see cref="Pre"/> seconds draw the dying body, then the particles fly.</summary>
        public string Skin = BubbleSkinIds.Soap;
        public int Seed;
        public float Pre;
        public double BodySeconds, Motion;
        public bool Gentle;
        /// <summary>The bubble's baked pictures (owned from the pop on, freed with the burst) and its shared sprite.</summary>
        public SkinArt? Art;
        public SKImage? Sprite;
        public SkinParticle[] Parts = System.Array.Empty<SkinParticle>();
        public int Count;
    }

    private readonly List<SkinFx> _skinFx = new();
    private readonly SkinPainter _skinPaint = new();
    private long _skinDrawnMs;
    private double _skinMotion = 1;
    private long _skinMotionAt = long.MinValue;
    private bool _skinGentle;

    private bool SkinFxDue => _skinFx.Count > 0 && System.Environment.TickCount64 - _skinDrawnMs >= SkinFxStepMs;

    /// <summary>Give a fresh item its skin. Soap, an unknown id or a reserved one leaves the item exactly as it was.</summary>
    public static void ApplySkin(BubbleItem item, string? skin, int seed)
    {
        if (!BubbleSkinIds.IsDrawn(skin)) return;
        item.Skin = skin;
        item.SkinSeed = seed;
        item.SkinStartMs = System.Environment.TickCount64;
    }

    /// <summary>MotionFx (Gentle visuals included) as a scale: Full 1, Reduced half, Off a still frame.
    /// Read at most twice a second, not per bubble per frame.</summary>
    private double SkinMotion()
    {
        long now = System.Environment.TickCount64;
        if (now - _skinMotionAt > 500)
        {
            _skinMotion = MotionFx.Level switch
            {
                Models.MotionLevel.Off => 0,
                Models.MotionLevel.Reduced => .5,
                _ => 1,
            };
            _skinGentle = Safety.GentleVisuals.On;
            _skinMotionAt = now;
        }
        return _skinMotion;
    }

    /// <summary>The skin draws this bubble's body in place of the soap sprite: a shaped skin, or a pop its burst now draws.</summary>
    private static bool SkinDrawsBody(BubbleItem item) => item.SkinBodyHanded || BubbleSkins.OwnsBody(item.Skin);

    private void DrawSkinInterior(SKCanvas canvas, BubbleItem item, float cx, float cy, float half)
    {
        if (item.SkinBodyHanded) return;   // the burst draws the dying body
        double seconds = (System.Environment.TickCount64 - item.SkinStartMs) / 1000.0;
        if (item.Skin == BubbleSkinIds.Spiral) item.SkinArt ??= SkinPainter.SpiralArt(item.SkinSeed, half * GlassOfHalf);   // once per bubble
        _skinPaint.DrawInterior(canvas, item.Skin!, item.SkinSeed, seconds, SkinMotion(), cx, cy, half * GlassOfHalf, item.Opacity, item.SkinArt);
    }

    /// <summary>The bubble started its pop: its burst joins the layer once. Off motion bursts nothing.</summary>
    public void AddSkinBurst(BubbleItem item)
    {
        if (item.Skin == null || item.SkinBurst) return;
        item.SkinBurst = true;
        float r = item.SizeDip * 0.5f * item.DpiScale * item.FrothScale * GlassOfHalf * Math.Max(1f, item.Scale);
        var parts = new SkinParticle[BubbleSkins.BurstCapacity];
        double motion = SkinMotion();
        int n = BubbleSkins.Burst(item.Skin, item.SkinSeed, r, motion, parts, _skinGentle);
        if (n == 0) return;
        if (_skinFx.Count >= MaxLiveSkinBursts) { _skinFx[0].Art?.Dispose(); _skinFx.RemoveAt(0); }
        long now = System.Environment.TickCount64;
        float pre = (float)BubbleSkins.PreBurst(item.Skin, _skinGentle);
        _skinFx.Add(new SkinFx
        {
            X = (float)item.CenterXPx, Y = (float)item.CenterYPx, R = r,
            StartMs = now, Parts = parts, Count = n,
            Life = pre + BubbleSkins.LifeOf(new ReadOnlySpan<SkinParticle>(parts, 0, n)),
            Skin = item.Skin, Seed = item.SkinSeed, Pre = pre, Motion = motion, Gentle = _skinGentle,
            BodySeconds = (now - item.SkinStartMs) / 1000.0,
            Art = pre > 0 ? item.SkinArt : null, Sprite = item.Sprite,
        });
        // A skin with a pre-burst takes over drawing the dying bubble (and its pictures); game logic is not delayed.
        if (pre > 0) { item.SkinBodyHanded = true; item.SkinArt = null; }
        _dirty = true;
        SetActive(true);
    }

    /// <summary>Panic, Stop or Clear: every skin burst goes at once.</summary>
    public void ClearSkinFx()
    {
        foreach (var fx in _skinFx) fx.Art?.Dispose();
        _skinFx.Clear();
        _dirty = true;
    }

    private void DrawSkinFx(SKCanvas c)
    {
        long now = System.Environment.TickCount64;
        _skinDrawnMs = now;
        for (int i = _skinFx.Count - 1; i >= 0; i--)
        {
            var fx = _skinFx[i];
            double age = (now - fx.StartMs) / 1000.0;
            if (age >= fx.Life) { fx.Art?.Dispose(); _skinFx.RemoveAt(i); continue; }
            if (age < fx.Pre)
                _skinPaint.DrawDying(c, fx.Skin, fx.Seed, fx.BodySeconds + age, fx.Motion, age / fx.Pre, fx.Gentle, fx.X, fx.Y, fx.R,
                                     fx.Sprite, fx.Art);
            else
                _skinPaint.DrawBurst(c, new ReadOnlySpan<SkinParticle>(fx.Parts, 0, fx.Count), age - fx.Pre, fx.X, fx.Y, fx.R, 1f, fx.Seed);
        }
        if (_skinFx.Count == 0) _dirty = true;   // one last clean frame without the burst
        if (_skinFx.Count == 0 && _items.Count == 0 && _bloomFx.Count == 0) SetActive(false);
    }

    public sealed partial class BubbleItem
    {
        /// <summary>A drawn skin id, or null for soap (today's look, nothing extra drawn).</summary>
        public string? Skin;
        public int SkinSeed;
        public long SkinStartMs;
        /// <summary>Set once the pop burst was handed to the layer.</summary>
        internal bool SkinBurst;
        /// <summary>Set when the burst took over drawing the dying body (a skin with a pre-burst).</summary>
        internal bool SkinBodyHanded;
    }

    /// <summary>The Skia draw of a skin, apart from the layer so a test can render it offscreen. Paints,
    /// the shape buffer and the unit-space shaders are built once and reused: nothing native per frame.</summary>
    internal sealed partial class SkinPainter : IDisposable
    {
        private static SKColorFilter? s_goldShell;

        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SkinPrim[] _prims = new SkinPrim[BubbleSkins.MaxInterior];
        private readonly SKShader _tint = SKShader.CreateLinearGradient(new SKPoint(0, -1), new SKPoint(0, 1),
            new[] { new SKColor(255, 224, 160, 10), new SKColor(255, 196, 90, 61) }, SKShaderTileMode.Clamp);
        private readonly SKShader _mist = SKShader.CreateRadialGradient(new SKPoint(0, 0), 1,
            new[] { new SKColor(255, 240, 210, 255), new SKColor(255, 240, 210, 0) }, SKShaderTileMode.Clamp);

        /// <summary>The colour filter the base sprite wears for a skin (Champagne: a light gold film, Frost: ice), else null.
        /// App-lifetime and shared, like the sprites.</summary>
        public static SKColorFilter? ShellTint(string? skin)
            => skin == BubbleSkinIds.Champagne
                ? s_goldShell ??= SKColorFilter.CreateBlendMode(new SKColor(255, 212, 140, 110), SKBlendMode.SrcATop)
                : skin == BubbleSkinIds.Frost ? IceShell() : null;

        public void DrawInterior(SKCanvas c, string skin, int seed, double seconds, double motion,
                                 float cx, float cy, float r, float alpha, SkinArt? art = null)
        {
            if (r < 2f || alpha <= .003f) return;
            if (skin == BubbleSkinIds.Spiral) { DrawSpiral(c, seed, seconds, motion, cx, cy, r, alpha, 0, false, art, null); return; }
            if (skin == BubbleSkinIds.Marble) { DrawMarble(c, seed, seconds, motion, cx, cy, r, alpha, 0, false); return; }
            if (skin == BubbleSkinIds.Disco) { DrawDisco(c, seed, seconds, motion, cx, cy, r, alpha, 0, false); return; }
            if (skin == BubbleSkinIds.Frost) { DrawFrost(c, seed, seconds, motion, cx, cy, r, alpha, 0, null); return; }
            int n = BubbleSkins.Interior(skin, seed, seconds, motion, r, _prims);
            if (n == 0) return;
            if (skin == BubbleSkinIds.Heart) { DrawHeart(c, _prims[0], _prims[1], cx, cy, r, alpha, 1, 0); return; }
            int save = c.Save();
            c.Translate(cx, cy);
            c.Scale(r, r);
            // Gold wine tint, light at the top. A circle at the inner share is the clip: every shape
            // from BubbleSkins sits inside it by construction, so no clip path per bubble per frame.
            _fill.Shader = _tint;
            _fill.Color = SKColors.White.WithAlpha(A(alpha));
            c.DrawCircle(0, 0, (float)BubbleSkins.Inner, _fill);
            _fill.Shader = null;
            for (int i = 0; i < n; i++)
            {
                var p = _prims[i];
                float a = p.Alpha * alpha;
                if (a <= .004f) continue;
                switch (p.Kind)
                {
                    case SkinPrimKind.Ring:
                        _stroke.Color = new SKColor(255, 236, 190, A(a));
                        _stroke.StrokeWidth = Math.Max(.6f / r, p.R * .2f);
                        c.DrawCircle(p.X, p.Y, p.R, _stroke);
                        _fill.Color = new SKColor(255, 255, 255, A(a));
                        c.DrawCircle(p.X - p.R * .35f, p.Y - p.R * .35f, p.R * .2f, _fill);
                        break;
                    case SkinPrimKind.Foam:
                        _fill.Color = new SKColor(255, 250, 235, A(a));
                        c.DrawCircle(p.X, p.Y, p.R, _fill);
                        break;
                    default:
                        _fill.Color = new SKColor(255, 250, 232, A(a));
                        c.DrawCircle(p.X, p.Y, p.R, _fill);
                        break;
                }
            }
            c.RestoreToCount(save);
        }

        /// <summary>The dying body during the skin's pre-burst, k 0..1 through it (squeeze, cracks, wind-in).
        /// Under Gentle visuals nothing flashes white.</summary>
        public void DrawDying(SKCanvas c, string skin, int seed, double seconds, double motion, double k, bool gentle,
                              float cx, float cy, float r, SKImage? sprite = null, SkinArt? art = null)
        {
            if (r < 2f) return;
            if (skin == BubbleSkinIds.Spiral) DrawSpiral(c, seed, seconds, motion, cx, cy, r, 1f, (float)k, gentle, art, sprite);
            if (skin == BubbleSkinIds.Marble) DrawMarble(c, seed, seconds, motion, cx, cy, r, 1f, (float)k, gentle);
            if (skin == BubbleSkinIds.Disco) DrawDisco(c, seed, seconds, motion, cx, cy, r, 1f, (float)k, gentle);
            if (skin == BubbleSkinIds.Frost) DrawFrost(c, seed, seconds, motion, cx, cy, r, 1f, (float)k, sprite);
            if (skin == BubbleSkinIds.Heart)
            {
                if (BubbleSkins.Interior(skin, seed, seconds, motion, r, _prims) < 2) return;
                float white = gentle ? 0 : (float)Math.Max(0, (k - .5) * 1.2);
                DrawHeart(c, _prims[0], _prims[1], cx, cy, r, 1f, (float)BubbleSkins.HeartSqueeze(k), white);
            }
        }

        public void DrawBurst(SKCanvas c, ReadOnlySpan<SkinParticle> parts, double age, float cx, float cy, float r, float alpha, int seed = 0)
        {
            foreach (ref readonly var p in parts)
            {
                var st = BubbleSkins.At(p, age);
                if (!st.Alive) continue;
                float a = st.Alpha * alpha, x = cx + st.X * r, y = cy + st.Y * r, s = st.Size * r;
                if (a <= .004f || s <= 0) continue;
                switch (p.Kind)
                {
                    case SkinParticleKind.Ring:
                        _stroke.Color = new SKColor(255, 214, 140, A(.95f * a));
                        _stroke.StrokeWidth = Math.Max(.6f, s * .2f);
                        c.DrawCircle(x, y, s, _stroke);
                        if (s > 2f)
                        {
                            _fill.Color = new SKColor(255, 255, 255, A(.85f * a));
                            c.DrawCircle(x - s * .35f, y - s * .35f, s * .22f, _fill);
                        }
                        break;
                    case SkinParticleKind.Wave:
                        float u = (float)(age / p.Life);
                        // Thinner and softer than the lab's ring: a field pops many at once.
                        _stroke.Color = p.Argb != 0 ? Tone(p.Argb, .7f * a) : new SKColor(255, 214, 140, A(.7f * a));
                        _stroke.StrokeWidth = Math.Max(.5f, p.Size * r * .05f * (1 - u));
                        c.DrawCircle(x, y, s, _stroke);
                        break;
                    case SkinParticleKind.Mist:
                        int save = c.Save();
                        c.Translate(x, y);
                        c.Scale(s, s);
                        _fill.Shader = _mist;
                        _fill.Color = SKColors.White.WithAlpha(A(.3f * a));
                        c.DrawCircle(0, 0, 1, _fill);
                        _fill.Shader = null;
                        c.RestoreToCount(save);
                        break;
                    case SkinParticleKind.Spark:
                        _fill.Color = new SKColor(255, 236, 190, A(.9f * a));
                        c.DrawCircle(x, y, s, _fill);
                        break;
                    case SkinParticleKind.Shard:
                    case SkinParticleKind.Star:
                        DrawMarbleParticle(c, p, st, seed, x, y, s, r, a);
                        break;
                    case SkinParticleKind.Heart:
                    case SkinParticleKind.HeartWave:
                        DrawHeartParticle(c, p, st, age, x, y, s, a);
                        break;
                    case SkinParticleKind.Square:
                    case SkinParticleKind.Beam:
                        DrawDiscoParticle(c, p, st, age, x, y, s, a);
                        break;
                    case SkinParticleKind.Flake:
                    case SkinParticleKind.Stick:
                    case SkinParticleKind.Puff:
                        DrawFrostParticle(c, p, st, x, y, s, r, a);
                        break;
                    default:
                        _fill.Color = p.Argb != 0 ? Tone(p.Argb, a) : new SKColor(255, 244, 214, A(.9f * a));
                        c.DrawCircle(x, y, s, _fill);
                        break;
                }
            }
        }

        private static byte A(float a) => (byte)Math.Clamp(a * 255f, 0f, 255f);

        /// <summary>A skin colour (0xAARRGGBB) with its own alpha times <paramref name="a"/>.</summary>
        private static SKColor Tone(uint argb, float a) => new SKColor(argb).WithAlpha(A((argb >> 24) / 255f * a));

        /// <summary>Each skin's own native parts (paths, paints) go with the painter.</summary>
        partial void DisposeParts();

        public void Dispose()
        {
            _fill.Dispose();
            _stroke.Dispose();
            _tint.Dispose();
            _mist.Dispose();
            DisposeParts();
        }
    }
}
