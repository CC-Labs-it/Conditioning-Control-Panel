using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Flash Images in the live preview (pilot, owner 2026-11-24). The pictures draw through the app's
    /// REAL <see cref="FlashLayer"/> on a private, disposed engine (as FirstShowEffects does), so glow,
    /// corners, the pop exits, Drift and Bounce, the pendulum and Shatter look exactly as on the real
    /// screen. Only the driver is simplified: a port of the mockup's FlashScene (when and where things
    /// spawn). The pace is compressed (1 an hour = a gap of 5.6 s, 180 an hour = 1.2 s) and nothing on
    /// screen states the real figure. Options it does not show (Solid, Jackpot Remix, Gaze, Deck) do
    /// nothing here.
    /// </summary>
    public sealed class FlashPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.FlashFrequency), nameof(AppSettings.SimultaneousImages),
            nameof(AppSettings.SimultaneousImagesRandom), nameof(AppSettings.SimultaneousImagesMin),
            nameof(AppSettings.HydraLimit), nameof(AppSettings.FlashClickable), nameof(AppSettings.FlashStayUntilPopped),
            nameof(AppSettings.CorruptionMode), nameof(AppSettings.HydraLinkedTiming), nameof(AppSettings.FlashShatterEnabled),
            nameof(AppSettings.FlashExitStyle), nameof(AppSettings.ImageScale), nameof(AppSettings.FlashOpacity),
            nameof(AppSettings.FadeDuration), nameof(AppSettings.FlashGlowEnabled), nameof(AppSettings.FlashRoundedCorners),
            nameof(AppSettings.FlashAvoidCenter), nameof(AppSettings.FlashCenterExclusionPercent),
            nameof(AppSettings.FlashMotionStyle), nameof(AppSettings.FlashDriftSpeed),
        };

        /// <summary>How long a picture stays when "Stay until popped" is off.</summary>
        public const double LifeSeconds = 2.6;
        /// <summary>A picture at Size 100 is this share of the slice's width.</summary>
        public const double SizeShare = 0.30;
        /// <summary>The preview's glow (the real pink halo, sized for a picture a few hundred px wide).</summary>
        private const float GlowRadiusDip = 14f;
        private const double GlowOpacity = 0.85;
        /// <summary>The real spawn helpers think in monitor px: the slice is mapped onto a virtual monitor this wide.</summary>
        private const int VirtualMonitorWidth = 1920;
        private const double RingSeconds = 2.2, RingFade = 0.4;

        private sealed class Card
        {
            public FlashLayer.FlashItem Item = null!;
            public double Born, Expire = double.PositiveInfinity, LeaveAt = -1, LeaveFrom, LeaveSecs;
            public float Aspect;
            public bool Leaving => LeaveAt >= 0;
        }

        private readonly CompositorEngine _engine = new();
        private readonly FlashLayer _layer;
        private readonly List<Card> _cards = new();
        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<string, bool> _owns;
        private readonly Random _rng;
        private readonly SKPaint _ringPaint = new()
        {
            IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 2,
            PathEffect = SKPathEffect.CreateDash(new[] { 7f, 5f }, 0),
        };
        private readonly Random _probe = new(1);
        private static AppSettings? _defaults;

        private SKRect _screen, _visible, _band;
        private double _t, _next = 0.5, _ringUntil = -1, _ringStart = -1;
        private FlashExitStyle? _lastExit;
        private bool _disposed;

        public FlashPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<string, bool>? owns = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _owns = owns ?? PrizeGrants.IsGranted;
            _rng = rng ?? new Random();
            // A draw list stepped by the preview host, never by a second compositor host (FirstShowEffects).
            _engine.Dispose();
            _layer = new FlashLayer(_engine);
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        /// <summary>Pictures on screen (fading ones included; popped ones leaving through the layer are not).</summary>
        public int OnScreen => _cards.Count;
        public double Clock => _t;
        public SKRect SpawnBand => _band;
        internal IEnumerable<FlashLayer.FlashItem> Items { get { foreach (var c in _cards) yield return c.Item; } }

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool OwnsV2 => _owns(PrizeGrants.FlashDriftBounce) || _owns(PrizeGrants.FlashPendulum);
        private float FieldBottom => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            _visible = visible;
            // The part of the slice that is on screen (mockup VIS): a sliver under 160 DIP falls back to the top.
            float top = Math.Clamp(visible.Top, screen.Top, FieldBottom), bottom = Math.Clamp(visible.Bottom, screen.Top, FieldBottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(FieldBottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start() => _next = Math.Min(_next, _t + 0.15);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            if (_t >= _next) { Burst(); _next = _t + Gap(); }

            var s = S;
            double max = Math.Clamp(s.FlashOpacity / 100.0, 0, 1), fade = FadeSeconds(s), rise = RiseSeconds(fade);
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (!c.Leaving && _t >= c.Expire) { c.LeaveAt = _t; c.LeaveFrom = c.Item.Opacity; c.LeaveSecs = fade; }
                if (c.Leaving)
                {
                    double a = c.LeaveSecs <= 0 ? 0 : c.LeaveFrom * (1 - (_t - c.LeaveAt) / c.LeaveSecs);
                    if (a <= 0) { _layer.Remove(c.Item); _cards.RemoveAt(i); continue; }
                    c.Item.Opacity = a;
                }
                else c.Item.Opacity = (rise <= 0 ? 1 : Math.Clamp((_t - c.Born) / rise, 0, 1)) * max;
            }
            _layer.Update(TimeSpan.FromSeconds(dt));
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            DrawRing(canvas);
            _layer.Render(canvas, new SKRectI((int)Math.Floor(visible.Left), (int)Math.Floor(visible.Top),
                (int)Math.Ceiling(visible.Right), (int)Math.Ceiling(visible.Bottom)), 1.0, TimeSpan.FromSeconds(_t));
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            var s = S;
            switch (name)
            {
                case nameof(AppSettings.HydraLimit):
                    for (int i = 0, extra = Live() - s.HydraLimit; i < _cards.Count && extra > 0; i++)
                        if (!_cards[i].Leaving) { Leave(_cards[i], 0.25); extra--; }
                    break;
                case nameof(AppSettings.FlashStayUntilPopped):
                case nameof(AppSettings.FlashClickable):
                    foreach (var c in _cards) if (!c.Leaving) c.Expire = Stays(s) ? double.PositiveInfinity : _t + 1.4;
                    break;
                case nameof(AppSettings.FlashGlowEnabled):
                case nameof(AppSettings.FlashRoundedCorners):
                    foreach (var c in _cards) Restyle(c, s);
                    break;
                case nameof(AppSettings.ImageScale):
                case nameof(AppSettings.FlashMotionStyle):
                case nameof(AppSettings.FlashDriftSpeed):
                    // Sizes and motion are rolled at spawn, as in the real effect: the old ones make way.
                    foreach (var c in _cards) Leave(c, 0.2);
                    break;
                case nameof(AppSettings.FlashAvoidCenter):
                case nameof(AppSettings.FlashCenterExclusionPercent):
                    _ringStart = _t; _ringUntil = _t + RingSeconds;
                    if (s.FlashAvoidCenter)
                        foreach (var c in _cards)
                            if (!c.Leaving && InKeepOut(c.Item.X, c.Item.Y, c.Item.W, c.Item.H)) Leave(c, 0.25);
                    break;
            }
            // A changed setting plays at once: a burst within 150 ms when the screen has room.
            if (Live() < Math.Max(2, Math.Min(s.SimultaneousImages, s.HydraLimit))) _next = Math.Min(_next, _t + 0.15);
        }

        public void Click(SKPoint at)
        {
            if (_disposed) return;
            var s = S;
            if (!s.FlashClickable) return;
            Card? hit = null;
            for (int i = _cards.Count - 1; i >= 0; i--)
            {
                var c = _cards[i];
                if (c.Leaving || c.Item.Opacity <= 0.02) continue;
                if (at.X >= c.Item.X && at.X <= c.Item.X + c.Item.W && at.Y >= c.Item.Y && at.Y <= c.Item.Y + c.Item.H) { hit = c; break; }
            }
            if (hit == null) return;
            var parent = hit.Item;
            float px = parent.X, py = parent.Y, pw = parent.W, ph = parent.H;
            double expire = hit.Expire;
            _cards.Remove(hit);
            Pop(parent, s);

            if (!s.CorruptionMode) return;
            int room = Math.Min(2, s.HydraLimit - _cards.Count);
            for (int k = 0; k < room; k++)
            {
                int side = k == 0 ? -1 : 1;
                Spawn(px + side * pw * 0.75f + (float)Rnd(-8, 8), py + (float)Rnd(-ph * 0.5, ph * 0.5),
                    s.HydraLinkedTiming ? expire : Life(s));
            }
        }

        // ---------------------------------------------------------------- driver

        private double Gap() => 5.6 - 4.4 * (Math.Clamp(S.FlashFrequency, 1, 180) - 1) / 179.0;
        private static bool Stays(AppSettings s) => s.FlashStayUntilPopped && s.FlashClickable;
        private double Life(AppSettings s) => Stays(s) ? double.PositiveInfinity : _t + LifeSeconds;
        private int Live() { int n = 0; foreach (var c in _cards) if (!c.Leaving) n++; return n; }
        private double Rnd(double a, double b) => a + _rng.NextDouble() * (b - a);
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        private double FadeSeconds(AppSettings s)
        {
            // Motion Off never animates: a still has no fade in it, every change jumps to the settled frame.
            if (_level() == MotionLevel.Off) return 0;
            double fade = Math.Clamp(s.FadeDuration, 0, 200) * 0.01;
            if (!Gentle) return fade;
            var gentle = GentleVisuals.On ? GentleVisuals.FadeSeconds : GentleVisuals.DefaultFadeSeconds;
            return GentleVisuals.Fade(fade, true, gentle);
        }

        private double RiseSeconds(double fade) => _level() == MotionLevel.Off ? 0 : Gentle ? Math.Max(fade, GentleVisuals.RiseFloorMs / 1000.0) : fade;

        private void Burst()
        {
            var s = S;
            int n = AppSettings.RollFlashImageCount(s.SimultaneousImagesRandom, s.SimultaneousImagesMin, s.SimultaneousImages, _rng);
            for (int i = 0; i < n; i++)
                if (Spawn(null, null, Life(s)) == null) break;
        }

        private (float W, float H) ContentSize(AppSettings s, float aspect)
        {
            float b = (float)(_screen.Width * SizeShare * Math.Clamp(s.ImageScale, 50, 250) / 100.0);
            float w = b * MathF.Sqrt(aspect), h = b / MathF.Sqrt(aspect);
            float room = Math.Max(40, (_band.Height - 12) * 0.9f);
            if (h > room) { w *= room / h; h = room; }
            if (w > _screen.Width - 8) { h *= (_screen.Width - 8) / w; w = _screen.Width - 8; }
            return (w, h);
        }

        private Card? Spawn(float? atX, float? atY, double expire)
        {
            if (_disposed || _screen.Width <= 0) return null;
            var s = S;
            if (_cards.Count >= s.HydraLimit) return null;   // Max On Screen: new ones are skipped when full

            float aspect = PreviewPictures.Aspects[_rng.Next(PreviewPictures.Aspects.Length)];
            var (cw, ch) = ContentSize(s, aspect);
            bool glow = s.FlashGlowEnabled;
            float pad = glow ? GlowRadiusDip / 2 : 0;
            float w = cw + 2 * pad, h = ch + 2 * pad;
            float x, y;
            if (atX is { } ax && atY is { } ay) { x = ax; y = ay; }
            else (x, y) = Place(s, w, h);
            x = Math.Clamp(x, _screen.Left + 2, Math.Max(_screen.Left + 2, _screen.Right - w - 2));
            y = Math.Clamp(y, _screen.Top + 2, Math.Max(_screen.Top + 2, FieldBottom - h - 2));

            var level = _level();
            var style = level == MotionLevel.Full
                ? FlashMotion.Resolve(s.FlashMotionStyle, _owns(PrizeGrants.FlashDriftBounce), _owns(PrizeGrants.FlashPendulum), level, _rng)
                : FlashMotionStyle.Still;   // Reduced: drift off; Off: still
            FlashMotionState? motion = null;
            if (style != FlashMotionStyle.Still)
            {
                motion = FlashMotion.Create(style, x, y, w, h, _band.Left, _band.Top, _band.Width, _band.Height, level, _rng);
                if (motion.Style == FlashMotionStyle.DriftBounce) { var k = AppSettings.ClampDriftSpeed(s.FlashDriftSpeed); motion.Vx *= k; motion.Vy *= k; }
            }

            var face = PreviewPictures.Make(_rng.Next(PreviewPictures.Hues.Length), aspect, Rnd(110, 160));
            var radius = (float)FlashCorners.Resolve(s.FlashRoundedCorners, OwnsV2, glow, Math.Min(cw, ch), 0.5);
            var item = _layer.Spawn(new[] { face }, x, y, w, h, pad, radius,
                SKColors.HotPink, glow ? GlowRadiusDip / 3 : 0, GlowOpacity, false, motion);
            item.Opacity = 0;
            item.DeckBounds = _band;
            var card = new Card { Item = item, Born = _t, Expire = expire, Aspect = aspect };
            _cards.Add(card);
            return card;
        }

        /// <summary>A spot in the visible band; with Avoid Screen Center the real band-remap pick, measured on that band.</summary>
        private (float X, float Y) Place(AppSettings s, float w, float h)
        {
            float bandH = _band.Height;
            if (s.FlashAvoidCenter && _screen.Width > 0)
            {
                double k = VirtualMonitorWidth / _screen.Width;
                if (FlashPlacement.TryPickAvoidCenterPointAdaptive(VirtualMonitorWidth, (int)(bandH * k), (int)(w * k), (int)(h * k),
                        s.FlashCenterExclusionPercent, _rng, out int vx, out int vy, out _))
                    return ((float)(_screen.Left + vx / k), (float)(_band.Top + vy / k));
            }
            float x = (float)Rnd(_screen.Left + _screen.Width * 0.03, Math.Max(_screen.Left + _screen.Width * 0.03, _screen.Left + _screen.Width * 0.97 - w));
            float y = (float)Rnd(_band.Top + 6, Math.Max(_band.Top + 8, _band.Bottom - h - 6));
            return (x, y);
        }

        /// <summary>The keep-out square the real pick avoids, in screen DIPs (effective size after the adaptive shrink).</summary>
        private SKRect KeepOut(float w, float h)
        {
            var s = S;
            double k = VirtualMonitorWidth / Math.Max(1, _screen.Width);
            int pct = Math.Clamp(s.FlashCenterExclusionPercent, 5, 60);
            if (w > 0 && FlashPlacement.TryPickAvoidCenterPointAdaptive(VirtualMonitorWidth, (int)(_band.Height * k), (int)(w * k), (int)(h * k),
                    pct, _probe, out _, out _, out int eff)) pct = eff;
            float side = (float)(pct / 100.0 * Math.Min(_screen.Width, _band.Height));
            float cx = _screen.MidX, cy = _band.MidY;
            return new SKRect(cx - side / 2, cy - side / 2, cx + side / 2, cy + side / 2);
        }

        private bool InKeepOut(float x, float y, float w, float h)
        {
            var r = KeepOut(w, h);
            return x < r.Right && x + w > r.Left && y < r.Bottom && y + h > r.Top;
        }

        private void DrawRing(SKCanvas canvas)
        {
            if (_ringUntil < 0 || _t >= _ringUntil || !S.FlashAvoidCenter) return;
            double a = Math.Min(Math.Clamp((_t - _ringStart) / RingFade, 0, 1), Math.Clamp((_ringUntil - _t) / RingFade, 0, 1));
            var (cw, ch) = ContentSize(S, 1f);
            var r = KeepOut(cw, ch);
            _ringPaint.Color = SKColors.White.WithAlpha((byte)(204 * a));
            canvas.DrawRoundRect(r, r.Width * 0.18f, r.Width * 0.18f, _ringPaint);
        }

        private void Restyle(Card c, AppSettings s)
        {
            if (c.Leaving) return;
            var item = c.Item;
            bool glow = s.FlashGlowEnabled;
            float pad = glow ? GlowRadiusDip / 2 : 0, grow = pad - item.PaddingPx;
            item.X -= grow; item.Y -= grow; item.W += 2 * grow; item.H += 2 * grow;
            if (item.Motion is { } m) { m.X -= grow; m.Y -= grow; m.W += 2 * grow; m.H += 2 * grow; m.MediaW = m.W; m.MediaH = m.H; }
            item.PaddingPx = pad;
            item.HasGlow = glow;
            item.GlowSigmaPx = glow ? GlowRadiusDip / 3 : 0;
            item.GlowOpacity = GlowOpacity;
            item.CornerRadiusPx = (float)FlashCorners.Resolve(s.FlashRoundedCorners, OwnsV2, glow,
                Math.Min(item.W - 2 * pad, item.H - 2 * pad), 0.5);
            item.LastOpacity = double.NaN;   // repaint
        }

        private void Leave(Card c, double inSeconds)
        {
            if (c.Leaving) return;
            var fade = FadeSeconds(S);
            c.Expire = Math.Min(c.Expire, _t);
            c.LeaveAt = _t;
            c.LeaveFrom = c.Item.Opacity;
            c.LeaveSecs = Gentle ? fade : Math.Min(fade, inSeconds);   // a short way out, never a cut while gentle
        }

        /// <summary>A click: the real Shatter when it is on and owned, else the real pop exit of the Exit picker.</summary>
        private void Pop(FlashLayer.FlashItem item, AppSettings s)
        {
            var level = _level();
            if (s.FlashShatterEnabled && OwnsV2)
            {
                var shatter = FlashShatter.Create(item.X, item.Y, item.W, item.H, _screen.Left, _screen.Top, _screen.Width, _screen.Height, level, _rng);
                if (shatter.Shards.Length > 0) { _layer.BeginShatter(item, shatter); return; }
            }
            if (_gentle())
            {
                _layer.BeginExit(item, FlashExit.Gentle(Math.Max(0.25, FadeSeconds(s)), _rng.Next()));
                return;
            }
            if (FlashExit.Pick(s.FlashExitStyle, _lastExit, _rng) is { } style)
            {
                _lastExit = style;
                _layer.BeginExit(item, FlashExit.Begin(style, level, _rng.Next()));
            }
            else _layer.Remove(item);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cards.Clear();
            _layer.Clear();
            _ringPaint.Dispose();
        }
    }
}
