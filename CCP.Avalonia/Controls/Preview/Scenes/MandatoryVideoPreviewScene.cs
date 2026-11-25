using System;
using ConditioningControlPanel.Avalonia;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using ConditioningControlPanel.Services.Super;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Mandatory Videos in the live preview (overnight sweep, lane P1, 2026-11-24). A DRAWN stand-in: no
    /// clip, no player, no sound. A "video" is a dark surface with a drifting two-hue gradient, a few soft
    /// colour bars sliding across it and a play mark with a progress strip that fills while it plays.
    ///
    /// <para>Full screen (default): one video covers the visible band, fading in and out. Floating
    /// (<see cref="AppSettings.MandatoryVideoFloating"/>): small 16:9 windows drift and bounce in the band,
    /// sized by Size, at most Most-at-once, one per trigger (or all at once with Pop up together), moving
    /// at Speed. The mini-game (attention checks): round targets on the video that pop when clicked, count
    /// from Density, size from Size, life from Lifespan. Lights Down when its Super switch is on: the room
    /// around the floating windows (the edges of a full screen video) dims to Darkness at Dim speed and
    /// comes back at Return speed.</para>
    ///
    /// <para>The pace is compressed (1 an hour = a gap of 4 s, 20 an hour = 1 s) and nothing on screen
    /// states the real figure. Options it cannot show (sound, monitors, durations past a few seconds,
    /// strict, gaze) do nothing here.</para>
    /// </summary>
    public sealed class MandatoryVideoPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.VideosPerHour), nameof(AppSettings.VideoMinDurationSeconds), nameof(AppSettings.VideoMaxDurationSeconds),
            nameof(AppSettings.MandatoryVideoFloating), nameof(AppSettings.MandatoryVideoFloatingSizePercent),
            nameof(AppSettings.MandatoryVideoFloatingSpeedPercent), nameof(AppSettings.MandatoryVideoFloatingCount),
            nameof(AppSettings.MandatoryVideoFloatingTogether),
            nameof(AppSettings.AttentionChecksEnabled), nameof(AppSettings.AttentionDensity), nameof(AppSettings.AttentionLifespan),
            nameof(AppSettings.AttentionSize), nameof(AppSettings.RandomizeAttentionTargets),
            nameof(AppSettings.LightsDownDarkness), nameof(AppSettings.LightsDownDimSpeed), nameof(AppSettings.LightsDownReturnSpeed),
            nameof(AppSettings.SuperEffectsOn),
        };

        /// <summary>Delay before the next floating window joins a run (one per trigger).</summary>
        public const double JoinSeconds = 0.9;
        private const double FadeIn = 0.45, FadeOut = 0.45;

        public sealed class Video
        {
            public SKRect Rect;
            public float Vx, Vy;
            public double Born, Length, LeaveAt = -1;
            public int Hue;
            public bool Full;
            public double Alpha;
            public bool Leaving => LeaveAt >= 0;
        }

        public sealed class Target
        {
            /// <summary>Where it sits as a share of its video's box, so it rides along with a drifting window.</summary>
            public float Fx, Fy;
            public SKPoint At => new(Host.Rect.Left + Host.Rect.Width * Fx, Host.Rect.Top + Host.Rect.Height * Fy);
            public float R;
            public double Born, Until, PopAt = -1;
            public Video Host = null!;
            public bool Popped => PopAt >= 0;
        }

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<bool> _lightsDown;
        private readonly Random _rng;
        private readonly List<Video> _videos = new();
        private readonly List<Target> _targets = new();
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _next = 0.6, _join = -1, _veil;
        private int _joinsLeft, _hue;
        private bool _disposed;

        /// <summary>The page's scene (found by name, built without arguments).</summary>
        public MandatoryVideoPreviewScene() : this(settings: null) { }

        public MandatoryVideoPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<bool>? lightsDown = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.ResolveLevel(App.Settings?.Current?.MotionLevel ?? MotionLevel.Full, global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.AnimationsEnabled, GentleVisuals.On));
            _gentle = gentle ?? (() => GentleVisuals.On);
            _lightsDown = lightsDown ?? (() => SuperAccess.IsSelected(SuperEffect.LightsDown));
            _rng = rng ?? new Random();
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public IReadOnlyList<Video> Videos => _videos;
        public IReadOnlyList<Target> Targets => _targets;
        public int LiveTargets { get { int n = 0; foreach (var t in _targets) if (!t.Popped) n++; return n; } }
        public int Popped { get; private set; }
        /// <summary>How dark the Lights Down veil is right now, 0..1.</summary>
        public double Veil => _veil;
        public double Clock => _t;
        public SKRect Band => _band;

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private float FieldBottom => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);
        private bool Off => _level() == MotionLevel.Off;
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, FieldBottom), bottom = Math.Clamp(visible.Bottom, screen.Top, FieldBottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(FieldBottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
            foreach (var v in _videos) if (v.Full) v.Rect = FullRect();
        }

        public void Start() => _next = Math.Min(_next, _t + 0.15);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            var s = S;
            if (_videos.Count == 0 && _t >= _next) Trigger(s);
            if (_join >= 0 && _t >= _join)
            {
                if (_joinsLeft > 0 && s.MandatoryVideoFloating && Live() < Most(s)) { Open(s, floating: true); _joinsLeft--; }
                _join = _joinsLeft > 0 ? _t + JoinSeconds : -1;
            }

            double fin = Fade(FadeIn), fout = Fade(FadeOut);
            float speed = SpeedDip(s);
            for (int i = _videos.Count - 1; i >= 0; i--)
            {
                var v = _videos[i];
                if (!v.Leaving && _t >= v.Born + v.Length) v.LeaveAt = _t;
                if (v.Leaving)
                {
                    v.Alpha = fout <= 0 ? 0 : Math.Min(v.Alpha, 1 - (_t - v.LeaveAt) / fout);
                    if (v.Alpha <= 0)
                    {
                        _videos.RemoveAt(i);
                        _targets.RemoveAll(x => x.Host == v);
                        if (_videos.Count == 0) _next = _t + Gap(s);
                        continue;
                    }
                }
                else v.Alpha = fin <= 0 ? 1 : Math.Clamp((_t - v.Born) / fin, 0, 1);
                if (!v.Full && !Off) Drift(v, speed, (float)dt);
            }

            MiniGame(s);
            LightsDown(s, dt);
        }

        public void Render(SKCanvas c, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            bool full = _videos.Count > 0 && _videos[0].Full;
            if (_veil > 0.004 && !full) DrawVeil(c);
            foreach (var v in _videos) DrawVideo(c, v);
            if (_veil > 0.004 && full) DrawVeil(c);
            foreach (var t in _targets) DrawTarget(c, t);
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            var s = S;
            switch (name)
            {
                case nameof(AppSettings.MandatoryVideoFloating):
                case nameof(AppSettings.MandatoryVideoFloatingSizePercent):
                case nameof(AppSettings.MandatoryVideoFloatingTogether):
                    // The way it shows changed: what is up makes way and a new run comes at once.
                    foreach (var v in _videos) Leave(v);
                    _join = -1; _joinsLeft = 0;
                    break;
                case nameof(AppSettings.MandatoryVideoFloatingCount):
                    for (int i = 0, extra = Live() - Most(s); i < _videos.Count && extra > 0; i++)
                        if (!_videos[i].Leaving) { Leave(_videos[i]); extra--; }
                    if (s.MandatoryVideoFloating && Live() > 0 && Live() < Most(s)) { _joinsLeft = Most(s) - Live(); _join = _t + 0.2; }
                    break;
                case nameof(AppSettings.AttentionChecksEnabled):
                case nameof(AppSettings.AttentionDensity):
                case nameof(AppSettings.AttentionSize):
                case nameof(AppSettings.RandomizeAttentionTargets):
                    _targets.Clear();
                    foreach (var v in _videos) if (!v.Leaving) v.Born = Math.Min(v.Born, _t - Fade(FadeIn));
                    break;
            }
            // A changed setting plays at once.
            if (_videos.Count == 0 || AllLeaving()) _next = Math.Min(_next, _t + 0.15);
            if (_videos.Count == 0) return;
            foreach (var v in _videos) if (!v.Leaving) v.Length = Math.Max(v.Length, _t - v.Born + 3.5);
        }

        public void Click(SKPoint at)
        {
            if (_disposed) return;
            for (int i = _targets.Count - 1; i >= 0; i--)
            {
                var t = _targets[i];
                if (t.Popped || t.Host.Alpha < 0.5) continue;
                float dx = at.X - t.At.X, dy = at.Y - t.At.Y;
                if (dx * dx + dy * dy <= (t.R + 4) * (t.R + 4)) { t.PopAt = _t; Popped++; return; }
            }
            // A click on an empty desk asks for the next video now.
            if (_videos.Count == 0) _next = Math.Min(_next, _t + 0.15);
        }

        // ---------------------------------------------------------------- driver

        private static int Most(AppSettings s) => Math.Clamp(s.MandatoryVideoFloatingCount, 1, 4);
        private double Gap(AppSettings s) => 4.0 - 3.0 * (Math.Clamp(s.VideosPerHour, 1, 20) - 1) / 19.0;
        private int Live() { int n = 0; foreach (var v in _videos) if (!v.Leaving) n++; return n; }
        private bool AllLeaving() { foreach (var v in _videos) if (!v.Leaving) return false; return true; }
        private double Rnd(double a, double b) => a + _rng.NextDouble() * (b - a);

        /// <summary>How long a preview video plays: 4 s for the shortest real setting, 7 s for the longest.</summary>
        private double Length(AppSettings s)
        {
            double min = Math.Clamp(s.VideoMinDurationSeconds, 0, 3600), max = Math.Clamp(s.VideoMaxDurationSeconds, 0, 3600);
            double mid = max > 0 ? (Math.Min(min, max) + max) / 2 : Math.Max(min, 60);
            return 4.0 + 3.0 * Math.Clamp(mid / 600.0, 0, 1);
        }

        private double Fade(double secs)
        {
            if (Off) return 0;
            if (!Gentle) return secs;
            return Math.Max(secs * 1.6, GentleVisuals.RiseFloorMs / 1000.0);
        }

        private float SpeedDip(AppSettings s) => Off || _level() == MotionLevel.Reduced ? 0 : 12f + 70f * Math.Clamp(s.MandatoryVideoFloatingSpeedPercent, 10, 100) / 100f;

        private void Trigger(AppSettings s)
        {
            if (!s.MandatoryVideoFloating) { Open(s, floating: false); return; }
            int most = Most(s);
            if (s.MandatoryVideoFloatingTogether)
            {
                for (int i = 0; i < most; i++) Open(s, floating: true);
                _joinsLeft = 0; _join = -1;
            }
            else
            {
                Open(s, floating: true);
                _joinsLeft = most - 1;
                _join = _joinsLeft > 0 ? _t + JoinSeconds : -1;
            }
        }

        private SKRect FullRect() => new(_screen.Left, _band.Top, _screen.Right, _band.Bottom);

        private void Open(AppSettings s, bool floating)
        {
            if (_disposed || _screen.Width <= 0) return;
            if (floating && Live() >= Most(s)) return;
            var v = new Video { Born = _t, Length = Length(s), Hue = _hue++ % PreviewPictures.Hues.Length, Full = !floating };
            if (!floating) v.Rect = FullRect();
            else
            {
                // Size 15..60 % of a monitor maps onto 38..92 % of the narrow slice, so every step reads.
                float w = _screen.Width * (0.38f + 0.54f * (Math.Clamp(s.MandatoryVideoFloatingSizePercent, 15, 60) - 15) / 45f);
                float h = w * 9f / 16f;
                float x = (float)Rnd(_screen.Left + 4, Math.Max(_screen.Left + 4, _screen.Right - w - 4));
                float y = (float)Rnd(_band.Top + 8, Math.Max(_band.Top + 8, _band.Bottom - h - 8));
                v.Rect = new SKRect(x, y, x + w, y + h);
                double a = Rnd(0, Math.PI * 2);
                v.Vx = (float)Math.Cos(a); v.Vy = (float)Math.Sin(a);
            }
            _videos.Add(v);
        }

        private void Leave(Video v)
        {
            if (v.Leaving) return;
            v.LeaveAt = _t;
        }

        private void Drift(Video v, float speed, float dt)
        {
            if (speed <= 0) return;
            var r = v.Rect;
            float dx = v.Vx * speed * dt, dy = v.Vy * speed * dt;
            r.Offset(dx, dy);
            if (r.Left < _screen.Left + 2 && v.Vx < 0 || r.Right > _screen.Right - 2 && v.Vx > 0) v.Vx = -v.Vx;
            if (r.Top < _band.Top + 2 && v.Vy < 0 || r.Bottom > _band.Bottom - 2 && v.Vy > 0) v.Vy = -v.Vy;
            v.Rect = r;
        }

        private void MiniGame(AppSettings s)
        {
            // Popped rings finish, old targets fade out.
            _targets.RemoveAll(t => t.Popped ? _t - t.PopAt > 0.45 : _t > t.Until + Fade(0.3));
            if (!s.AttentionChecksEnabled) { _targets.Clear(); return; }
            int want = Math.Clamp((s.AttentionDensity + 1) / 2, 1, 5);
            double life = 1.4 + 0.12 * Math.Clamp(s.AttentionLifespan, 1, 30);
            foreach (var v in _videos)
            {
                if (v.Leaving || v.Alpha < 0.6) continue;
                int on = 0;
                foreach (var t in _targets) if (t.Host == v && !t.Popped && _t <= t.Until) on++;
                if (on >= want || _rng.NextDouble() > 0.08) continue;
                float baseR = 9f + 17f * (Math.Clamp(s.AttentionSize, 30, 150) - 30) / 120f;
                if (!v.Full) baseR *= 0.8f;
                float r = s.RandomizeAttentionTargets ? baseR * (float)Rnd(0.65, 1.3) : baseR;
                var box = v.Rect;
                if (box.Width < 4 * r || box.Height < 4 * r) continue;
                var at = new SKPoint((float)Rnd(box.Left + 2 * r, box.Right - 2 * r), (float)Rnd(box.Top + 2 * r, box.Bottom - 2 * r - 10));
                _targets.Add(new Target { Fx = (at.X - box.Left) / box.Width, Fy = (at.Y - box.Top) / box.Height, R = r, Born = _t, Until = _t + life, Host = v });
            }
        }

        private void LightsDown(AppSettings s, double dt)
        {
            bool lit = _lightsDown();
            double goal = 0;
            foreach (var v in _videos) if (!v.Leaving && v.Alpha > 0.5) goal = Math.Clamp(s.LightsDownDarkness, 0, 100) / 100.0 * 0.82;
            if (!lit) goal = 0;
            if (Off) { _veil = goal; return; }
            double rate = goal > _veil
                ? 0.25 * Math.Clamp(s.LightsDownDimSpeed, 10, 300) / 100.0
                : 0.6 * Math.Clamp(s.LightsDownReturnSpeed, 10, 300) / 100.0;
            if (Gentle) rate *= 0.6;
            double step = rate * dt;
            _veil = goal > _veil ? Math.Min(goal, _veil + step) : Math.Max(goal, _veil - step);
        }

        // ---------------------------------------------------------------- drawing

        private void DrawVeil(SKCanvas c)
        {
            byte a = (byte)(255 * Math.Clamp(_veil, 0, 1));
            if (_videos.Count > 0 && _videos[0].Full)
            {
                // Full screen: the edges dim, the middle of the video stays clear.
                var r = _videos[0].Rect;
                float rad = Math.Max(r.Width, r.Height) * 0.62f;
                var m = SKMatrix.CreateScale(r.Width / Math.Max(1, r.Height) * 1.25f, 1, r.MidX, r.MidY);
                _fill.Shader = SKShader.CreateRadialGradient(new SKPoint(r.MidX, r.MidY), rad,
                    new[] { new SKColor(2, 2, 8, 0), new SKColor(2, 2, 8, 0), new SKColor(2, 2, 8, a) }, new[] { 0f, 0.42f, 0.8f },
                    SKShaderTileMode.Clamp, m);
                c.DrawRect(r, _fill);
                _fill.Shader.Dispose();
                _fill.Shader = null;
                return;
            }
            using var path = new SKPath { FillType = SKPathFillType.EvenOdd };
            path.AddRect(new SKRect(_screen.Left, _band.Top - 40, _screen.Right, _band.Bottom + 40));
            foreach (var v in _videos) if (!v.Full) path.AddRoundRect(Inflate(v.Rect, 1), 6, 6);
            _fill.Shader = null;
            _fill.Color = new SKColor(2, 2, 8, (byte)(255 * Math.Clamp(_veil, 0, 1)));
            c.DrawPath(path, _fill);
        }

        private void DrawVideo(SKCanvas c, Video v)
        {
            if (v.Alpha <= 0.004) return;
            byte a = (byte)(255 * Math.Clamp(v.Alpha, 0, 1));
            var r = v.Rect;
            float radius = v.Full ? 0 : 6;
            var (h1, h2) = PreviewPictures.Hues[v.Hue];
            double age = _t - v.Born;

            c.Save();
            if (v.Full) c.ClipRect(r);
            else c.ClipRoundRect(new SKRoundRect(r, radius, radius), antialias: true);

            // Dark screen, then a drifting two-hue gradient: the "picture" of the clip.
            _fill.Shader = null;
            _fill.Color = new SKColor(8, 6, 16, a);
            c.DrawRect(r, _fill);
            float drift = (float)Math.Sin(age * 0.7) * r.Width * 0.25f;
            _fill.Shader = SKShader.CreateLinearGradient(
                new SKPoint(r.Left - r.Width * 0.2f + drift, r.Top), new SKPoint(r.Right + r.Width * 0.2f + drift, r.Bottom),
                new[] { PreviewDesktop.Css(h1).WithAlpha((byte)(a * 0.75)), new SKColor(20, 12, 40, (byte)(a * 0.6)), PreviewDesktop.Css(h2).WithAlpha((byte)(a * 0.75)) },
                new[] { 0f, 0.5f, 1f }, SKShaderTileMode.Clamp);
            c.DrawRect(r, _fill);
            _fill.Shader?.Dispose();
            _fill.Shader = null;

            // Soft colour bars sliding across, like light moving in the clip.
            for (int i = 0; i < 3; i++)
            {
                float bw = r.Width * (0.14f + 0.05f * i);
                float x = r.Left + (float)(((age * (18 + 9 * i) + i * r.Width * 0.37) % (r.Width + bw)) - bw);
                _fill.Color = SKColors.White.WithAlpha((byte)(a * (0.07 + 0.03 * i)));
                c.DrawRect(new SKRect(x, r.Top, x + bw, r.Bottom), _fill);
            }

            // Play mark and progress strip along the bottom.
            float strip = v.Full ? 24 : 14, pad = v.Full ? 14 : 7;
            var bar = new SKRect(r.Left + pad + strip, r.Bottom - pad - 4, r.Right - pad, r.Bottom - pad);
            _fill.Color = new SKColor(255, 255, 255, (byte)(a * 0.28));
            c.DrawRoundRect(bar, 2, 2, _fill);
            float done = (float)Math.Clamp(age / Math.Max(0.1, v.Length), 0, 1);
            _fill.Color = PreviewDesktop.Css(0xff69b4).WithAlpha(a);
            c.DrawRoundRect(new SKRect(bar.Left, bar.Top, bar.Left + bar.Width * done, bar.Bottom), 2, 2, _fill);
            using (var tri = new SKPath())
            {
                float tx = r.Left + pad, ty = bar.MidY, k = v.Full ? 7 : 4.5f;
                tri.MoveTo(tx, ty - k); tri.LineTo(tx + k * 1.6f, ty); tri.LineTo(tx, ty + k); tri.Close();
                _fill.Color = SKColors.White.WithAlpha((byte)(a * 0.9));
                c.DrawPath(tri, _fill);
            }
            c.Restore();

            if (!v.Full)
            {
                _stroke.StrokeWidth = 1.5f;
                _stroke.Color = new SKColor(255, 255, 255, (byte)(a * 0.35));
                c.DrawRoundRect(Inflate(r, 0.75f), radius, radius, _stroke);
            }
        }

        private void DrawTarget(SKCanvas c, Target t)
        {
            double hostA = Math.Clamp(t.Host.Alpha, 0, 1);
            if (t.Popped)
            {
                double k = Math.Clamp((_t - t.PopAt) / 0.45, 0, 1);
                _stroke.StrokeWidth = 3f * (float)(1 - k) + 0.5f;
                _stroke.Color = SKColors.White.WithAlpha((byte)(230 * (1 - k) * hostA));
                c.DrawCircle(t.At, t.R * (1 + 0.9f * (float)k), _stroke);
                return;
            }
            double rise = Fade(0.25), fall = Fade(0.3);
            double a = (rise <= 0 ? 1 : Math.Clamp((_t - t.Born) / rise, 0, 1)) * (_t <= t.Until ? 1 : fall <= 0 ? 0 : Math.Clamp(1 - (_t - t.Until) / fall, 0, 1)) * hostA;
            if (a <= 0.004) return;
            _fill.Shader = null;
            _fill.Color = PreviewDesktop.Css(0xff3fa4).WithAlpha((byte)(235 * a));
            c.DrawCircle(t.At, t.R, _fill);
            _fill.Color = SKColors.White.WithAlpha((byte)(245 * a));
            c.DrawCircle(t.At, t.R * 0.62f, _fill);
            _fill.Color = PreviewDesktop.Css(0xff3fa4).WithAlpha((byte)(235 * a));
            c.DrawCircle(t.At, t.R * 0.3f, _fill);
        }

        private static SKRect Inflate(SKRect r, float by) => new(r.Left - by, r.Top - by, r.Right + by, r.Bottom + by);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _videos.Clear();
            _targets.Clear();
            _fill.Dispose();
            _stroke.Dispose();
        }
    }
}
