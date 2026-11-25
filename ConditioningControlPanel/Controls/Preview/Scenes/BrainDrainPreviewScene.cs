using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Brain Drain in the live preview (lane P3, 2026-11-24): a drawn stand-in for the screen blur. The real
    /// BrainDrainLayer needs a capture of the real screen, so the scene paints its OWN small copy of the
    /// slice's desktop (PreviewDesktop.Paint at a quarter scale), blurs it once per strength, and lays that
    /// soft copy over the sharp desktop, stretched with a Low filter (SKIA STRETCH TRAP). Blur strength sets
    /// the blur and how much of it shows (0 = off, as on the real screen). Melting mode cuts the soft copy
    /// into a few bands that sway slowly side to side. With Undertow lit, a clear lens sits at the last
    /// click (the middle of the visible band at first): Lens size, Shrink speed and Click widen follow the
    /// real tuning (shares of the screen width). The audio half (Intensity, Volume) is not shown.
    /// </summary>
    public sealed class BrainDrainPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.BrainDrainBlurStrength), nameof(AppSettings.BrainDrainMeltEnabled), nameof(AppSettings.SuperModes),
            nameof(AppSettings.UndertowLensSize), nameof(AppSettings.UndertowShrinkSpeed), nameof(AppSettings.UndertowClickWiden),
        };

        /// <summary>Scale of the scene's own copy of the desktop.</summary>
        public const float CopyScale = 0.25f;
        public const int MeltBands = 9;
        /// <summary>Seconds the lens takes to drain from widest to smallest at Shrink speed 100.</summary>
        public const double DrainSeconds = 9;

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<bool> _lit;
        private readonly SKPaint _paint = new() { IsAntialias = false, FilterQuality = SKFilterQuality.Low };
        private readonly SKPaint _ring = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.5f };
        private readonly SKPath _hole = new();
        private SKImage? _soft;
        private (int W, int H, int Sigma) _softKey;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private SKPoint _lens, _lensTarget;
        private bool _lensSet, _disposed;
        private double _t, _shown, _lensShown, _lensR = -1;

        /// <summary>The page's scene (EffectPreview finds it by name and needs a constructor without arguments).</summary>
        private BrainDrainPreviewScene() : this(null, null, null, null) { }

        public BrainDrainPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<bool>? lit = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _lit = lit ?? UndertowLit;
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public double Clock => _t;
        public double Shown => _shown;
        /// <summary>The opacity the soft copy is laid over at right now.</summary>
        public double VeilOpacity => _shown * Strength(S.BrainDrainBlurStrength).Alpha;
        public bool LensOn => _lensShown > 0.01;
        public SKPoint LensAt => _lens;
        public double LensRadius => _lensR;
        /// <summary>The sideways shift of melt band <paramref name="i"/> right now (DIPs).</summary>
        public float MeltShift(int i) => S.BrainDrainMeltEnabled && _level() != MotionLevel.Off
            ? (float)(Math.Sin(_t * 0.9 * (_gentle() ? 0.5 : 1) + i * 0.8) * 7) : 0;

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;
        private UndertowTuning Tuning => UndertowOptions.Tuning(S.UndertowLensSize, S.UndertowShrinkSpeed, S.UndertowClickWiden);

        private static bool UndertowLit()
        {
            try { return SuperAccess.IsSelected(SuperEffect.Undertow); } catch { return false; }
        }

        /// <summary>Blur strength 0..100 to the blur (in pixels of the small copy) and the veil's opacity.</summary>
        public static (double Sigma, double Alpha) Strength(int percent)
        {
            double s = Math.Clamp(percent / 100.0, 0, 1);
            return s <= 0 ? (0, 0) : (0.5 + 2.6 * s, 0.4 + 0.6 * s);
        }

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, screen.Bottom), bottom = Math.Clamp(visible.Bottom, screen.Top, screen.Bottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(screen.Bottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
            if (!_lensSet)
            {
                _lensTarget = new SKPoint(_band.MidX, _band.Top + _band.Height * 0.4f);
                _lens = _lensTarget;
            }
        }

        public void Start() { }

        private double Rise => _level() == MotionLevel.Off ? 0 : Gentle ? Math.Max(0.9, GentleVisuals.RiseFloorMs / 1000.0) : 0.7;

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            double rise = Rise;
            _shown = rise <= 0 ? 1 : Math.Min(1, _shown + dt / rise);
            bool lit = _lit();
            _lensShown = rise <= 0 ? (lit ? 1 : 0) : Math.Clamp(_lensShown + (lit ? dt : -dt) / rise, 0, 1);

            var tune = Tuning;
            double w = Math.Max(1, _screen.Width);
            double still = tune.StillRadius * w, min = tune.MinRadius * w, max = tune.MaxRadius * w;
            if (_lensR < 0) _lensR = still;
            // It drains on its own toward the smallest lens; Shrink speed 0 holds it open.
            if (_level() != MotionLevel.Off)
                _lensR = Math.Max(min, _lensR - dt * tune.ShrinkScale * (max - min) / DrainSeconds);
            _lensR = Math.Min(_lensR, max);
            float k = rise <= 0 ? 1 : (float)(1 - Math.Exp(-5 * dt));
            _lens = new SKPoint(_lens.X + (_lensTarget.X - _lens.X) * k, _lens.Y + (_lensTarget.Y - _lens.Y) * k);
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            var (sigma, alpha) = Strength(S.BrainDrainBlurStrength);
            double a = _shown * alpha;
            if (a <= 0.002) return;
            var soft = Soft(sigma);
            if (soft == null) return;

            bool lens = _lensShown > 0.01 && _lensR > 0;
            canvas.Save();
            if (lens)
            {
                _hole.Rewind();
                _hole.AddCircle(_lens.X, _lens.Y, (float)(_lensR * (0.3 + 0.7 * _lensShown)));
                canvas.ClipPath(_hole, SKClipOperation.Difference, antialias: true);
            }
            _paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(a * 255, 0, 255));
            if (!S.BrainDrainMeltEnabled)
                canvas.DrawImage(soft, screen, _paint);
            else
            {
                // Melting: a few bands of the soft copy sway slowly side to side (a stand-in for the warp).
                float bandH = screen.Height / MeltBands, srcH = soft.Height / (float)MeltBands, over = 10;
                for (int i = 0; i < MeltBands; i++)
                {
                    float dx = MeltShift(i);
                    var src = new SKRect(0, i * srcH, soft.Width, (i + 1) * srcH);
                    var dst = new SKRect(screen.Left - over + dx, screen.Top + i * bandH, screen.Right + over + dx, screen.Top + (i + 1) * bandH + 0.5f);
                    canvas.DrawImage(soft, src, dst, _paint);
                }
            }
            canvas.Restore();
            if (lens)
            {
                _ring.Color = SKColors.White.WithAlpha((byte)(70 * _lensShown * _shown));
                canvas.DrawCircle(_lens.X, _lens.Y, (float)(_lensR * (0.3 + 0.7 * _lensShown)), _ring);
            }
        }

        /// <summary>The scene's own soft copy of the desktop, made once per size and strength.</summary>
        private SKImage? Soft(double sigma)
        {
            int w = Math.Max(4, (int)Math.Ceiling(_screen.Width * CopyScale)), h = Math.Max(4, (int)Math.Ceiling(_screen.Height * CopyScale));
            var key = (w, h, (int)Math.Round(sigma * 10));
            if (_soft != null && _softKey == key) return _soft;
            try
            {
                var info = new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var sharp = SKSurface.Create(info);
                using var blurred = SKSurface.Create(info);
                if (sharp == null || blurred == null) return _soft;
                sharp.Canvas.Clear(SKColors.Transparent);
                sharp.Canvas.Scale(w / Math.Max(1, _screen.Width), h / Math.Max(1, _screen.Height));
                PreviewDesktop.Paint(sharp.Canvas, _screen.Width, _screen.Height);
                using var plain = sharp.Snapshot();
                using var blur = SKImageFilter.CreateBlur((float)sigma, (float)sigma, SKShaderTileMode.Clamp);
                using var p = new SKPaint { ImageFilter = blur };
                blurred.Canvas.Clear(SKColors.Transparent);
                blurred.Canvas.DrawImage(plain, 0, 0, p);
                _soft?.Dispose();
                _soft = blurred.Snapshot();
                _softKey = key;
            }
            catch (Exception ex) { App.Logger?.Debug("BrainDrainPreviewScene: {E}", ex.Message); }
            return _soft;
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            if (name == nameof(AppSettings.UndertowLensSize)) _lensR = Tuning.StillRadius * Math.Max(1, _screen.Width);
        }

        /// <summary>With Undertow lit, the lens moves to the click and a click widens it (Click widen).</summary>
        public void Click(SKPoint at)
        {
            if (_disposed || !_lit()) return;
            _lensTarget = new SKPoint(Math.Clamp(at.X, _screen.Left, _screen.Right), Math.Clamp(at.Y, _screen.Top, _screen.Bottom));
            _lensSet = true;
            var tune = Tuning;
            double w = Math.Max(1, _screen.Width);
            if (_lensR < 0) _lensR = tune.StillRadius * w;
            _lensR = Math.Min(tune.MaxRadius * w, _lensR + tune.KickPerClick * w);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _soft?.Dispose();
            _paint.Dispose();
            _ring.Dispose();
            _hole.Dispose();
        }
    }
}
