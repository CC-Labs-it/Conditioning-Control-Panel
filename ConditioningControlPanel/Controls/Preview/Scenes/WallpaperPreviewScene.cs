using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Wallpaper in the live preview (overnight sweep, lane P1, 2026-11-24): the slice's OWN wallpaper
    /// changes, under its windows, icons and taskbar (the host lays those over this scene, see
    /// <see cref="IPreviewWallpaperScene"/>). On open the stock blue desktop crossfades into the new one.
    ///
    /// <para>Static: a collage of fake gradient cards (<see cref="PreviewPictures"/>) on a dark ground;
    /// with Change it on, a fresh collage crossfades in on a compressed interval (10 s = every 3 s,
    /// 5 min = every 8 s). Dynamic: the same collage, slowly moving. Own image: one fake picture (never the
    /// player's own file, its path is never read), sat on the slice by the Fit setting (Fill and Span
    /// cover, Fit letterboxes, Stretch distorts, Center sits it small on a plain ground). A click brings
    /// the next picture. Run with CCP and the master switch do nothing here.</para>
    /// </summary>
    public sealed class WallpaperPreviewScene : IPreviewWallpaperScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.CollageWallpaperEnabled), nameof(AppSettings.CollageMode), nameof(AppSettings.CollageWallpaperFit),
            nameof(AppSettings.CollageWallpaperIntervalSeconds), nameof(AppSettings.CollageWallpaperRotate),
            nameof(AppSettings.CollageOwnImagePath),
        };

        public const double CrossfadeSeconds = 1.1;
        /// <summary>The wall is painted at this share of the screen's DIPs (a soft picture under crisp icons).</summary>
        private const float WallScale = 0.5f;

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Random _rng;
        private readonly SKPaint _paint = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
        private static AppSettings? _defaults;

        private SKRect _screen;
        private SKImage? _cur, _old;
        private double _t, _fadeAt = double.NegativeInfinity, _nextSwap = double.PositiveInfinity;
        private int _seed;
        private bool _disposed, _started;

        /// <summary>The page's scene (found by name, built without arguments).</summary>
        public WallpaperPreviewScene() : this(settings: null) { }

        public WallpaperPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _rng = rng ?? new Random();
            _seed = _rng.Next();
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        /// <summary>How many wallpapers have been put up (the first one on open counts).</summary>
        public int Changes { get; private set; }
        /// <summary>0..1, how far the newest wallpaper has faded in.</summary>
        public double Mix => MixNow();
        public bool HasWall => _cur != null;
        public double Clock => _t;
        public WallpaperMode ShownMode { get; private set; }

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Off => _level() == MotionLevel.Off;
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        public void Layout(SKRect screen, SKRect visible)
        {
            bool resized = Math.Abs(screen.Width - _screen.Width) > 0.5f || Math.Abs(screen.Height - _screen.Height) > 0.5f;
            _screen = screen;
            if (resized && _cur != null) Swap(fade: false);
            else if (_started && _cur == null) Swap(fade: true);
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            Swap(fade: true);
        }

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            if (_old != null && MixNow() >= 1) { _old.Dispose(); _old = null; }
            if (_t >= _nextSwap) Swap(fade: true);
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            switch (name)
            {
                case nameof(AppSettings.CollageWallpaperIntervalSeconds):
                case nameof(AppSettings.CollageWallpaperRotate):
                    Plan();
                    if (!S.CollageWallpaperRotate) break;
                    _nextSwap = Math.Min(_nextSwap, _t + 0.4);
                    break;
                default:
                    Swap(fade: true);
                    break;
            }
        }

        public void Click(SKPoint at)
        {
            if (_disposed) return;
            Swap(fade: true);
        }

        public void Render(SKCanvas c, SKRect screen, SKRect visible)
        {
            if (_disposed || _cur == null) return;
            double mix = MixNow();
            var dest = Dest(screen);
            if (_old != null && mix < 1)
            {
                _paint.Color = SKColors.White;
                c.DrawImage(_old, dest, _paint);
            }
            _paint.Color = SKColors.White.WithAlpha((byte)(255 * Math.Clamp(mix, 0, 1)));
            c.DrawImage(_cur, dest, _paint);
        }

        // ---------------------------------------------------------------- driver

        private double Fade()
        {
            if (Off) return 0;
            return Gentle ? Math.Max(CrossfadeSeconds * 1.6, GentleVisuals.RiseFloorMs / 1000.0) : CrossfadeSeconds;
        }

        private double MixNow()
        {
            double f = Fade();
            return f <= 0 ? 1 : Math.Clamp((_t - _fadeAt) / f, 0, 1);
        }

        /// <summary>The next change of a rotating collage, on a compressed interval.</summary>
        private void Plan()
        {
            var s = S;
            if (!s.CollageWallpaperRotate || s.CollageMode == WallpaperMode.OwnImage) { _nextSwap = double.PositiveInfinity; return; }
            double k = (Math.Clamp(s.CollageWallpaperIntervalSeconds, 10, 300) - 10) / 290.0;
            _nextSwap = _t + Fade() + 3.0 + 5.0 * k;
        }

        /// <summary>Dynamic mode drifts the wall slowly (Motion Off and Reduced hold it still).</summary>
        private SKRect Dest(SKRect screen)
        {
            if (ShownMode != WallpaperMode.Dynamic || _level() != MotionLevel.Full) return screen;
            float grow = 0.06f, ox = (float)Math.Sin(_t * 0.35) * screen.Width * 0.03f, oy = (float)Math.Cos(_t * 0.27) * screen.Height * 0.012f;
            float w = screen.Width * (1 + grow), h = screen.Height * (1 + grow);
            return new SKRect(screen.MidX - w / 2 + ox, screen.MidY - h / 2 + oy, screen.MidX + w / 2 + ox, screen.MidY + h / 2 + oy);
        }

        private void Swap(bool fade)
        {
            if (_disposed || _screen.Width <= 0 || _screen.Height <= 0) return;
            var s = S;
            var next = Paint(s, _seed++);
            if (next == null) return;
            _old?.Dispose();
            _old = fade ? _cur : null;
            if (!fade) _cur?.Dispose();
            _cur = next;
            _fadeAt = fade ? _t : double.NegativeInfinity;
            ShownMode = s.CollageMode;
            Changes++;
            Plan();
        }

        private SKImage? Paint(AppSettings s, int seed)
        {
            int pw = Math.Max(8, (int)Math.Ceiling(_screen.Width * WallScale)), ph = Math.Max(8, (int)Math.Ceiling(_screen.Height * WallScale));
            using var surface = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return null;
            var c = surface.Canvas;
            c.Scale(WallScale);
            float w = _screen.Width, h = _screen.Height;
            var rng = new Random(seed);
            if (s.CollageMode == WallpaperMode.OwnImage) Own(c, s.CollageWallpaperFit, w, h, rng);
            else Collage(c, w, h, rng);
            c.Flush();
            return surface.Snapshot();
        }

        /// <summary>The collage: rows of fake cards, slightly turned, on a dark ground.</summary>
        private static void Collage(SKCanvas c, float w, float h, Random rng)
        {
            c.Clear(new SKColor(0x14, 0x0c, 0x24));
            using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
            using var shade = new SKPaint { IsAntialias = true, Color = new SKColor(0, 0, 0, 90) };
            float cell = Math.Max(90, w * 0.46f);
            int hue = rng.Next(PreviewPictures.Hues.Length);
            for (float y = -cell * 0.2f; y < h + cell * 0.2f; y += cell * 0.62f)
            {
                float shift = (float)rng.NextDouble() * cell * 0.5f;
                for (float x = -cell * 0.4f + shift; x < w + cell * 0.2f; x += cell * 0.7f)
                {
                    float aspect = PreviewPictures.Aspects[rng.Next(PreviewPictures.Aspects.Length)];
                    using var img = PreviewPictures.Make(hue++, aspect, 110 + rng.Next(60), 160);
                    float cw = cell * 0.78f * MathF.Sqrt(aspect), ch = cell * 0.78f / MathF.Sqrt(aspect);
                    c.Save();
                    c.Translate(x + cw / 2, y + ch / 2);
                    c.RotateDegrees((float)(rng.NextDouble() * 14 - 7));
                    var r = new SKRect(-cw / 2, -ch / 2, cw / 2, ch / 2);
                    c.DrawRoundRect(new SKRect(r.Left + 3, r.Top + 5, r.Right + 3, r.Bottom + 5), 6, 6, shade);
                    p.Color = SKColors.White;
                    c.DrawRoundRect(new SKRect(r.Left - 3, r.Top - 3, r.Right + 3, r.Bottom + 3), 7, 7, p);
                    c.Save();
                    c.ClipRoundRect(new SKRoundRect(r, 5, 5), antialias: true);
                    c.DrawImage(img, r, p);
                    c.Restore();
                    c.Restore();
                }
            }
        }

        /// <summary>One fake picture sat on the slice by the Fit setting.</summary>
        private static void Own(SKCanvas c, WallpaperFit fit, float w, float h, Random rng)
        {
            float aspect = 1.36f;   // a landscape picture on a tall slice shows every fit clearly
            using var img = PreviewPictures.Make(rng.Next(PreviewPictures.Hues.Length), aspect, 110 + rng.Next(60), 320);
            c.Clear(new SKColor(0x10, 0x10, 0x18));
            using var p = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
            SKRect r;
            switch (fit)
            {
                case WallpaperFit.Fit:
                    { float ih = w / aspect; r = new SKRect(0, h / 2 - ih / 2, w, h / 2 + ih / 2); break; }
                case WallpaperFit.Stretch:
                    r = new SKRect(0, 0, w, h); break;
                case WallpaperFit.Center:
                    { float iw = w * 0.7f, ih = iw / aspect; r = new SKRect(w / 2 - iw / 2, h / 2 - ih / 2, w / 2 + iw / 2, h / 2 + ih / 2); break; }
                default:   // Windows (the player's own fit, read as Fill), Fill, Span: cover
                    { float iw = h * aspect; r = new SKRect(w / 2 - iw / 2, 0, w / 2 + iw / 2, h); break; }
            }
            c.DrawImage(img, r, p);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _cur?.Dispose(); _cur = null;
            _old?.Dispose(); _old = null;
            _paint.Dispose();
        }
    }
}
