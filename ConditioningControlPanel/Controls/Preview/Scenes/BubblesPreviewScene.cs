using System;
using System.Collections.Generic;
using System.Windows.Media.Imaging;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Prizes;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Bubbles in the live preview (sweep lane P2, owner 2026-11-24). The bubbles draw through the app's
    /// REAL <see cref="BubbleLayer"/> on a private, disposed engine (as FirstShowEffects does) with the
    /// shipped glass sprite, and pop through the real <see cref="ChaosFxLayer"/> burst. Only the driver
    /// is simplified: bubbles rise through the visible band at a compressed pace from Per Min, Speed and
    /// Size; Rain and Spiral In (Bubbles v2, ownership-gated like the real field) are shown too. A click
    /// pops one when Clickable is on (never a sound, never XP). With the Froth gold box lit, bubbles
    /// that touch merge into a bigger one with the gold rim, up to Froth's biggest size.
    /// Options it does not show (volume, triggers, gaze, magnet, worn skins) do nothing here.
    /// </summary>
    public sealed class BubblesPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.BubblesFrequency), nameof(AppSettings.BubbleSpeedBoost), nameof(AppSettings.BubblesSize),
            nameof(AppSettings.BubblesClickable), nameof(AppSettings.BubbleMotionStyle), nameof(AppSettings.SuperModes),
            nameof(AppSettings.FrothMaxSize),
        };

        /// <summary>A bubble at Size 100 is this share of the slice's width.</summary>
        public const double SizeShare = 0.20;
        /// <summary>Seconds a bubble takes to cross a 560 DIP band at Speed 0.</summary>
        public const double RiseSeconds = 5.0;
        public const int MaxBubbles = 12;
        private const double PopSeconds = 0.2;

        private sealed class Bubble
        {
            public BubbleLayer.BubbleItem Item = null!;
            public BubbleMotionStyle Style;
            public double X, Y, Vy, Born, Phase, Wobble, Size, PopAt = -1, MergeAt = -1;
            public SpiralInPath.State Spiral;
            public double SpiralStart, SpiralDir, SpiralRadial;
            public bool Popping => PopAt >= 0;
        }

        private readonly CompositorEngine _engine = new();
        private readonly BubbleLayer _layer;
        private readonly ChaosFxLayer _fx;
        private readonly List<Bubble> _bubbles = new();
        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<string, bool> _owns;
        private readonly Func<bool> _froth;
        private readonly Random _rng;
        private static AppSettings? _defaults;
        private static SKImage? _sprite;
        private static bool _spriteTried;

        private SKRect _screen, _band;
        private double _t, _next = 0.3;
        private bool _disposed;

        /// <summary>The host builds a scene by its id with no arguments (EffectPreview.Create).</summary>
        public BubblesPreviewScene() : this(settings: null) { }

        public BubblesPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<string, bool>? owns = null, Func<bool>? froth = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _owns = owns ?? PrizeGrants.IsGranted;
            _froth = froth ?? (() => SuperAccess.IsSelected(SuperEffect.InnerBloom));
            _rng = rng ?? new Random();
            // Draw lists stepped by the preview host, never by a second compositor host (FirstShowEffects).
            _engine.Dispose();
            _layer = new BubbleLayer(_engine);
            _fx = new ChaosFxLayer(_engine);
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public int OnScreen { get { int n = 0; foreach (var b in _bubbles) if (!b.Popping) n++; return n; } }
        public double Clock => _t;
        public SKRect SpawnBand => _band;
        internal IEnumerable<BubbleLayer.BubbleItem> Items { get { foreach (var b in _bubbles) if (!b.Popping) yield return b.Item; } }
        /// <summary>Bubbles that grew by merging (Froth).</summary>
        public int Merged { get { int n = 0; foreach (var b in _bubbles) if (b.Item.FrothScale > 1.01f) n++; return n; } }

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private float FieldBottom => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        /// <summary>The shipped glass sprite (never a mod's or a skin's): decoded once, never freed. Null = the layer's soft circle.</summary>
        internal static SKImage? GlassSprite()
        {
            if (_spriteTried) return _sprite;
            _spriteTried = true;
            try
            {
                _ = System.IO.Packaging.PackUriHelper.UriSchemePack;   // registers pack:// outside a running app (tests)
                var name = typeof(BubblesPreviewScene).Assembly.GetName().Name;
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri($"pack://application:,,,/{name};component/Resources/bubble.png");
                bmp.DecodePixelWidth = 256;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                bmp.Freeze();
                _sprite = SkiaWpfInterop.ToSKImage(bmp);
            }
            catch (Exception ex) { App.Logger?.Debug("Bubbles preview sprite: {E}", ex.Message); _sprite = null; }
            return _sprite;
        }

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, FieldBottom), bottom = Math.Clamp(visible.Bottom, screen.Top, FieldBottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(FieldBottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start() => _next = Math.Min(_next, _t + 0.15);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            var s = S;
            if (_t >= _next) { Spawn(s); _next = _t + Gap(s); }

            var level = _level();
            double speed = SpeedFactor(s) * (level == MotionLevel.Reduced ? 0.5 : 1);
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                var it = b.Item;
                if (b.Popping)
                {
                    double p = (_t - b.PopAt) / PopLength();
                    if (p >= 1) { Drop(i); continue; }
                    it.Opacity = (float)(1 - p);
                    if (!Gentle) it.Scale = (float)(1 + 0.35 * p);
                    continue;
                }
                double age = _t - b.Born;
                switch (b.Style)
                {
                    case BubbleMotionStyle.SpiralIn:
                        b.Spiral = SpiralInPath.Step(b.Spiral, b.SpiralStart, b.SpiralRadial * speed, SpiralInPath.Turns(level), b.SpiralDir, dt * 30);
                        b.X = _band.MidX + Math.Cos(b.Spiral.Angle) * b.Spiral.Radius;
                        b.Y = _band.MidY + Math.Sin(b.Spiral.Angle) * b.Spiral.Radius;
                        it.Opacity = (float)(Math.Min(1, age / Rise()) * b.Spiral.Fade);
                        if (b.Spiral.Done) { Drop(i); continue; }
                        break;
                    default:
                        b.Y += b.Vy * speed * dt;
                        double sway = level == MotionLevel.Off ? 0 : Math.Sin(age * 1.7 + b.Phase) * b.Wobble;
                        it.CenterXPx = b.X + sway;
                        it.Opacity = (float)Math.Min(1, age / Rise());
                        double r = b.Size * 0.5 * it.FrothScale;
                        if (b.Vy < 0 ? b.Y < _band.Top - r - 4 : b.Y > _band.Bottom + r + 4) { Drop(i); continue; }
                        break;
                }
                if (b.Style == BubbleMotionStyle.SpiralIn) it.CenterXPx = b.X;
                it.CenterYPx = b.Y;
                if (b.MergeAt >= 0)
                {
                    double m = (_t - b.MergeAt) / 0.5;
                    it.FrothSquash = m >= 1 || Gentle ? 1f : (float)(1 + 0.12 * Math.Sin(m * Math.PI * 3) * (1 - m));
                    if (m >= 1) b.MergeAt = -1;
                }
                it.FrothTime = age;
            }
            if (_froth()) Merge(s);
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
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            switch (name)
            {
                case nameof(AppSettings.BubblesSize):
                case nameof(AppSettings.BubbleMotionStyle):
                    // Size and motion are rolled at spawn, as in the real field: the old ones make way.
                    foreach (var b in _bubbles) if (!b.Popping) b.PopAt = _t;
                    break;
                case nameof(AppSettings.SuperModes):
                case nameof(AppSettings.FrothMaxSize):
                    if (!_froth()) foreach (var b in _bubbles) { b.Item.FrothScale = 1f; b.Item.FrothRim = 0f; }
                    break;
            }
            _next = Math.Min(_next, _t + 0.15);
        }

        public void Click(SKPoint at)
        {
            if (_disposed || !S.BubblesClickable) return;
            for (int i = _bubbles.Count - 1; i >= 0; i--)
            {
                var b = _bubbles[i];
                if (b.Popping || b.Item.Opacity < 0.05f) continue;
                double r = b.Size * 0.5 * b.Item.FrothScale, dx = at.X - b.Item.CenterXPx, dy = at.Y - b.Item.CenterYPx;
                if (dx * dx + dy * dy > r * r) continue;
                Pop(b);
                return;
            }
        }

        // ---------------------------------------------------------------- driver

        /// <summary>1 a minute = a gap of 3 s, 60 a minute = 0.45 s.</summary>
        private static double Gap(AppSettings s) => 3.0 - 2.55 * (Math.Clamp(s.BubblesFrequency, 1, 60) - 1) / 59.0;
        /// <summary>Speed boost 0..500 % on top of the natural rise.</summary>
        private static double SpeedFactor(AppSettings s) => 1 + Math.Clamp(s.BubbleSpeedBoost, 0, 500) / 100.0;
        private double Rise() => _level() == MotionLevel.Off ? 0.0001 : Gentle ? GentleVisuals.RiseFloorMs / 1000.0 : 0.25;
        private double PopLength()
        {
            if (_level() == MotionLevel.Off) return 0.0001;
            if (!Gentle) return PopSeconds;
            return GentleVisuals.Fade(PopSeconds, true, GentleVisuals.On ? GentleVisuals.FadeSeconds : GentleVisuals.DefaultFadeSeconds);
        }

        private int Live() { int n = 0; foreach (var b in _bubbles) if (!b.Popping) n++; return n; }

        private void Spawn(AppSettings s)
        {
            if (_disposed || _screen.Width <= 0 || Live() >= MaxBubbles) return;
            var level = _level();
            var style = AmbientBubbleMotion.Resolve(s.BubbleMotionStyle, _owns(PrizeGrants.BubbleRain), _owns(PrizeGrants.BubbleSpiralIn), level, _rng.NextDouble());
            double size = _screen.Width * SizeShare * Math.Clamp(s.BubblesSize, 50, 150) / 100.0 * (0.8 + _rng.NextDouble() * 0.4);
            size = Math.Min(size, Math.Max(30, _band.Height * 0.5));
            double r = size / 2;
            var b = new Bubble
            {
                Style = style, Born = _t, Size = size, Phase = _rng.NextDouble() * Math.PI * 2,
                Wobble = size * (0.10 + _rng.NextDouble() * 0.10),
                X = _screen.Left + r + 6 + _rng.NextDouble() * Math.Max(1, _screen.Width - size - 12),
            };
            double cross = (_band.Height + size) / RiseSeconds * (0.85 + _rng.NextDouble() * 0.3);
            switch (style)
            {
                case BubbleMotionStyle.Rain:
                    b.Y = _band.Top - r + 2; b.Vy = cross * 1.4;
                    break;
                case BubbleMotionStyle.SpiralIn:
                    b.SpiralStart = SpiralInPath.StartRadius(Math.Min(_screen.Width, _band.Height));
                    b.SpiralDir = _rng.Next(2) == 0 ? -1 : 1;
                    b.SpiralRadial = SpiralInPath.RadialPerSpeed * (1 + _rng.NextDouble());
                    b.Spiral = SpiralInPath.Start(b.SpiralStart, _rng.NextDouble() * Math.PI * 2);
                    b.X = _band.MidX + Math.Cos(b.Spiral.Angle) * b.SpiralStart;
                    b.Y = _band.MidY + Math.Sin(b.Spiral.Angle) * b.SpiralStart;
                    break;
                default:
                    b.Y = _band.Bottom + r - 2; b.Vy = -cross;
                    break;
            }
            b.Item = _layer.Add(new BubbleLayer.BubbleItem
            {
                DpiScale = 1f, SizeDip = (float)size, HitSizeDip = (float)size, Sprite = GlassSprite(),
                CenterXPx = b.X, CenterYPx = b.Y, Opacity = 0f,
            });
            _bubbles.Add(b);
        }

        private void Pop(Bubble b)
        {
            b.PopAt = _t;
            if (_level() == MotionLevel.Full && !_gentle() && MotionFx.ParticlesAllowedByLevel)
                _fx.EmitBurst(new System.Windows.Point(b.Item.CenterXPx, b.Item.CenterYPx), SKColors.HotPink,
                    (float)Math.Clamp(b.Size * b.Item.FrothScale / 90.0, 0.5, 1.6));
        }

        /// <summary>Froth: two bubbles that touch become one, by area, up to the biggest size the gold box allows.</summary>
        private void Merge(AppSettings s)
        {
            double max = FrothOptions.ClampMaxSize(s.FrothMaxSize);
            for (int i = 0; i < _bubbles.Count; i++)
            {
                var a = _bubbles[i];
                if (a.Popping || a.Item.Opacity < 0.6f) continue;
                for (int j = i + 1; j < _bubbles.Count; j++)
                {
                    var c = _bubbles[j];
                    if (c.Popping || c.Item.Opacity < 0.6f) continue;
                    double ra = a.Size * 0.5 * a.Item.FrothScale, rc = c.Size * 0.5 * c.Item.FrothScale;
                    double dx = a.Item.CenterXPx - c.Item.CenterXPx, dy = a.Item.CenterYPx - c.Item.CenterYPx;
                    if (dx * dx + dy * dy > Math.Pow((ra + rc) * 0.85, 2)) continue;
                    var (big, small) = ra >= rc ? (a, c) : (c, a);
                    double rb = Math.Max(ra, rc), rs = Math.Min(ra, rc);
                    double grown = Math.Sqrt(rb * rb + rs * rs) / (big.Size * 0.5);
                    big.Item.FrothScale = (float)Math.Min(max, grown);
                    big.Item.FrothRim = (float)Math.Clamp((big.Item.FrothScale - 1) / Math.Max(0.5, max - 1) * 1.6, 0, 1);
                    big.MergeAt = _t;
                    small.PopAt = _t;
                    small.Item.Opacity = 0f;
                    return;   // one merge a frame is plenty
                }
            }
        }

        private void Drop(int i)
        {
            _layer.Remove(_bubbles[i].Item);
            _bubbles.RemoveAt(i);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _bubbles.Clear();
            _layer.Clear();
            _fx.Clear();
        }
    }
}
