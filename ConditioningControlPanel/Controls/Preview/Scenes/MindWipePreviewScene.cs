using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Mind Wipe in the live preview (lane P3, 2026-11-24). Mind Wipe is heard, not seen, and the preview is
    /// always silent, so this is a drawn stand-in: a small speaker mark in the visible band sends out soft
    /// rings at a compressed pace of Per Hour (1 an hour = a gap of 6 s, 180 an hour = 1.2 s), ring size
    /// from Volume, and with Loop on a steady slow pulse instead. A click sends one wipe at once (the page's
    /// Test button does the same on the real screen). Rings fade in and out; nothing cuts or flickers.
    /// </summary>
    public sealed class MindWipePreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.MindWipeFrequency), nameof(AppSettings.MindWipeVolume), nameof(AppSettings.MindWipeLoop),
        };

        /// <summary>Seconds a ring takes to spread and fade.</summary>
        public const double RingSeconds = 1.8;
        /// <summary>Gap between rings of the Loop pulse.</summary>
        public const double LoopGap = 1.1;
        /// <summary>Rings in one wipe, and the gap between them.</summary>
        public const int RingsPerWipe = 3;
        public const double RingStagger = 0.28;
        private const int MaxRings = 16;

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly double[] _rings = new double[MaxRings];   // birth times, NaN = free
        private readonly SKPaint _ring = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPath _cone = new();
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _next = 0.4, _shown;
        private bool _disposed;

        /// <summary>The page's scene (EffectPreview finds it by name and needs a constructor without arguments).</summary>
        private MindWipePreviewScene() : this(null, null, null) { }

        public MindWipePreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null, Func<bool>? gentle = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            for (int i = 0; i < _rings.Length; i++) _rings[i] = double.NaN;
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public double Clock => _t;
        /// <summary>Rings on screen right now (born and not yet faded out).</summary>
        public int RingsOut
        {
            get
            {
                int n = 0;
                foreach (var b in _rings) if (!double.IsNaN(b) && _t >= b && _t - b < RingSeconds) n++;
                return n;
            }
        }
        /// <summary>Rings sent since the scene was made (tests).</summary>
        public int RingsSent { get; private set; }
        public SKPoint Speaker => new(_band.Left + _band.Width * 0.62f, _band.Top + _band.Height * 0.42f);
        /// <summary>How far a ring spreads at the current Volume, DIPs.</summary>
        public float MaxRadius => RingReach(S.MindWipeVolume, Math.Max(1, _screen.Width));

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        /// <summary>The compressed pace: 1 an hour = 6 s between wipes, 180 an hour = 1.2 s.</summary>
        public static double Gap(int perHour) => 6.0 - 4.8 * (Math.Clamp(perHour, 1, 180) - 1) / 179.0;

        /// <summary>Volume 0..100 to how far a ring spreads (a faint small ring even at 0).</summary>
        public static float RingReach(int volume, float screenWidth)
            => (float)(screenWidth * (0.08 + 0.34 * Math.Clamp(volume / 100.0, 0, 1)));

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float field = Math.Max(screen.Top + 1, screen.Bottom - PreviewDesktop.TaskbarHeight);
            float top = Math.Clamp(visible.Top, screen.Top, field), bottom = Math.Clamp(visible.Bottom, screen.Top, field);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(field, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start() => _next = Math.Min(_next, _t + 0.3);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            double rise = _level() == MotionLevel.Off ? 0 : Gentle ? Math.Max(0.9, GentleVisuals.RiseFloorMs / 1000.0) : 0.4;
            _shown = rise <= 0 ? 1 : Math.Min(1, _shown + dt / rise);
            if (_t >= _next)
            {
                if (S.MindWipeLoop) { Send(_t); _next = _t + LoopGap; }
                else { Wipe(); _next = _t + Gap(S.MindWipeFrequency); }
            }
            for (int i = 0; i < _rings.Length; i++)
                if (!double.IsNaN(_rings[i]) && _t - _rings[i] >= RingSeconds) _rings[i] = double.NaN;
        }

        private void Wipe()
        {
            for (int k = 0; k < RingsPerWipe; k++) Send(_t + k * RingStagger);
        }

        private void Send(double at)
        {
            int slot = -1;
            double oldest = double.PositiveInfinity;
            for (int i = 0; i < _rings.Length; i++)
            {
                if (double.IsNaN(_rings[i])) { slot = i; break; }
                if (_rings[i] < oldest) { oldest = _rings[i]; slot = i; }
            }
            _rings[slot] = at;
            RingsSent++;
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed || _band.Height <= 0) return;
            var c = Speaker;
            float reach = MaxRadius, start = 16;
            bool loop = S.MindWipeLoop;
            double vol = Math.Clamp(S.MindWipeVolume / 100.0, 0, 1);
            bool still = _level() == MotionLevel.Off;
            for (int i = 0; i < (still ? RingsPerWipe : _rings.Length); i++)
            {
                // Motion Off: a still of one wipe spreading, three rings at fixed steps.
                double born = still ? _t - (0.22 + 0.24 * i) * RingSeconds : _rings[i];
                if (double.IsNaN(born) || _t < born) continue;
                double p = (_t - born) / RingSeconds;
                if (p >= 1) continue;
                // In soft, out slow: never a hard edge in time.
                double a = Math.Min(1, p / 0.18) * (1 - p) * (1 - p) * _shown * (loop ? 0.55 : 0.85) * (0.45 + 0.55 * vol);
                if (a <= 0.004) continue;
                float r = start + (reach - start) * (float)(1 - (1 - p) * (1 - p));
                _ring.StrokeWidth = (float)(6 - 4 * p);
                _ring.Color = new SKColor(255, 140, 215, (byte)Math.Clamp(a * 255, 0, 255));
                canvas.DrawCircle(c, r, _ring);
            }
            DrawSpeaker(canvas, c);
        }

        /// <summary>A small speaker mark: a soft disc, a box and a cone, drawn in code.</summary>
        private void DrawSpeaker(SKCanvas canvas, SKPoint c)
        {
            byte a = (byte)Math.Clamp(255 * Math.Max(0.35, _shown), 0, 255);
            _fill.Color = new SKColor(18, 14, 40, (byte)(a * 0.72));
            canvas.DrawCircle(c, 19, _fill);
            _fill.Color = new SKColor(255, 255, 255, a);
            canvas.DrawRoundRect(new SKRect(c.X - 9, c.Y - 4.5f, c.X - 4, c.Y + 4.5f), 1.2f, 1.2f, _fill);
            _cone.Rewind();
            _cone.MoveTo(c.X - 5, c.Y - 4.5f);
            _cone.LineTo(c.X + 3, c.Y - 10);
            _cone.LineTo(c.X + 3, c.Y + 10);
            _cone.LineTo(c.X - 5, c.Y + 4.5f);
            _cone.Close();
            canvas.DrawPath(_cone, _fill);
            _fill.Color = new SKColor(255, 140, 215, a);
            canvas.DrawCircle(c.X + 8, c.Y, 2, _fill);
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            // A changed setting plays at once.
            _next = Math.Min(_next, _t + 0.15);
        }

        /// <summary>A click sends one wipe at once (Loop on: one pulse).</summary>
        public void Click(SKPoint at)
        {
            if (_disposed) return;
            if (S.MindWipeLoop) Send(_t);
            else Wipe();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ring.Dispose();
            _fill.Dispose();
            _cone.Dispose();
        }
    }
}
