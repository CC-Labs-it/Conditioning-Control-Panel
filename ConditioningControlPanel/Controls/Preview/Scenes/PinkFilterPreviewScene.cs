using System;
using System.Runtime.InteropServices;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using ConditioningControlPanel.SuperCore;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Pink Filter in the live preview (lane P3, 2026-11-24). The slice is tinted in the page's Filter color
    /// at its opacity, drawn the way PinkTintLayer draws a plain tint (one rect, the opacity in the colour's
    /// alpha). The layer itself is not used: its Set writes the static look the real Steam wears, and the
    /// preview must never touch a real effect. With Steam (Super Creep) lit, a soft fog drifts in from the
    /// edges: worked out every frame in a small Bgra8888 picture (edge fade, drift and wiped holes all in
    /// the small pixels, no shader, no layer) and stretched over the visible band with a Low filter, per
    /// the SKIA STRETCH TRAP. Reach sets how far in from the edge it lies, the creep cap how thick it gets,
    /// Drift how fast it moves; a click wipes a hole (Wipe size) that fills back in (Fog return).
    /// </summary>
    public sealed class PinkFilterPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.PinkFilterOpacity), nameof(AppSettings.PinkFilterColor), nameof(AppSettings.SuperModes),
            nameof(AppSettings.SteamReach), nameof(AppSettings.SteamWipeSize), nameof(AppSettings.SteamFogReturn),
            nameof(AppSettings.SteamDrift), nameof(AppSettings.SuperCreepCap), nameof(AppSettings.SuperCreepSpeed),
        };

        /// <summary>Width of the small fog picture (px); its height follows the band.</summary>
        public const int FogW = 48;
        public const int FogMaxH = 200;
        /// <summary>Seconds the fog takes to creep in (the real one takes minutes).</summary>
        public const double CreepSeconds = 3.2;
        /// <summary>Radius of a wipe at Wipe size 100, DIPs.</summary>
        public const float WipeRadius = 46;
        /// <summary>Seconds a wipe takes to fill back in at Fog return 100.</summary>
        public const double ReturnSeconds = 2.6;
        private const int MaxHoles = 6;
        private const int Grid = 16;

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<bool> _lit;
        private readonly Func<(byte R, byte G, byte B)> _modColour;
        private readonly SKPaint _tint = new();
        private readonly SKPaint _fogPaint = new() { IsAntialias = false, FilterQuality = SKFilterQuality.Low };
        private readonly float[] _noise = new float[Grid * Grid];
        private readonly (float X, float Y, double Open)[] _holes = new (float, float, double)[MaxHoles];
        private SKBitmap? _fog;
        private byte[]? _fogPx;
        private int _fogH;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _shown, _creep, _driftX, _driftY;
        private float _r = 255, _g = 105, _b = 180;
        private bool _colourSet, _disposed;

        /// <summary>The page's scene (EffectPreview finds it by name and needs a constructor without arguments).</summary>
        private PinkFilterPreviewScene() : this(null, null, null, null, null) { }

        public PinkFilterPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<bool>? lit = null, Func<(byte, byte, byte)>? modColour = null, int seed = 5)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _lit = lit ?? CreepLit;
            _modColour = modColour ?? (() => App.Mods?.GetFilterColorRgb() ?? ((byte)255, (byte)105, (byte)180));
            var rng = new Random(seed);
            for (int i = 0; i < _noise.Length; i++) _noise[i] = (float)rng.NextDouble();
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public double Clock => _t;
        public double Shown => _shown;
        /// <summary>0..1, how far the fog has crept in.</summary>
        public double Creep => _creep;
        public SKColor TintNow => new((byte)_r, (byte)_g, (byte)_b, (byte)Math.Clamp(_shown * Opacity * 255, 0, 255));
        public int OpenHoles { get { int n = 0; foreach (var h in _holes) if (h.Open > 0.01) n++; return n; } }

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;
        private double Opacity => Math.Clamp(S.PinkFilterOpacity / 100.0, 0, 1);

        private static bool CreepLit()
        {
            try { return SuperAccess.IsSelected(SuperEffect.Creep); } catch { return false; }
        }

        /// <summary>The colour the tint renders: the player's pick, else the mod's, else hot pink (PinkFilterFeatureControl.EffectiveColor).</summary>
        public (byte R, byte G, byte B) TargetColour()
        {
            var hex = S.PinkFilterColor?.Trim().TrimStart('#');
            if (hex is { Length: 6 } && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int v))
                return ((byte)(v >> 16), (byte)(v >> 8), (byte)v);
            try { return _modColour(); } catch { return (255, 105, 180); }
        }

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, screen.Bottom), bottom = Math.Clamp(visible.Bottom, screen.Top, screen.Bottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(screen.Bottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start()
        {
            if (!_colourSet) { var c = TargetColour(); _r = c.R; _g = c.G; _b = c.B; _colourSet = true; }
        }

        private double Rise => _level() == MotionLevel.Off ? 0 : Gentle ? Math.Max(0.9, GentleVisuals.RiseFloorMs / 1000.0) : 0.6;

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            double rise = Rise;
            _shown = rise <= 0 ? 1 : Math.Min(1, _shown + dt / rise);

            // A new colour eases in, never cuts.
            var (tr, tg, tb) = TargetColour();
            if (!_colourSet) { _r = tr; _g = tg; _b = tb; _colourSet = true; }
            float k = rise <= 0 ? 1 : (float)Math.Min(1, dt / (rise * 0.6));
            _r += (tr - _r) * k; _g += (tg - _g) * k; _b += (tb - _b) * k;

            bool lit = _lit();
            double creepIn = _level() == MotionLevel.Off ? 0 : CreepSeconds * CreepPace(S.SuperCreepSpeed);
            _creep = creepIn <= 0 ? (lit ? 1 : 0) : Math.Clamp(_creep + (lit ? dt / creepIn : -dt / Math.Max(0.4, rise)), 0, 1);

            double back = ReturnSeconds * 100.0 / SteamOptions.ClampReturn(S.SteamFogReturn);
            for (int i = 0; i < _holes.Length; i++)
                if (_holes[i].Open > 0) _holes[i].Open = Math.Max(0, _holes[i].Open - dt / back);

            if (_level() == MotionLevel.Off) return;   // a still: the fog does not drift
            double drift = SteamOptions.ClampDrift(S.SteamDrift) / 100.0 * (_gentle() ? 0.5 : 1);
            _driftX += dt * 0.09 * drift;
            _driftY += dt * 0.05 * drift;
        }

        private static double CreepPace(CreepSpeed speed) => speed switch
        {
            CreepSpeed.Slow => 1.4,
            CreepSpeed.Fast => 0.6,
            _ => 1.0,
        };

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            double o = _shown * Opacity;
            if (o > 0.002)
            {
                _tint.Color = new SKColor((byte)_r, (byte)_g, (byte)_b, (byte)Math.Clamp(o * 255, 0, 255));
                canvas.DrawRect(screen, _tint);
            }
            if (_creep > 0.01 && _band.Height > 0) DrawFog(canvas);
        }

        private void DrawFog(SKCanvas canvas)
        {
            int h = Math.Clamp((int)Math.Round(FogW * _band.Height / Math.Max(1, _band.Width)), 8, FogMaxH);
            if (_fog == null || _fogH != h)
            {
                _fog?.Dispose();
                _fog = new SKBitmap(new SKImageInfo(FogW, h, SKColorType.Bgra8888, SKAlphaType.Premul));
                _fogPx = new byte[FogW * h * 4];
                _fogH = h;
            }
            var px = _fogPx!;
            var s = S;
            double reach = SteamOptions.ClampReach(s.SteamReach) / 100.0;
            double cap = Math.Clamp(s.SuperCreepCap, 0.2, 0.9) * _creep * _shown;
            // Fog is the filter colour lifted toward white.
            float fr = _r + (255 - _r) * 0.55f, fg = _g + (255 - _g) * 0.55f, fb = _b + (255 - _b) * 0.55f;
            float sx = _band.Width / FogW, sy = _band.Height / h;
            // Depth of the fog from the edge, as a share of the half short side: a thin line at 10%, the whole slice at 100%.
            double half = Math.Min(_band.Width, _band.Height) / 2.0, depth = half * (0.12 + 1.6 * reach * _creep);
            double wipe = WipeRadius * AppSettings.ClampSteamWipeSize(s.SteamWipeSize) / 100.0;
            for (int y = 0, q = 0; y < h; y++)
            {
                double dy = _band.Top + (y + 0.5) * sy;
                double ey = Math.Min(dy - _band.Top, _band.Bottom - dy);
                for (int x = 0; x < FogW; x++, q += 4)
                {
                    double dx = _band.Left + (x + 0.5) * sx;
                    double edge = Math.Min(Math.Min(dx - _band.Left, _band.Right - dx), ey);
                    double e = 1 - edge / depth;
                    if (e <= 0) { px[q] = px[q + 1] = px[q + 2] = px[q + 3] = 0; continue; }
                    e = e * e * (3 - 2 * e);
                    double n = Noise(dx / 120.0 + _driftX, dy / 120.0 + _driftY) * 0.65 + Noise(dx / 47.0 - _driftY * 1.7, dy / 47.0 + _driftX * 1.3) * 0.35;
                    double a = cap * e * (0.45 + 0.75 * n);
                    for (int i = 0; i < _holes.Length; i++)
                    {
                        var hole = _holes[i];
                        if (hole.Open <= 0.01) continue;
                        double d = Math.Sqrt((dx - hole.X) * (dx - hole.X) + (dy - hole.Y) * (dy - hole.Y)) / wipe;
                        if (d < 1) a *= 1 - hole.Open * (1 - d * d);
                    }
                    byte al = (byte)Math.Clamp(a * 255, 0, 255);
                    float m = al / 255f;
                    px[q] = (byte)(fb * m); px[q + 1] = (byte)(fg * m); px[q + 2] = (byte)(fr * m); px[q + 3] = al;
                }
            }
            Marshal.Copy(px, 0, _fog.GetPixels(), px.Length);
            _fog.NotifyPixelsChanged();
            canvas.DrawBitmap(_fog, _band, _fogPaint);
        }

        /// <summary>Smooth value noise on a small wrapped grid, 0..1.</summary>
        private float Noise(double x, double y)
        {
            double fx = Math.Floor(x), fy = Math.Floor(y);
            int x0 = ((int)fx % Grid + Grid) % Grid, y0 = ((int)fy % Grid + Grid) % Grid;
            int x1 = (x0 + 1) % Grid, y1 = (y0 + 1) % Grid;
            float tx = (float)(x - fx), ty = (float)(y - fy);
            tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
            float a = _noise[y0 * Grid + x0], b = _noise[y0 * Grid + x1], c = _noise[y1 * Grid + x0], d = _noise[y1 * Grid + x1];
            return (a + (b - a) * tx) * (1 - ty) + (c + (d - c) * tx) * ty;
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            if (name == nameof(AppSettings.SuperModes) && !_lit()) for (int i = 0; i < _holes.Length; i++) _holes[i].Open = 0;
        }

        /// <summary>With Steam lit, a click wipes a hole in the fog that fills back in.</summary>
        public void Click(SKPoint at)
        {
            if (_disposed || !_lit() || _creep <= 0.01) return;
            int slot = 0;
            for (int i = 1; i < _holes.Length; i++) if (_holes[i].Open < _holes[slot].Open) slot = i;
            _holes[slot] = (at.X, at.Y, 1);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fog?.Dispose();
            _tint.Dispose();
            _fogPaint.Dispose();
        }
    }
}
