using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Bouncing Text in the live preview (sweep lane P2, owner 2026-11-24). The real effect makes a
    /// window per screen, so this is a drawn stand-in: a neutral word ("DEEPER", and "RELAX" with
    /// Second text on) bouncing off the edges of the visible band like the real one. It follows Speed,
    /// Size, Opacity, Font, the colour mode, Outline and the cheap Effects switches (breathing, wobble,
    /// spin, velocity tilt, squash on a hit, a corner flash). With the Scrawl gold box lit, the word
    /// leaves stamps where it hits a wall, which fade after Scrawl's own "stays" time, capped by its cap
    /// and sized by its stamp size. A click on the word makes it jump. Never the user's own words.
    /// </summary>
    public sealed class BouncingTextPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.BouncingTextSpeed), nameof(AppSettings.BouncingTextSize), nameof(AppSettings.BouncingTextOpacity),
            nameof(AppSettings.BouncingTextColorMode), nameof(AppSettings.BouncingTextFixedColor), nameof(AppSettings.BouncingTextFont),
            nameof(AppSettings.BouncingTextFxBreathing), nameof(AppSettings.BouncingTextFxWobble), nameof(AppSettings.BouncingTextFxSpin),
            nameof(AppSettings.BouncingTextFxVelocityTilt), nameof(AppSettings.BouncingTextFxSquashStretch),
            nameof(AppSettings.BouncingTextFxCornerBurst), nameof(AppSettings.BouncingTextOutline), nameof(AppSettings.BouncingTextSecondText),
            nameof(AppSettings.SuperModes), nameof(AppSettings.ScrawlStampSeconds), nameof(AppSettings.ScrawlStampCap),
            nameof(AppSettings.ScrawlStampSize),
        };

        public static readonly string[] Words = { "DEEPER", "RELAX" };
        /// <summary>Text height at Size 100, as a share of the slice's width.</summary>
        public const double SizeShare = 0.13;
        /// <summary>A preview stamp stays at most this long (the real one can stay a minute).</summary>
        public const double StampSecondsCap = 8;
        /// <summary>At most this many stamps on the slice.</summary>
        public const int StampCapCap = 12;
        private const double SquashSeconds = 0.25, BurstSeconds = 0.6, JumpSeconds = 0.45;

        private static readonly SKColor[] Palette =
        {
            new(0xff, 0x69, 0xb4), new(0x5e, 0xa8, 0xff), new(0xff, 0xb3, 0x47), new(0x5c, 0xe0, 0xa5),
            new(0xb5, 0x8c, 0xff), new(0xff, 0x5f, 0xd2), new(0xff, 0xff, 0xff),
        };

        private sealed class Word
        {
            public string Text = "";
            public double X, Y, Vx, Vy, HitAt = -9, HitAxisX, JumpAt = -9, Phase;
            public SKColor Colour;
            public float W, H;
        }

        private sealed class Stamp
        {
            public string Text = "";
            public double X, Y, Born, Angle;
            public SKColor Colour;
            public float Size;
        }

        private readonly List<Word> _words = new();
        private readonly List<Stamp> _stamps = new();
        private readonly List<(double X, double Y, double At)> _bursts = new();
        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<bool> _scrawl;
        private readonly Random _rng;
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round };
        private static AppSettings? _defaults;
        private SKTypeface? _face;
        private string? _faceName;

        private SKRect _screen, _band;
        private double _t;
        private bool _disposed, _started;

        /// <summary>The host builds a scene by its id with no arguments (EffectPreview.Create).</summary>
        public BouncingTextPreviewScene() : this(settings: null) { }

        public BouncingTextPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<bool>? scrawl = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _scrawl = scrawl ?? (() => SuperAccess.IsSelected(SuperEffect.Scrawl));
            _rng = rng ?? new Random();
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public int WordCount => _words.Count;
        public int Stamps => _stamps.Count;
        public double Clock => _t;
        public SKRect SpawnBand => _band;
        internal IReadOnlyList<(double X, double Y, float W, float H, double Vy)> Placed
        {
            get
            {
                var list = new List<(double, double, float, float, double)>();
                foreach (var w in _words) list.Add((w.X, w.Y, w.W, w.H, w.Vy));
                return list;
            }
        }

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private float FieldBottom => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, FieldBottom), bottom = Math.Clamp(visible.Bottom, screen.Top, FieldBottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(FieldBottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
            foreach (var w in _words) Keep(w);
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            Rebuild();
        }

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            if (_words.Count == 0) Rebuild();
            var s = S;
            double speed = Speed(s) * (_level() == MotionLevel.Reduced ? 0.5 : 1);
            foreach (var w in _words)
            {
                w.X += w.Vx * speed * dt;
                w.Y += w.Vy * speed * dt;
                // A jump is a short kick upward (set by the click) that settles back to the bounce speed.
                if (_t - w.JumpAt >= JumpSeconds) w.Vy = Math.Sign(w.Vy == 0 ? 1 : w.Vy) * Math.Max(Math.Abs(w.Vx) * 0.8, Math.Min(Math.Abs(w.Vy), Math.Abs(w.Vx) * 1.6));
                bool hitX = false, hitY = false;
                if (w.X < _band.Left + 2) { w.X = _band.Left + 2; w.Vx = Math.Abs(w.Vx); hitX = true; }
                else if (w.X + w.W > _band.Right - 2) { w.X = _band.Right - 2 - w.W; w.Vx = -Math.Abs(w.Vx); hitX = true; }
                if (w.Y < _band.Top + 2) { w.Y = _band.Top + 2; w.Vy = Math.Abs(w.Vy); hitY = true; }
                else if (w.Y + w.H > _band.Bottom - 2) { w.Y = _band.Bottom - 2 - w.H; w.Vy = -Math.Abs(w.Vy); hitY = true; }
                if (hitX || hitY) Hit(w, s, hitX, hitY);
            }
            double stay = Math.Min(StampSecondsCap, ScrawlOptions.ClampStampSeconds(s.ScrawlStampSeconds));
            _stamps.RemoveAll(p => _t - p.Born > stay);
            _bursts.RemoveAll(b => _t - b.At > BurstSeconds);
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            var s = S;
            var face = Face(s);
            double alpha = Math.Clamp(s.BouncingTextOpacity / 100.0, 0, 1);
            double stay = Math.Min(StampSecondsCap, ScrawlOptions.ClampStampSeconds(s.ScrawlStampSeconds));
            _fill.Typeface = face;
            _line.Typeface = face;
            foreach (var p in _stamps)
            {
                double age = _t - p.Born, a = Math.Clamp(Math.Min(age / 0.12, (stay - age) / Math.Max(0.3, stay * 0.3)), 0, 1) * 0.75 * alpha;
                canvas.Save();
                canvas.RotateDegrees((float)p.Angle, (float)p.X, (float)p.Y);
                _fill.TextSize = p.Size;
                _fill.TextAlign = SKTextAlign.Center;
                _line.TextSize = p.Size;
                _line.TextAlign = SKTextAlign.Center;
                _line.StrokeWidth = Math.Max(1.5f, p.Size * 0.08f);
                _line.Color = new SKColor(20, 8, 30, (byte)(200 * a));
                canvas.DrawText(p.Text, (float)p.X, (float)p.Y + p.Size * 0.35f, _line);
                _fill.Color = p.Colour.WithAlpha((byte)(255 * a));
                canvas.DrawText(p.Text, (float)p.X, (float)p.Y + p.Size * 0.35f, _fill);
                canvas.Restore();
            }
            foreach (var b in _bursts)
            {
                double p = (_t - b.At) / BurstSeconds;
                _line.Color = SKColors.White.WithAlpha((byte)(220 * (1 - p)));
                _line.StrokeWidth = 3;
                canvas.DrawCircle((float)b.X, (float)b.Y, (float)(10 + 70 * p), _line);
            }
            foreach (var w in _words) DrawWord(canvas, w, s, face, alpha);
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            switch (name)
            {
                case nameof(AppSettings.BouncingTextSize):
                case nameof(AppSettings.BouncingTextFont):
                case nameof(AppSettings.BouncingTextSecondText):
                    Rebuild();
                    break;
                case nameof(AppSettings.BouncingTextColorMode):
                case nameof(AppSettings.BouncingTextFixedColor):
                    foreach (var w in _words) w.Colour = NextColour(S, w.Colour);
                    break;
                case nameof(AppSettings.SuperModes):
                    if (!_scrawl()) _stamps.Clear();
                    break;
                case nameof(AppSettings.ScrawlStampCap):
                    TrimStamps(S);
                    break;
            }
        }

        public void Click(SKPoint at)
        {
            if (_disposed) return;
            foreach (var w in _words)
            {
                if (at.X < w.X - 6 || at.X > w.X + w.W + 6 || at.Y < w.Y - 6 || at.Y > w.Y + w.H + 6) continue;
                w.JumpAt = _t;
                w.Vy = -Math.Max(Math.Abs(w.Vy) * 2.2, 260);
                w.HitAt = _t; w.HitAxisX = 0;
                return;
            }
        }

        // ---------------------------------------------------------------- driver

        /// <summary>Speed 1..10 in DIP a second over the slice.</summary>
        private double Speed(AppSettings s) => (30 + 18 * Math.Clamp(s.BouncingTextSpeed, 1, 10)) / 100.0;

        private float TextSize(AppSettings s) => (float)(_screen.Width * SizeShare * Math.Clamp(s.BouncingTextSize, 50, 300) / 100.0);

        private SKTypeface Face(AppSettings s)
        {
            var name = string.IsNullOrWhiteSpace(s.BouncingTextFont) ? "Segoe UI" : s.BouncingTextFont;
            if (_face != null && _faceName == name) return _face;
            if (_face != null && !ReferenceEquals(_face, SKTypeface.Default)) _face.Dispose();
            _face = SKTypeface.FromFamilyName(name, SKFontStyle.Bold) ?? SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
            _faceName = name;
            return _face;
        }

        private void Rebuild()
        {
            if (_screen.Width <= 0) return;
            var s = S;
            var old = _words.Count > 0 ? _words[0] : null;
            _words.Clear();
            int n = s.BouncingTextSecondText ? 2 : 1;
            float size = Math.Min(TextSize(s), Math.Max(18, _band.Height * 0.3f));
            _fill.Typeface = Face(s);
            _fill.TextSize = size;
            for (int i = 0; i < n; i++)
            {
                var text = Words[i];
                float w = Math.Min(_fill.MeasureText(text), _screen.Width - 8);
                var word = new Word
                {
                    Text = text, W = w, H = size, Phase = _rng.NextDouble() * 6,
                    X = old != null && i == 0 ? old.X : _band.Left + 8 + _rng.NextDouble() * Math.Max(1, _band.Width - w - 16),
                    Y = old != null && i == 0 ? old.Y : _band.Top + 8 + _rng.NextDouble() * Math.Max(1, _band.Height - size - 16),
                    Vx = (i == 0 ? 1 : -1) * (100 + _rng.NextDouble() * 30),
                    Vy = (i == 0 ? 1 : -1) * (75 + _rng.NextDouble() * 30),
                };
                word.Colour = NextColour(s, default);
                Keep(word);
                _words.Add(word);
            }
        }

        private void Keep(Word w)
        {
            w.X = Math.Clamp(w.X, _band.Left + 2, Math.Max(_band.Left + 2, _band.Right - 2 - w.W));
            w.Y = Math.Clamp(w.Y, _band.Top + 2, Math.Max(_band.Top + 2, _band.Bottom - 2 - w.H));
        }

        private SKColor NextColour(AppSettings s, SKColor current)
        {
            switch (s.BouncingTextColorMode)
            {
                case 1:
                    return SKColor.TryParse(s.BouncingTextFixedColor ?? "", out var c) && !string.IsNullOrWhiteSpace(s.BouncingTextFixedColor)
                        ? c.WithAlpha(255) : SKColors.HotPink;
                case 2:
                    return SKColors.HotPink;   // rainbow: the hue moves with time at draw
                default:
                    SKColor next;
                    do next = Palette[_rng.Next(Palette.Length)]; while (next == current);
                    return next;
            }
        }

        private void Hit(Word w, AppSettings s, bool hitX, bool hitY)
        {
            w.HitAt = _t;
            w.HitAxisX = hitX ? 1 : 0;
            if (s.BouncingTextColorMode == 0) w.Colour = NextColour(s, w.Colour);
            bool corner = hitX && hitY;
            if (!corner)
            {
                // Near a corner counts too (the real effect is generous): both walls within a word's height.
                bool nearX = w.X <= _band.Left + 8 || w.X + w.W >= _band.Right - 8;
                bool nearY = w.Y <= _band.Top + 8 || w.Y + w.H >= _band.Bottom - 8;
                corner = nearX && nearY;
            }
            if (corner && s.BouncingTextFxCornerBurst && !_gentle() && _level() == MotionLevel.Full)
                _bursts.Add((hitX ? (w.Vx > 0 ? w.X : w.X + w.W) : w.X + w.W / 2, hitY ? (w.Vy > 0 ? w.Y : w.Y + w.H) : w.Y + w.H / 2, _t));
            if (_scrawl())
            {
                float size = (float)(w.H * 0.55 * ScrawlOptions.ClampStampSize(s.ScrawlStampSize) / 100.0);
                double x = w.X + w.W / 2, y = w.Y + w.H / 2;
                if (hitX) x = w.Vx > 0 ? w.X + w.W * 0.3 : w.X + w.W * 0.7;
                if (hitY) y = w.Vy > 0 ? w.Y + w.H * 0.3 : w.Y + w.H * 0.7;
                _stamps.Add(new Stamp { Text = w.Text, X = x, Y = y, Born = _t, Colour = w.Colour, Size = size, Angle = _rng.NextDouble() * 24 - 12 });
                TrimStamps(s);
            }
        }

        private void TrimStamps(AppSettings s)
        {
            // The slice is a sliver of a screen: the real cap, but never more than a sliver's worth.
            int cap = Math.Min(StampCapCap, ScrawlOptions.EffectiveCap(ScrawlOptions.ClampStampCap(s.ScrawlStampCap), ScrawlOptions.ClampStampSize(s.ScrawlStampSize), _words.Count));
            if (_stamps.Count > cap) _stamps.RemoveRange(0, _stamps.Count - cap);
        }

        private void DrawWord(SKCanvas canvas, Word w, AppSettings s, SKTypeface face, double alpha)
        {
            var level = _level();
            bool moving = level == MotionLevel.Full;
            double age = _t + w.Phase;
            float cx = (float)(w.X + w.W / 2), cy = (float)(w.Y + w.H / 2);
            float sx = 1, sy = 1, angle = 0;
            if (moving && s.BouncingTextFxBreathing) { float k = 1 + 0.06f * (float)Math.Sin(age * 2.4); sx *= k; sy *= k; }
            if (moving && s.BouncingTextFxWobble) angle += 6f * (float)Math.Sin(age * 3.1);
            if (moving && s.BouncingTextFxSpin) angle += (float)(age * 60 % 360);
            if (moving && s.BouncingTextFxVelocityTilt) angle += (float)Math.Clamp(w.Vx / 20.0, -8, 8);
            double since = _t - w.HitAt;
            if (s.BouncingTextFxSquashStretch && !Gentle && since < SquashSeconds)
            {
                float q = (float)(0.22 * (1 - since / SquashSeconds));
                if (w.HitAxisX > 0) { sx -= q; sy += q * 0.6f; } else { sy -= q; sx += q * 0.6f; }
            }
            if (_t - w.JumpAt < JumpSeconds && !Gentle) { float k = 1 + 0.18f * (float)(1 - (_t - w.JumpAt) / JumpSeconds); sx *= k; sy *= k; }
            var colour = s.BouncingTextColorMode == 2 ? SKColor.FromHsv((float)(_t * 60 % 360), 70, 100) : w.Colour;

            canvas.Save();
            canvas.Translate(cx, cy);
            if (angle != 0) canvas.RotateDegrees(angle);
            canvas.Scale(sx, sy);
            _fill.Typeface = face; _fill.TextSize = w.H; _fill.TextAlign = SKTextAlign.Center;
            float baseline = w.H * 0.35f;
            float fit = _fill.MeasureText(w.Text) > w.W ? w.W / _fill.MeasureText(w.Text) : 1f;
            if (fit < 1) canvas.Scale(fit, fit);
            if (s.BouncingTextOutline)
            {
                _line.Typeface = face; _line.TextSize = w.H; _line.TextAlign = SKTextAlign.Center;
                _line.StrokeWidth = Math.Max(2f, w.H * 0.09f);
                _line.Color = new SKColor(10, 6, 20, (byte)(230 * alpha));
                canvas.DrawText(w.Text, 0, baseline, _line);
            }
            else
            {
                _fill.Color = new SKColor(0, 0, 0, (byte)(90 * alpha));
                canvas.DrawText(w.Text, 2, baseline + 2, _fill);
            }
            _fill.Color = colour.WithAlpha((byte)(255 * alpha));
            canvas.DrawText(w.Text, 0, baseline, _fill);
            canvas.Restore();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _words.Clear();
            _stamps.Clear();
            _fill.Dispose();
            _line.Dispose();
            if (_face != null && !ReferenceEquals(_face, SKTypeface.Default)) _face.Dispose();
        }
    }
}
