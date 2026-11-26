using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Services.Super
{
    /// <summary>One stamp on the walls, in virtual-desktop DIP. <see cref="Look"/> is the head's cached glyph geometry.</summary>
    public sealed class ScrawlStamp
    {
        public string Word = "";
        public double Size;                     // font size the stamp is built at, DIP
        public int Ink;                         // 0 pink, 1 mint, 2 lilac, 3 gold
        public bool Slam, Gold;
        public double X, Y, Age, Reach, Pulse = -1;
        public double HalfW, HalfH, Rot, Phase;  // Rot in radians, Phase for the idle breath and drip sway
        public bool Splatted;                    // the ink splat fired at contact
        public double[] Drips = Array.Empty<double>();   // per drip: x offset (-1..1 of half width), start delay 0..1, speed 0.7..1.3
        public (double X, double Y, double R)[] Dots = Array.Empty<(double, double, double)>();   // stamp space
        public object? Look;
    }

    public struct ScrawlGlow { public double X, Y, Age; public int Ink; }
    public struct ScrawlWave { public double X, Y, Age, Reach; }
    public struct ScrawlSpark { public double X, Y, Vx, Vy, G, T, Life; public int Ink; }

    /// <summary>
    /// Super Scrawl's live state, shared by both heads (moved out of the WPF ScrawlScene for the Linux
    /// head, 2026-11-25): wall stamps, wall glows, slam shockwaves, ink sparks, the corner label and the
    /// shake, all in virtual-desktop DIP. The head feeds it hits and the frame clock and only draws.
    /// The numbers live in <see cref="ScrawlRules"/>. UI thread only.
    /// </summary>
    public sealed class ScrawlField
    {
        public const int Gold = 3, White = 4, MaxSparks = 400;

        private readonly List<ScrawlStamp> _stamps = new();
        private readonly List<ScrawlGlow> _glows = new();
        private readonly List<ScrawlWave> _waves = new();
        private readonly ScrawlSpark[] _sparks = new ScrawlSpark[MaxSparks];
        private int _sparkCount;
        private readonly Random _rng;
        private readonly Func<string, double, (double W, double H)> _measure;
        private readonly Func<string> _labelText;
        private MotionLevel _motion = MotionLevel.Full;
        private double _shake;
        private double _life = ScrawlRules.StampLife, _slamShake = 1;
        private int _cap = ScrawlRules.StampCap;
        private bool _cornerLabel = true;

        /// <summary>The stamps' font size (the words' size times Stamp size) and its mockup ratio (stroke widths).</summary>
        public double FontSize { get; }
        public double K { get; }
        public MotionLevel Motion => _motion;
        public IReadOnlyList<ScrawlStamp> Stamps => _stamps;
        public IReadOnlyList<ScrawlGlow> Glows => _glows;
        public IReadOnlyList<ScrawlWave> Waves => _waves;
        public ReadOnlySpan<ScrawlSpark> Sparks => _sparks.AsSpan(0, _sparkCount);

        /// <summary>The CORNER label: its text, where it floats from, its age (-1 = none) and a serial that moves per new label.</summary>
        public string? LabelText { get; private set; }
        public double LabelX { get; private set; }
        public double LabelY { get; private set; }
        public double LabelAge { get; private set; } = -1;
        public double LabelSize => Math.Min(FontSize * 0.9, ScrawlRules.CornerLabelMaxDip);
        public int LabelSerial { get; private set; }

        /// <summary>The shake offset for this frame, in DIP. The head moves its canvas by it.</summary>
        public double ShakeX { get; private set; }
        public double ShakeY { get; private set; }
        public double StampLife => _life;
        public int Cap => _cap;
        public int LiveSparks => _sparkCount;

        /// <summary>Anything left to draw or shake. Layers stop invalidating once this goes false.</summary>
        public bool IsLive => _stamps.Count > 0 || _glows.Count > 0 || _waves.Count > 0 || _sparkCount > 0
                              || LabelAge >= 0 || _shake > 0;

        /// <param name="measure">The head's text measure: word and font size to its box in DIP.</param>
        /// <param name="labelText">The localised CORNER label, read on each corner hit.</param>
        public ScrawlField(double fontSize, Func<string, double, (double W, double H)> measure, Func<string> labelText, Random? rng = null)
        {
            FontSize = Math.Max(8, fontSize);
            K = Math.Max(1, FontSize / ScrawlRules.MockupFont);
            _measure = measure;
            _labelText = labelText;
            _rng = rng ?? new Random();
        }

        /// <summary>
        /// The option box, read live by the head: how long a wall stamp stays (s), how many stamps the
        /// walls hold, the slam's shake multiplier and the corner word (Corner flash). A lower cap drops the oldest at once.
        /// </summary>
        public void Configure(double stampLife, int stampCap, double slamShake, bool cornerLabel = true)
        {
            _cornerLabel = cornerLabel;
            _life = Math.Max(0.5, stampLife);
            _slamShake = Math.Max(0, slamShake);
            int cap = Math.Max(1, stampCap);
            if (cap == _cap) return;
            _cap = cap;
            TrimToCap();
        }

        /// <summary>A word hit a wall. A corner (both axes near) gets the gold stamp; anything else a wall stamp.</summary>
        public void WallHit(string word, ScrawlWall wall, double left, double top, double right, double bottom,
            ScrawlBounds b, double labelX, double labelY)
        {
            if (wall == ScrawlWall.None || string.IsNullOrEmpty(word)) return;
            double cx = (left + right) / 2, cy = (top + bottom) / 2, fs = FontSize;
            int ink = ScrawlRules.InkFor(word);
            var (glx, gly) = ScrawlRules.GlowPoint(wall, cx, cy, b);
            EmitBounce(glx, gly, wall, ink);
            if (ScrawlRules.IsCorner(left, top, right, bottom, b, fs))
            {
                var p = ScrawlRules.PlaceCornerStamp(cx, cy, b, fs);
                AddStamp(word, p, ScrawlRules.CornerStampScale, Gold, gold: true, slam: false, dots: 0, 0, 0, 0);
                var (gx, gy) = ScrawlRules.GlowPoint(cx < (b.MinX + b.MaxX) / 2 ? ScrawlWall.Left : ScrawlWall.Right, cx, cy, b);
                var (hx, hy) = ScrawlRules.GlowPoint(cy < (b.MinY + b.MaxY) / 2 ? ScrawlWall.Top : ScrawlWall.Bottom, cx, cy, b);
                _glows.Add(new ScrawlGlow { X = gx, Y = gy, Ink = Gold });
                _glows.Add(new ScrawlGlow { X = hx, Y = hy, Ink = Gold });
                var (sx, sy) = ScrawlRules.CornerPoint(cx, cy, b, fs);
                Emit(sx, sy, ScrawlRules.CornerSparks, Gold, 280, 1.0);
                Emit(sx, sy, ScrawlRules.CornerWhiteSparks, White, 200, 0.8);
                _shake = Math.Max(_shake, ScrawlRules.ShakeCorner);
                // "Corner flash" off: the gold stamp still lands, the word stays away (tier-2 report, 7.3.2).
                if (!_cornerLabel) return;
                LabelText = _labelText();
                LabelX = labelX; LabelY = labelY; LabelAge = 0; LabelSerial++;
                return;
            }
            var pose = ScrawlRules.PlaceStamp(wall, cx, cy, b, fs, _rng.NextDouble() * 2 - 1);
            AddStamp(word, pose, ScrawlRules.StampScale, ink, gold: false, slam: false, ScrawlRules.InkDots, 0.9, 2.0, 0.55);
            _glows.Add(new ScrawlGlow { X = glx, Y = gly, Ink = ink });
        }

        /// <summary>A slammed word arrived at the centre: the huge stamp, the shockwave, the shake, every stamp flares.</summary>
        public void SlamLanded(string word, double x, double y, double screenWidth)
        {
            _shake = ScrawlRules.ShakeSlam * _slamShake;
            foreach (var s in _stamps) if (!s.Slam) s.Pulse = 0;
            AddStamp(word, new StampPose(x, y, -0.14), ScrawlRules.SlamStampScale, 1, gold: false, slam: true,
                ScrawlRules.SlamDots, 2.2, 4.4, 0.6);
            if (_motion != MotionLevel.Off)
                _waves.Add(new ScrawlWave { X = x, Y = y, Reach = screenWidth * ScrawlRules.WaveReach });
            Emit(x, y, ScrawlRules.SlamMintSparks, 1, 300, 0.9);
            Emit(x, y, ScrawlRules.SlamPinkSparks, 0, 240, 0.9);
        }

        /// <summary>Advance every timer by dt. <paramref name="motion"/> and <paramref name="photosafe"/> are read once per frame by the caller.</summary>
        public void Tick(double dt, MotionLevel motion, bool photosafe = false)
        {
            _motion = motion;
            for (int i = _stamps.Count - 1; i >= 0; i--)
            {
                var s = _stamps[i];
                s.Age += dt;
                if (s.Pulse >= 0) s.Pulse = s.Pulse + dt > ScrawlRules.PulseLife ? -1 : s.Pulse + dt;
                if (!s.Slam && !s.Splatted && s.Age >= ScrawlRules.ContactTime(false)) { s.Splatted = true; Splat(s); }
                if (s.Age >= (s.Slam ? ScrawlRules.SlamStampLife : _life)) _stamps.RemoveAt(i);
            }
            for (int i = _glows.Count - 1; i >= 0; i--)
            {
                var g = _glows[i]; g.Age += dt;
                if (g.Age >= ScrawlRules.GlowLife) _glows.RemoveAt(i); else _glows[i] = g;
            }
            for (int i = _waves.Count - 1; i >= 0; i--)
            {
                var w = _waves[i]; w.Age += dt;
                if (w.Age >= ScrawlRules.WaveLife) _waves.RemoveAt(i); else _waves[i] = w;
            }
            double drag = Math.Max(0, 1 - dt * 2);
            for (int i = _sparkCount - 1; i >= 0; i--)
            {
                ref var p = ref _sparks[i];
                p.T += dt;
                if (p.T >= p.Life) { _sparks[i] = _sparks[--_sparkCount]; continue; }
                p.Vy += p.G * dt;
                p.X += p.Vx * dt; p.Y += p.Vy * dt; p.Vx *= drag; p.Vy *= drag;
            }
            if (LabelAge >= 0) { LabelAge += dt; if (LabelAge >= ScrawlRules.FloatLife) { LabelAge = -1; LabelText = null; } }

            double gain = ScrawlRules.ShakeGain(motion, photosafe);
            if (_shake > 0 && gain > 0)
            {
                double amp = ScrawlRules.ShakePixels * K * _shake * gain;
                ShakeX = (_rng.NextDouble() - 0.5) * amp;
                ShakeY = (_rng.NextDouble() - 0.5) * amp;
            }
            else { ShakeX = 0; ShakeY = 0; }
            _shake = ScrawlRules.DecayShake(_shake, dt);
        }

        /// <summary>Every stamp's box in DIP (x, y, w, h), for the OCR self-exclusion list (the stamps are the player's text too).</summary>
        public void AppendRects(List<(double X, double Y, double W, double H)> into)
        {
            foreach (var s in _stamps)
                into.Add((s.X - s.Reach, s.Y - s.Reach, s.Reach * 2, s.Reach * 2));
        }

        public void Clear()
        {
            _stamps.Clear(); _glows.Clear(); _waves.Clear(); _sparkCount = 0;
            LabelText = null; LabelAge = -1; _shake = 0; ShakeX = 0; ShakeY = 0;
        }

        private void AddStamp(string word, StampPose pose, double scale, int ink, bool gold, bool slam,
            int dots, double near, double far, double flatten)
        {
            double size = FontSize * scale;
            var (w, h) = _measure(word, size);
            var dot = Array.Empty<(double, double, double)>();
            if (dots > 0)
            {
                dot = new (double, double, double)[dots];
                ScrawlRules.Dots(_rng, dots, FontSize, near, far, flatten, 0.8 * K, (slam ? 3.4 : 2.4) * K, dot);
            }
            _stamps.Add(new ScrawlStamp
            {
                Word = word, Size = size, Dots = dot, Ink = ink, Gold = gold, Slam = slam, X = pose.X, Y = pose.Y,
                Reach = Math.Max(w, h) / 2 + (dots > 0 ? far * FontSize : 0),
                HalfW = w / 2, HalfH = h / 2, Rot = pose.Rotation, Phase = _rng.NextDouble() * Math.PI * 2,
                Drips = slam ? Array.Empty<double>() : MakeDrips(),
            });
            if (!slam) TrimToCap();
        }

        /// <summary>Past the cap, the oldest wall stamps go first. The slam stamp is never dropped.</summary>
        private void TrimToCap()
        {
            while (_stamps.Count > _cap)
            {
                int oldest = _stamps.FindIndex(s => !s.Slam);
                if (oldest < 0) break;
                _stamps.RemoveAt(oldest);
            }
        }

        private double[] MakeDrips()
        {
            int n = 3 + _rng.Next(ScrawlRules.MaxDrips - 2);
            var d = new double[n * 3];
            for (int i = 0; i < n; i++)
            {
                d[i * 3] = _rng.NextDouble() * 1.7 - 0.85;
                d[i * 3 + 1] = _rng.NextDouble();
                d[i * 3 + 2] = 0.7 + _rng.NextDouble() * 0.6;
            }
            return d;
        }

        private double SpeedGain => _motion == MotionLevel.Reduced ? 0.5 : 1;

        private void Emit(double x, double y, int n, int ink, double speed, double life)
        {
            n = ScrawlRules.SparkCount(n, _motion);
            double sp = speed * K * SpeedGain;
            for (int i = 0; i < n; i++)
            {
                double a = _rng.NextDouble() * Math.PI * 2, s = sp * (0.3 + _rng.NextDouble() * 0.7);
                Spawn(x, y, Math.Cos(a) * s, Math.Sin(a) * s, 0, life * (0.6 + _rng.NextDouble() * 0.6), ink);
            }
        }

        /// <summary>A word bounced: a few sparks in its ink, thrown into the field off the wall it hit.</summary>
        private void EmitBounce(double x, double y, ScrawlWall wall, int ink)
        {
            int n = ScrawlRules.SparkCount(ScrawlRules.BounceSparks, _motion);
            double sp = ScrawlRules.BounceSparkSpeed * K * SpeedGain;
            for (int i = 0; i < n; i++)
            {
                double a = ScrawlRules.BounceSparkAngle(wall, _rng.NextDouble() * 2 - 1), s = sp * (0.45 + _rng.NextDouble() * 0.55);
                Spawn(x, y, Math.Cos(a) * s, Math.Sin(a) * s, 0, ScrawlRules.BounceSparkLife * (0.6 + _rng.NextDouble() * 0.6), ink);
            }
        }

        /// <summary>The stamp met the wall: a tiny ink splat out from under its edge, flat along the wall, falling a little.</summary>
        private void Splat(ScrawlStamp st)
        {
            int n = ScrawlRules.SparkCount(ScrawlRules.SplatSparks, _motion);
            double sp = ScrawlRules.SplatSpeed * K * SpeedGain, g = ScrawlRules.SplatGravity * K * SpeedGain;
            double cr = Math.Cos(st.Rot), sr = Math.Sin(st.Rot);
            for (int i = 0; i < n; i++)
            {
                double a = _rng.NextDouble() * Math.PI * 2, s = sp * (0.4 + _rng.NextDouble() * 0.6);
                double ca = Math.Cos(a), sa = Math.Sin(a) * ScrawlRules.SplatFlatten;
                double ox = ca * st.HalfW * 0.8, oy = sa * st.HalfH * 1.6;   // start at the stamp's rim, stamp space
                Spawn(st.X + ox * cr - oy * sr, st.Y + ox * sr + oy * cr, (ca * cr - sa * sr) * s, (ca * sr + sa * cr) * s, g,
                    ScrawlRules.SplatLife * (0.6 + _rng.NextDouble() * 0.6), st.Ink);
            }
        }

        private void Spawn(double x, double y, double vx, double vy, double g, double life, int ink)
        {
            if (_sparkCount >= MaxSparks) return;
            _sparks[_sparkCount++] = new ScrawlSpark { X = x, Y = y, Vx = vx, Vy = vy, G = g, Life = life, Ink = ink };
        }
    }
}
