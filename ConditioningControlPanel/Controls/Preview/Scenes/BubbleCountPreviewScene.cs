using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Bubble Count in the live preview (sweep lane P2, owner 2026-11-24). The real round is a video
    /// window with a question window after it, so this is a drawn stand-in of one round: a handful of
    /// bubbles (the real <see cref="BubbleLayer"/> glass, as on the Bubbles page) float up and pop in
    /// turn, then a small number pad card with a "?" appears in the band and the answer's digits light
    /// one by one. Difficulty sets how many (Easy 3, Medium 5, Hard 8, give or take one, as the real
    /// window's base rate), the frequency sets the rest between rounds, Strict lock puts a padlock on the
    /// card. A click pops a bubble early. No sound, no real question window, never a setting written.
    /// </summary>
    public sealed class BubbleCountPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.BubbleCountFrequency), nameof(AppSettings.BubbleCountDifficulty), nameof(AppSettings.BubbleCountStrictLock),
        };

        public const double SpawnEvery = 0.4, BubbleLife = 1.1, PopSeconds = 0.2, DigitEvery = 0.4, CardHold = 1.6, CardFade = 0.3;

        public enum Phase { Rest, Bubbles, Card }

        private sealed class Bubble
        {
            public BubbleLayer.BubbleItem Item = null!;
            public double X, Y, Born, PopAt = -1, Phase, Size;
            public bool Popping => PopAt >= 0;
        }

        private readonly CompositorEngine _engine = new();
        private readonly BubbleLayer _layer;
        private readonly ChaosFxLayer _fx;
        private readonly List<Bubble> _bubbles = new();
        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Random _rng;
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _line = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKTypeface _bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _phaseAt, _next = 0.3;
        private int _target, _spawned, _popped;
        private bool _disposed;

        /// <summary>The host builds a scene by its id with no arguments (EffectPreview.Create).</summary>
        public BubbleCountPreviewScene() : this(settings: null) { }

        public BubbleCountPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _rng = rng ?? new Random();
            _engine.Dispose();
            _layer = new BubbleLayer(_engine);
            _fx = ChaosFxLayerWpf.Create(_engine);
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public Phase Now { get; private set; } = Phase.Rest;
        public int Target => _target;
        public int Popped => _popped;
        public int Rounds { get; private set; }
        public int OnScreen { get { int n = 0; foreach (var b in _bubbles) if (!b.Popping) n++; return n; } }
        /// <summary>Digits of the answer lit on the card so far.</summary>
        public int DigitsLit => Now != Phase.Card ? 0 : Math.Min(Answer.Length, (int)((_t - _phaseAt) / DigitEvery));
        public double Clock => _t;
        public SKRect SpawnBand => _band;
        internal IEnumerable<BubbleLayer.BubbleItem> Items { get { foreach (var b in _bubbles) if (!b.Popping) yield return b.Item; } }

        private string Answer => _target.ToString(System.Globalization.CultureInfo.InvariantCulture);
        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private float FieldBottom => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        /// <summary>The real window's base rate per difficulty.</summary>
        public static int BaseCount(int difficulty) => Math.Clamp(difficulty, 0, 2) switch { 0 => 3, 2 => 8, _ => 5 };

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, FieldBottom), bottom = Math.Clamp(visible.Bottom, screen.Top, FieldBottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(FieldBottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start() { if (Now == Phase.Rest) _next = Math.Min(_next, _t + 0.15); }

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            switch (Now)
            {
                case Phase.Rest:
                    if (_t >= _next) BeginRound();
                    break;
                case Phase.Bubbles:
                    if (_spawned < _target && _t - _phaseAt >= _spawned * SpawnEvery) SpawnBubble();
                    if (_popped >= _target && _bubbles.Count == 0 && _t - _phaseAt >= _target * SpawnEvery + 0.3)
                    { Now = Phase.Card; _phaseAt = _t; }
                    break;
                case Phase.Card:
                    if (_t - _phaseAt >= Answer.Length * DigitEvery + CardHold + CardFade)
                    { Now = Phase.Rest; _next = _t + Gap(S); Rounds++; }
                    break;
            }
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                var it = b.Item;
                if (!b.Popping && _t - b.Born >= BubbleLife) Pop(b);
                if (b.Popping)
                {
                    double p = (_t - b.PopAt) / PopLength();
                    if (p >= 1) { _layer.Remove(it); _bubbles.RemoveAt(i); continue; }
                    it.Opacity = (float)(1 - p);
                    if (!Gentle) it.Scale = (float)(1 + 0.35 * p);
                    continue;
                }
                double age = _t - b.Born;
                bool moving = _level() != MotionLevel.Off;
                it.CenterXPx = b.X + (moving ? Math.Sin(age * 2 + b.Phase) * b.Size * 0.12 : 0);
                it.CenterYPx = b.Y - (moving ? age * b.Size * 0.5 : 0);
                it.Opacity = (float)Math.Min(1, age / (Gentle ? GentleVisuals.RiseFloorMs / 1000.0 : 0.2));
            }
            _layer.MarkDirty();
            _layer.Update(TimeSpan.FromSeconds(dt));
            _fx.Update(TimeSpan.FromSeconds(dt));
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            var bounds = new SKRectI((int)Math.Floor(visible.Left), (int)Math.Floor(visible.Top),
                (int)Math.Ceiling(visible.Right), (int)Math.Ceiling(visible.Bottom));
            var elapsed = TimeSpan.FromSeconds(_t);
            _layer.Render(canvas, bounds, 1.0, elapsed);
            _fx.Render(canvas, bounds, 1.0, elapsed);
            if (Now == Phase.Card) DrawCard(canvas);
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            if (name == nameof(AppSettings.BubbleCountDifficulty) || name == nameof(AppSettings.BubbleCountFrequency))
            {
                // A new round with the new count, at once.
                foreach (var b in _bubbles) if (!b.Popping) b.PopAt = _t;
                Now = Phase.Rest;
                _next = _t + 0.35;
            }
        }

        public void Click(SKPoint at)
        {
            if (_disposed) return;
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                if (b.Popping) continue;
                double r = b.Size * 0.5, dx = at.X - b.Item.CenterXPx, dy = at.Y - b.Item.CenterYPx;
                if (dx * dx + dy * dy > r * r) continue;
                Pop(b);
                return;
            }
        }

        // ---------------------------------------------------------------- driver

        /// <summary>Rest between rounds: 1 an hour = 3 s, 10 an hour = 0.8 s.</summary>
        private static double Gap(AppSettings s) => 3.0 - 2.2 * (Math.Clamp(s.BubbleCountFrequency, 1, 10) - 1) / 9.0;

        private double PopLength()
        {
            if (_level() == MotionLevel.Off) return 0.0001;
            if (!Gentle) return PopSeconds;
            return GentleVisuals.Fade(PopSeconds, true, GentleVisuals.On ? GentleVisuals.FadeSeconds : GentleVisuals.DefaultFadeSeconds);
        }

        private void BeginRound()
        {
            _target = Math.Max(3, BaseCount(S.BubbleCountDifficulty) + _rng.Next(-1, 2));
            _spawned = 0;
            _popped = 0;
            Now = Phase.Bubbles;
            _phaseAt = _t;
        }

        private void SpawnBubble()
        {
            _spawned++;
            if (_screen.Width <= 0) return;
            double size = _screen.Width * 0.17 * (0.85 + _rng.NextDouble() * 0.3);
            var b = new Bubble
            {
                Born = _t, Size = size, Phase = _rng.NextDouble() * 6,
                X = _screen.Left + size / 2 + 8 + _rng.NextDouble() * Math.Max(1, _screen.Width - size - 16),
                Y = _band.Top + _band.Height * 0.3 + _rng.NextDouble() * Math.Max(1, _band.Height * 0.55),
            };
            b.Item = _layer.Add(new BubbleLayer.BubbleItem
            {
                DpiScale = 1f, SizeDip = (float)size, HitSizeDip = (float)size, Sprite = BubblesPreviewScene.GlassSprite(),
                CenterXPx = b.X, CenterYPx = b.Y, Opacity = 0f,
            });
            _bubbles.Add(b);
        }

        private void Pop(Bubble b)
        {
            if (b.Popping) return;
            b.PopAt = _t;
            _popped++;
            if (_level() == MotionLevel.Full && !_gentle() && MotionFx.ParticlesAllowedByLevel)
                _fx.EmitBurst(new System.Windows.Point(b.Item.CenterXPx, b.Item.CenterYPx), SKColors.HotPink, (float)Math.Clamp(b.Size / 90.0, 0.5, 1.4));
        }

        private void DrawCard(SKCanvas canvas)
        {
            double age = _t - _phaseAt, total = Answer.Length * DigitEvery + CardHold + CardFade;
            double rise = Gentle ? GentleVisuals.RiseFloorMs / 1000.0 : CardFade;
            double a = _level() == MotionLevel.Off ? 1 : Math.Clamp(Math.Min(age / rise, (total - age) / CardFade), 0, 1);
            if (a <= 0) return;
            // The card fits the band: on a short page (Bubble Count's is about 200 DIP tall) it shrinks to the height.
            float key = Math.Min(Math.Min(_screen.Width * 0.78f, 300) / 4.2f, Math.Max(12, (_band.Height - 16) / 6.4f)), gap = key * 0.15f;
            float w = key * 4.2f;
            float h = key * 0.2f + key * 1.05f + key * 0.8f + gap * 2 + 4 * key * 0.8f + 3 * gap + key * 0.35f;
            float x = _band.MidX - w / 2, y = Math.Max(_band.Top + 8, _band.MidY - h / 2);
            var card = new SKRect(x, y, x + w, y + h);
            byte A(double v) => (byte)Math.Clamp(v * a, 0, 255);

            _fill.Typeface = _bold;
            _fill.TextAlign = SKTextAlign.Center;
            _fill.Color = new SKColor(0, 0, 0, A(90));
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(card.Left, card.Top + 5, card.Right, card.Bottom + 5), 18, 18), _fill);
            _fill.Color = new SKColor(0x1c, 0x12, 0x2e, A(240));
            canvas.DrawRoundRect(new SKRoundRect(card, 18, 18), _fill);
            _line.StrokeWidth = 2;
            _line.Color = new SKColor(0xff, 0x69, 0xb4, A(200));
            canvas.DrawRoundRect(new SKRoundRect(card, 18, 18), _line);

            // The "?" and the answer box under it.
            float top = card.Top + key * 0.2f;
            _fill.TextSize = key * 0.95f;
            _fill.Color = new SKColor(0xff, 0x69, 0xb4, A(255));
            canvas.DrawText("?", card.MidX, top + key * 0.85f, _fill);
            if (S.BubbleCountStrictLock) DrawLock(canvas, card.Right - key * 0.55f, card.Top + key * 0.5f, key * 0.36f, a);
            top += key * 1.05f;
            var box = new SKRect(card.Left + gap * 2, top, card.Right - gap * 2, top + key * 0.8f);
            _fill.Color = new SKColor(0x0d, 0x08, 0x18, A(255));
            canvas.DrawRoundRect(new SKRoundRect(box, 10, 10), _fill);
            int lit = DigitsLit;
            if (lit > 0)
            {
                _fill.TextSize = key * 0.62f;
                _fill.Color = new SKColor(255, 255, 255, A(255));
                canvas.DrawText(Answer.Substring(0, lit), box.MidX, box.MidY + key * 0.22f, _fill);
            }
            top = box.Bottom + gap * 2;

            // The pad: 1-9, then a blank, 0, a tick. The digit being typed lights.
            // The key just typed lights for most of its beat; the tick lights once the answer is in.
            int lighting = lit > 0 && (lit < Answer.Length || age - lit * DigitEvery < DigitEvery * 0.8) ? Answer[lit - 1] - '0' : -1;
            bool done = lit >= Answer.Length && age >= Answer.Length * DigitEvery + 0.3;
            float kx0 = card.MidX - (3 * key + 2 * gap) / 2;
            string[] labels = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "", "0", "ok" };
            _fill.TextSize = key * 0.5f;
            for (int i = 0; i < 12; i++)
            {
                if (labels[i].Length == 0) continue;
                int r = i / 3, c = i % 3;
                var k = new SKRect(kx0 + c * (key + gap), top + r * (key * 0.8f + gap), kx0 + c * (key + gap) + key, top + r * (key * 0.8f + gap) + key * 0.8f);
                bool on = labels[i] == lighting.ToString(System.Globalization.CultureInfo.InvariantCulture)
                          || (i == 11 && done);
                _fill.Color = on ? new SKColor(0xff, 0x69, 0xb4, A(255)) : new SKColor(0x2e, 0x22, 0x46, A(255));
                canvas.DrawRoundRect(new SKRoundRect(k, 9, 9), _fill);
                _fill.Color = new SKColor(255, 255, 255, A(on ? 255 : 210));
                if (i == 11) DrawTick(canvas, k, a, on);
                else canvas.DrawText(labels[i], k.MidX, k.MidY + key * 0.17f, _fill);
            }
        }

        private void DrawTick(SKCanvas canvas, SKRect k, double a, bool on)
        {
            float s = Math.Min(k.Width, k.Height) * 0.26f;
            _line.StrokeWidth = Math.Max(2f, s * 0.35f);
            _line.StrokeCap = SKStrokeCap.Round;
            _line.Color = new SKColor(255, 255, 255, (byte)((on ? 255 : 210) * a));
            using var path = new SKPath();
            path.MoveTo(k.MidX - s, k.MidY);
            path.LineTo(k.MidX - s * 0.25f, k.MidY + s * 0.7f);
            path.LineTo(k.MidX + s, k.MidY - s * 0.7f);
            canvas.DrawPath(path, _line);
            _line.StrokeCap = SKStrokeCap.Butt;
        }

        private void DrawLock(SKCanvas canvas, float cx, float cy, float s, double a)
        {
            _line.StrokeWidth = Math.Max(1.5f, s * 0.22f);
            _line.Color = new SKColor(0xff, 0xd8, 0x6a, (byte)(230 * a));
            canvas.DrawArc(new SKRect(cx - s * 0.45f, cy - s * 0.95f, cx + s * 0.45f, cy - s * 0.05f), 180, 180, false, _line);
            _fill.Color = new SKColor(0xff, 0xd8, 0x6a, (byte)(230 * a));
            canvas.DrawRoundRect(new SKRoundRect(new SKRect(cx - s * 0.65f, cy - s * 0.45f, cx + s * 0.65f, cy + s * 0.55f), 3, 3), _fill);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bubbles.Clear();
            _layer.Clear();
            _fx.Clear();
            _fill.Dispose();
            _line.Dispose();
            if (!ReferenceEquals(_bold, SKTypeface.Default)) _bold.Dispose();
        }
    }
}
