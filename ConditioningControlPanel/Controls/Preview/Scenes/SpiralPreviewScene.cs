using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.Services.Super.Draw;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Spiral Overlay in the live preview (lane P3, 2026-11-24). The spiral is a GENERATED Loom spiral
    /// (CCP.Core LoomSpiral, the weave the Whirlpool wears), never the player's own spiral file. It is
    /// worked out once into a small Bgra8888 disc and turned on the canvas, stretched with a Low filter
    /// (SKIA STRETCH TRAP), so a frame costs one stretched draw. The real SpiralLayer cannot be driven
    /// here: it takes decoded frames through an async hop to the dispatcher. Opacity follows the page's
    /// slider over the whole slice. With the Whirlpool (Super Vortex) lit, the real <see cref="Whirlpool"/>
    /// and <see cref="WhirlpoolDraw"/> (its drawn arms) sit at the last click point, sized by Pool size,
    /// or in the middle of the visible band with Stay centered. Options it cannot show do nothing.
    /// </summary>
    public sealed class SpiralPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.SpiralOpacity), nameof(AppSettings.SuperModes),
            nameof(AppSettings.WhirlpoolSize), nameof(AppSettings.WhirlpoolStayCentred),
        };

        /// <summary>The small disc the spiral is worked out in (px).</summary>
        public const int DiscPx = 256;
        /// <summary>Turn speed of the base spiral, degrees a second (gentle: half).</summary>
        public const float TurnDegPerSecond = 38f;
        /// <summary>How much rest the pool is given at once when it appears, so it shows inside the wake window.</summary>
        public const double PoolHeadStart = 7;

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<bool> _lit;
        private readonly SKBitmap _disc;
        private readonly SKPaint _paint = new() { IsAntialias = false, FilterQuality = SKFilterQuality.Low };
        private readonly Whirlpool _pool = new();
        private readonly WhirlpoolDraw _poolDraw = new();
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private SKPoint _target;
        private bool _targetSet, _wasLit, _disposed;
        private double _t, _angle, _shown, _poolShown;

        /// <summary>The page's scene (EffectPreview finds it by name and needs a constructor without arguments).</summary>
        private SpiralPreviewScene() : this(null, null, null, null) { }

        public SpiralPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<bool>? lit = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _lit = lit ?? VortexLit;
            _disc = MakeDisc();
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public double Clock => _t;
        public double Angle => _angle;
        /// <summary>0..1, how far the spiral has faded in.</summary>
        public double Shown => _shown;
        /// <summary>The paint opacity the spiral is drawn at right now.</summary>
        public double DrawOpacity => _shown * PreviewOpacity(S.SpiralOpacity);
        public bool PoolOn => _poolShown > 0.01;
        public SKPoint PoolAt => new((float)_pool.X, (float)_pool.Y);
        public double PoolRadius => _pool.Radius;
        public SKRect Band => _band;

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        private static bool VortexLit()
        {
            try { return SuperAccess.IsSelected(SuperEffect.Vortex); } catch { return false; }
        }

        /// <summary>
        /// The real paint curve (OverlayService.SpiralPaint: a tenth of the slider up to 50%) all but hides
        /// the spiral at the low end on a small slice, so the preview keeps a readable floor under it.
        /// </summary>
        public static double PreviewOpacity(int percent)
        {
            double f = Math.Clamp(percent / 100.0, 0, 1);
            double real = f <= 0.5 ? f * 0.1 : 0.05 + (f - 0.5) * 1.1;
            return Math.Max(real, f <= 0 ? 0 : 0.06 + 0.3 * f);
        }

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float field = Math.Max(screen.Top + 1, screen.Bottom - PreviewDesktop.TaskbarHeight);
            float top = Math.Clamp(visible.Top, screen.Top, field), bottom = Math.Clamp(visible.Bottom, screen.Top, field);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(field, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
            if (!_targetSet) _target = new SKPoint(_band.MidX, _band.Top + _band.Height * 0.45f);
        }

        public void Start()
        {
            if (_lit() && !_pool.Live) HeadStart();
        }

        private double Rise => _level() == MotionLevel.Off ? 0 : Gentle ? Math.Max(0.9, GentleVisuals.RiseFloorMs / 1000.0) : 0.6;

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            double rise = Rise;
            _shown = rise <= 0 ? 1 : Math.Min(1, _shown + dt / rise);

            bool lit = _lit();
            if (lit && !_wasLit && !_pool.Live) HeadStart();
            _wasLit = lit;
            _poolShown = rise <= 0 ? (lit ? 1 : 0) : Math.Clamp(_poolShown + (lit ? dt : -dt) / rise, 0, 1);

            if (_level() == MotionLevel.Off) return;   // a still: nothing turns
            _angle = (_angle + dt * TurnDegPerSecond * (_gentle() ? 0.5 : 1)) % 360;
            if (lit || _poolShown > 0) StepPool(dt);
        }

        private SKPoint Anchor()
            => S.WhirlpoolStayCentred ? new SKPoint(_band.MidX, _band.MidY) : _target;

        private void StepPool(double dt)
        {
            _pool.ReachScale = WhirlpoolOptions.ReachScale(S.WhirlpoolSize);
            var a = Anchor();
            _pool.Step(dt, a.X, a.Y, 0, 1, Math.Max(1, _screen.Width), _gentle());
        }

        private void HeadStart()
        {
            _pool.Reset();
            for (double s = 0; s < PoolHeadStart; s += 0.25) StepPool(0.25);
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            double a = DrawOpacity;
            if (a > 0.002)
            {
                // A disc wide enough to cover the whole band whatever its turn: half the band's diagonal.
                float r = (float)Math.Sqrt(_band.Width * _band.Width + _band.Height * _band.Height) / 2 + 2;
                canvas.Save();
                canvas.Translate(_band.MidX, _band.MidY);
                canvas.RotateDegrees((float)_angle);
                _paint.Color = SKColors.White.WithAlpha((byte)Math.Clamp(a * 255, 0, 255));
                canvas.DrawBitmap(_disc, new SKRect(-r, -r, r, r), _paint);
                canvas.Restore();
            }
            if (_poolShown > 0.01 && _pool.Size > 0.004)
            {
                double strength = Math.Clamp(S.SpiralOpacity / 100.0, 0, 1) * _poolShown;
                _poolDraw.Draw(canvas, _pool, strength);
            }
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            switch (name)
            {
                case nameof(AppSettings.SuperModes):
                    if (_lit() && !_pool.Live) HeadStart();
                    break;
                case nameof(AppSettings.WhirlpoolStayCentred):
                    if (S.WhirlpoolStayCentred) _target = new SKPoint(_band.MidX, _band.MidY);
                    break;
            }
        }

        /// <summary>With the Whirlpool lit (and not held in the middle), the pool trails to the click point.</summary>
        public void Click(SKPoint at)
        {
            if (_disposed || !_lit() || S.WhirlpoolStayCentred) return;
            _target = new SKPoint(Math.Clamp(at.X, _screen.Left, _screen.Right), Math.Clamp(at.Y, _band.Top, _band.Bottom));
            _targetSet = true;
        }

        /// <summary>The Loom weave, worked out once: two pink and violet arms with a cyan thread over them.</summary>
        private static SKBitmap MakeDisc()
        {
            var spec = new LoomSpec { Glow = 0.35 };
            spec.Layer.Arms = 2; spec.Layer.Turns = 2.5; spec.Layer.Duty = 0.5; spec.Layer.Style = 0;
            spec.Layer.Colors = new uint[] { 0xff69b4, 0x8a5cff };
            spec.Layer2On = true;
            spec.Layer2.Arms = 3; spec.Layer2.Turns = 1.25; spec.Layer2.Duty = 0.18; spec.Layer2.Style = 1;
            spec.Layer2.Colors = new uint[] { 0x00e5ff };
            spec.Layer2.Direction = -1;
            var loom = new LoomSpiral(DiscPx);
            loom.Add(spec, 0, 0, 1);
            var px = new byte[DiscPx * DiscPx * 4];
            loom.CopyTo(px);
            for (int q = 0; q < px.Length; q += 4) (px[q], px[q + 2]) = (px[q + 2], px[q]);   // RGBA -> BGRA
            var bmp = new SKBitmap(new SKImageInfo(DiscPx, DiscPx, SKColorType.Bgra8888, SKAlphaType.Premul));
            Marshal.Copy(px, 0, bmp.GetPixels(), px.Length);
            bmp.NotifyPixelsChanged();
            return bmp;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _disc.Dispose();
            _paint.Dispose();
            _poolDraw.Dispose();
        }
    }
}
