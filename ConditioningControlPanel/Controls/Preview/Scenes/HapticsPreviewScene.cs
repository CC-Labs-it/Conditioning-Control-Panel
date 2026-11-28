using System;
using System.Linq;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// The toy page in the live preview (lane P4, 2026-11-24). Vibration cannot be seen, so the preview makes it
    /// visible: a small drawn "pulse monitor" window sits on the desktop slice with a plain capsule (a generic
    /// shape, no brand) that buzzes (a small jitter, rings) and a scrolling PULSE TRACE under it, a line whose
    /// height is the strength over time. Everything is drawn and simulated here. The scene only READS the
    /// page's settings: it never talks to a toy or to anything that could reach one, never writes a setting,
    /// never plays sound.
    ///
    /// <para>Follows: the master switch (off = a grey capsule and a flat trace), Intensity (everything scales
    /// with it), Max power (a faint dashed ceiling the trace never crosses), the routing rows of the show
    /// (Flash show, Subliminals, Bubbles: every 1.3 s a fake flash card, word or bubble pops on the slice and
    /// the trace kicks by that row's strength; a row switched off sends nothing), the temperament (how hard
    /// and how sharp a kick is), flash brightness (while a flash card is up the floor rises), and audio sync:
    /// a small beat dot and three bars (bass, level, beat, each scaled by its weight) whose kick reaches the
    /// trace late by Delay and sized by Power, through Sensitivity, Smoothing and Max. A click on a flash card
    /// pops it with the Flash click row's kick; any other click is one test pulse. With toy input on, after a
    /// test pulse the steady floor stands aside (the trace rests flat) for a compressed "Stand aside for".
    /// Rabbit Hole, FunScript, band split, patterns and the connection rows do nothing here.</para>
    /// </summary>
    public sealed class HapticsPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched = new[]
        {
            nameof(AppSettings.Haptics),
            nameof(HapticSettings.Enabled), nameof(HapticSettings.GlobalIntensity), nameof(HapticSettings.V2),
            nameof(HapticSettings.ToyInputEnabled), nameof(HapticSettings.UserOverrideCooldownSec),
            nameof(HapticSettings.LuminanceSyncEnabled), nameof(HapticSettings.LuminanceSyncIntensity),
            nameof(HapticSettings.AudioSync),
            nameof(AudioSyncSettings.ManualLatencyOffsetMs), nameof(AudioSyncSettings.LiveIntensity),
            nameof(AudioSyncSettings.Sensitivity), nameof(AudioSyncSettings.Smoothing),
            nameof(AudioSyncSettings.BassWeight), nameof(AudioSyncSettings.RmsWeight),
            nameof(AudioSyncSettings.OnsetWeight), nameof(AudioSyncSettings.MaxIntensity),
        }.Distinct().ToArray();

        /// <summary>Seconds of strength the trace holds, and its samples (one a frame at 30 fps).</summary>
        public const double TraceSeconds = 5.0;
        public const int TraceSamples = 150;
        /// <summary>The compressed pace of the show: one fake effect every so often.</summary>
        public const double ShowGap = 1.3;
        /// <summary>The fake music's beat (long enough that a 600 ms Delay reads as late).</summary>
        public const double BeatSeconds = 0.8;
        /// <summary>The height a test pulse asks for, before Intensity and the ceiling.</summary>
        public const double TestPulse = 0.9;
        /// <summary>The ceiling a settings file without one gets (the shipped default).</summary>
        public const double DefaultCap = 0.70;

        private const int MaxPulses = 10, MaxRings = 8, MaxItems = 4;
        private static readonly string[] Words = { "DEEPER", "RELAX", "FOCUS" };

        private enum Kind { Flash, Word, Bubble }

        private struct Pulse { public double At, Amp, Attack, Hold, Decay; public bool Live; }
        private struct Ring { public double At; public bool Live; }
        private struct Item
        {
            public Kind Kind; public double At, Life, PopAt; public SKPoint C; public float W, H; public int Pic;
            public bool Live, Popped, Kicked;
        }

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Random _rng;
        private readonly Pulse[] _pulses = new Pulse[MaxPulses];
        private readonly Ring[] _rings = new Ring[MaxRings];
        private readonly Item[] _items = new Item[MaxItems];
        private readonly float[] _trace = new float[TraceSamples];
        private readonly SKImage?[] _pics = new SKImage?[3];
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
        private readonly SKPaint _dash = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1.2f };
        private readonly SKPathEffect _dashes = SKPathEffect.CreateDash(new[] { 5f, 4f }, 0);
        private readonly SKPath _path = new();
        private SKShader? _capsuleShader;
        private static readonly SKTypeface Bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _next = 0.35, _smooth, _standUntil = double.NegativeInfinity, _level01;
        private int _head, _word, _pic;
        private bool _disposed;

        /// <summary>The page's scene (found by name, built without arguments).</summary>
        public HapticsPreviewScene() : this(settings: null) { }

        public HapticsPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _rng = rng ?? new Random();
            _dash.PathEffect = _dashes;
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public double Clock => _t;
        /// <summary>The strength right now, 0..1 (after Intensity and the ceiling).</summary>
        public double Level => _level01;
        /// <summary>The ceiling the trace never crosses (Max power).</summary>
        public double Cap => CapOf(Toy);
        /// <summary>The highest sample the trace holds.</summary>
        public double TraceMax { get { float m = 0; foreach (var v in _trace) m = Math.Max(m, v); return m; } }
        /// <summary>Pulses sent since the scene was made (show kicks, beats are not pulses, test pulses).</summary>
        public int PulsesSent { get; private set; }
        public int TestPulses { get; private set; }
        public int ItemsShown { get; private set; }
        /// <summary>How far the capsule is shaken right now (DIPs).</summary>
        public float Jitter => JitterNow();
        public bool StandingAside => _t < _standUntil;
        public SKRect Monitor => MonitorRect();
        public SKPoint Capsule => CapsuleCentre();

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private HapticSettings Toy => S.Haptics ?? new HapticSettings();
        private bool Off => _level() == MotionLevel.Off;
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        private static double CapOf(HapticSettings h)
        {
            double c = h.V2?.MasterCap ?? DefaultCap;
            return Math.Clamp(double.IsFinite(c) ? c : DefaultCap, 0.05, 1.0);
        }

        /// <summary>
        /// A routing row's switch and strength, read without creating it: a row the settings do not hold yet
        /// reads as a fresh row would (on, half strength), and nothing is written.
        /// </summary>
        private static (bool On, double Amp) Row(HapticSettings h, string key)
        {
            try
            {
                var ev = h.V2?.Events;
                if (ev != null && ev.TryGetValue(key, out var r) && r != null) return (r.Enabled, Math.Clamp(r.Intensity, 0, 1));
            }
            catch (InvalidOperationException) { }
            return (true, 0.5);
        }

        private static (bool On, double Amp) Layer(HapticSettings h, string key)
        {
            try
            {
                var ly = h.V2?.Layers;
                if (ly != null && ly.TryGetValue(key, out var r) && r != null) return (r.Enabled, Math.Clamp(r.Intensity, 0, 1.5));
            }
            catch (InvalidOperationException) { }
            return (true, 1.0);
        }

        /// <summary>The temperament's kick scale, attack and decay stretch (the five shipped presets, numbers only).</summary>
        public static (double Kick, double Floor, double Attack, double Decay) Temper(string? key) => (key ?? "").Trim().ToLowerInvariant() switch
        {
            "gentle" => (0.75, 0.70, 1.40, 1.30),
            "tease" => (0.90, 0.85, 1.60, 1.80),
            "intense" => (1.20, 1.15, 0.80, 0.90),
            "cruel" => (1.40, 1.25, 0.45, 0.70),
            _ => (1.0, 1.0, 1.0, 1.0),
        };

        /// <summary>"Stand aside for" 5..120 s compressed to 0.8..3.5 s of flat floor.</summary>
        public static double StandAside(int seconds) => 0.8 + 2.7 * (Math.Clamp(seconds, 5, 120) - 5) / 115.0;

        // ---------------------------------------------------------------- layout

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float field = Math.Max(screen.Top + 1, screen.Bottom - PreviewDesktop.TaskbarHeight);
            float top = Math.Clamp(visible.Top, screen.Top, field), bottom = Math.Clamp(visible.Bottom, screen.Top, field);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(field, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
            _capsuleShader?.Dispose();
            _capsuleShader = null;
        }

        private SKRect MonitorRect()
        {
            float w = Math.Min(_band.Width - 26, 300), h = Math.Min(214, Math.Max(150, _band.Height * 0.5f));
            float left = _band.MidX - w / 2 + 14, top = _band.Bottom - h - 14;
            left = Math.Min(left, _band.Right - w - 8);
            return new SKRect(left, top, left + w, top + h);
        }

        private SKPoint CapsuleCentre()
        {
            var m = MonitorRect();
            return new SKPoint(m.MidX - (AudioOn(Toy) ? 26 : 0), m.Top + 22 + 36);
        }

        private SKRect TraceRect()
        {
            var m = MonitorRect();
            return new SKRect(m.Left + 12, m.Top + 22 + 78, m.Right - 12, m.Bottom - 12);
        }

        /// <summary>The part of the slice above the monitor where the fake show pops.</summary>
        private SKRect ShowRect()
        {
            var m = MonitorRect();
            float top = _band.Top + 12, bottom = m.Top - 10;
            if (bottom - top < 70) { top = _band.Top + 8; bottom = Math.Max(top + 70, m.Top); }
            return new SKRect(_band.Left + 64, top, _band.Right - 10, bottom);
        }

        private static bool AudioOn(HapticSettings h) => h.AudioSync?.Enabled == true && Layer(h, "AudioSync").On;

        // ---------------------------------------------------------------- simulation

        public void Start() => _next = Math.Min(_next, _t + 0.3);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            // Motion Off: the settle runs a whole trace's worth in one go, so the still shows a full line.
            int steps = Off ? (int)Math.Ceiling(dt / PreviewDriver.StepSeconds * (TraceSeconds / PreviewDriver.SettleSeconds)) : 1;
            double step = Off ? PreviewDriver.StepSeconds : dt;
            for (int i = 0; i < steps; i++) Step(step);
        }

        private void Step(double dt)
        {
            _t += dt;
            var s = S;
            var h = s.Haptics ?? new HapticSettings();
            var temper = Temper(h.V2?.Temperament);

            if (_t >= _next)
            {
                Spawn(h, temper);
                _next = _t + ShowGap;
            }

            // The show: items age, bubbles pop at their end and kick then.
            double lum = 0;
            for (int i = 0; i < _items.Length; i++)
            {
                ref var it = ref _items[i];
                if (!it.Live) continue;
                double age = _t - it.At;
                if (it.Kind == Kind.Bubble && !it.Kicked && _t >= it.PopAt)
                {
                    it.Kicked = true;
                    it.Popped = true;
                    var row = Row(h, "BubblePop");
                    if (row.On) Kick(row.Amp * temper.Kick, temper);
                }
                if (age >= it.Life) { it.Live = false; continue; }
                if (it.Kind == Kind.Flash) lum = Math.Max(lum, ItemAlpha(it));
            }

            // The steady floor: audio sync (late by Delay, sized by Power) and flash brightness.
            var a = h.AudioSync ?? new AudioSyncSettings();
            double floor = 0;
            if (AudioOn(h))
            {
                double heard = Analyse(a, _t - Math.Clamp(a.ManualLatencyOffsetMs, -600, 600) / 1000.0);
                double k = 1 - Math.Pow(Math.Clamp(a.Smoothing, 0, 0.95), dt * 30);
                _smooth += (heard - _smooth) * k;
                floor += _smooth * Math.Clamp(a.LiveIntensity, 0, 1) * Layer(h, "AudioSync").Amp;
            }
            else _smooth = 0;
            if (h.LuminanceSyncEnabled) floor += lum * Math.Clamp(h.LuminanceSyncIntensity, 0, 1) * 0.7;
            floor *= temper.Floor;
            if (h.ToyInputEnabled && _t < _standUntil) floor = 0;

            double trans = 0;
            for (int i = 0; i < _pulses.Length; i++)
            {
                ref var p = ref _pulses[i];
                if (!p.Live) continue;
                double e = Envelope(p, _t);
                if (e < 0) { p.Live = false; continue; }
                trans += e;
            }

            double raw = h.Enabled ? Math.Clamp(h.GlobalIntensity, 0, 1) * (floor + trans) : 0;
            _level01 = Math.Clamp(raw, 0, CapOf(h));
            _trace[_head] = (float)_level01;
            _head = (_head + 1) % TraceSamples;

            for (int i = 0; i < _rings.Length; i++)
                if (_rings[i].Live && _t - _rings[i].At >= RingSeconds) _rings[i].Live = false;
        }

        private const double RingSeconds = 0.75;

        /// <summary>The fake music at time <paramref name="t"/> through the page's analysis knobs.</summary>
        public static double Analyse(AudioSyncSettings a, double t)
        {
            var (bass, rms, onset) = Music(t);
            double v = Math.Clamp(a.Sensitivity, 0.1, 3) *
                       (Math.Clamp(a.BassWeight, 0, 1) * bass + Math.Clamp(a.RmsWeight, 0, 1) * rms + Math.Clamp(a.OnsetWeight, 0, 1) * onset);
            return Math.Clamp(v, 0, Math.Clamp(a.MaxIntensity, 0.5, 1));
        }

        /// <summary>Three bands of a made-up song: a bass thump each beat, a slow level, a sharp beat tick.</summary>
        public static (double Bass, double Rms, double Onset) Music(double t)
        {
            double ph = t / BeatSeconds;
            double since = (ph - Math.Floor(ph)) * BeatSeconds;
            double bar = Math.Floor(ph) % 4;
            double bass = Math.Exp(-since / 0.22) * (bar == 0 ? 1.0 : 0.75);
            double onset = Math.Exp(-since / 0.07);
            double rms = 0.45 + 0.2 * Math.Sin(t * 1.7) + 0.15 * Math.Exp(-since / 0.3);
            return (bass, Math.Clamp(rms, 0, 1), onset);
        }

        private static double Envelope(in Pulse p, double t)
        {
            double x = t - p.At;
            if (x < 0) return 0;
            if (x < p.Attack) return p.Amp * x / p.Attack;
            x -= p.Attack;
            if (x < p.Hold) return p.Amp;
            x -= p.Hold;
            if (x < p.Decay) { double f = 1 - x / p.Decay; return p.Amp * f * f; }
            return -1;
        }

        private void Kick(double amp, (double Kick, double Floor, double Attack, double Decay) temper)
        {
            if (amp <= 0) return;
            int slot = 0;
            double oldest = double.PositiveInfinity;
            for (int i = 0; i < _pulses.Length; i++)
            {
                if (!_pulses[i].Live) { slot = i; break; }
                if (_pulses[i].At < oldest) { oldest = _pulses[i].At; slot = i; }
            }
            _pulses[slot] = new Pulse
            {
                At = _t, Amp = amp, Attack = 0.05 * temper.Attack, Hold = 0.10, Decay = 0.42 * temper.Decay, Live = true,
            };
            PulsesSent++;
            AddRing();
        }

        private void AddRing()
        {
            int slot = 0;
            double oldest = double.PositiveInfinity;
            for (int i = 0; i < _rings.Length; i++)
            {
                if (!_rings[i].Live) { slot = i; break; }
                if (_rings[i].At < oldest) { oldest = _rings[i].At; slot = i; }
            }
            _rings[slot] = new Ring { At = _t, Live = true };
        }

        private void Spawn(HapticSettings h, (double Kick, double Floor, double Attack, double Decay) temper)
        {
            var flash = Row(h, "FlashDecay");
            var word = Row(h, "SubliminalTrigger");
            var bubble = Row(h, "BubblePop");
            Span<Kind> kinds = stackalloc Kind[3];
            int n = 0;
            if (flash.On) kinds[n++] = Kind.Flash;
            if (word.On) kinds[n++] = Kind.Word;
            if (bubble.On) kinds[n++] = Kind.Bubble;
            if (n == 0) return;   // every row of the show is off: nothing pops, nothing kicks
            int slot = -1;
            for (int i = 0; i < _items.Length; i++) if (!_items[i].Live) { slot = i; break; }
            if (slot < 0) return;

            var kind = kinds[_rng.Next(n)];
            var area = ShowRect();
            float w, hgt;
            switch (kind)
            {
                case Kind.Flash:
                    _pic = (_pic + 1) % _pics.Length;
                    float aspect = PreviewPictures.Aspects[_pic % PreviewPictures.Aspects.Length];
                    w = Math.Min(area.Width * 0.5f, Math.Max(70, _screen.Width * 0.3f));
                    hgt = w / aspect;
                    if (hgt > area.Height - 8) { hgt = Math.Max(40, area.Height - 8); w = hgt * aspect; }
                    break;
                case Kind.Bubble: w = hgt = 46; break;
                default: w = 120; hgt = 34; break;
            }
            float cx = area.Left + w / 2 + (float)_rng.NextDouble() * Math.Max(1, area.Width - w);
            float cy = area.Top + hgt / 2 + (float)_rng.NextDouble() * Math.Max(1, area.Height - hgt);
            double life = kind == Kind.Bubble ? 1.25 : 1.1;
            _items[slot] = new Item
            {
                Kind = kind, At = _t, Life = life, PopAt = _t + 0.95, C = new SKPoint(cx, cy), W = w, H = hgt,
                Pic = _pic, Live = true, Kicked = kind != Kind.Bubble,
            };
            if (kind == Kind.Word) _word = (_word + 1) % Words.Length;
            ItemsShown++;
            if (kind == Kind.Flash) Kick(flash.Amp * temper.Kick, temper);
            else if (kind == Kind.Word) Kick(word.Amp * temper.Kick, temper);
        }

        private double Fade(double secs)
        {
            if (Off) return 0.0001;
            return Gentle ? Math.Max(secs * 1.5, GentleVisuals.RiseFloorMs / 1000.0) : secs;
        }

        private double ItemAlpha(in Item it)
        {
            double age = _t - it.At, left = it.Life - age;
            if (it.Kind == Kind.Bubble && it.Popped) return Math.Clamp(1 - (_t - it.PopAt) / 0.3, 0, 1);
            double fin = Fade(0.16), fout = Fade(0.3);
            return Math.Clamp(Math.Min(age / fin, left / fout), 0, 1);
        }

        private float JitterNow()
        {
            if (Off || _level01 <= 0.01) return 0;
            float amp = (float)(_level01 * (Gentle ? 1.2 : 3.4));
            return amp * (float)Math.Sin(_t * 97.0);
        }

        // ---------------------------------------------------------------- input

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            // A changed setting plays at once: the next effect comes soon.
            _next = Math.Min(_next, _t + 0.15);
        }

        /// <summary>A click on a flash card pops it with the Flash click row; any other click is one test pulse.</summary>
        public void Click(SKPoint at)
        {
            if (_disposed) return;
            var h = Toy;
            var temper = Temper(h.V2?.Temperament);
            for (int i = 0; i < _items.Length; i++)
            {
                ref var it = ref _items[i];
                if (!it.Live || it.Kind != Kind.Flash) continue;
                var r = new SKRect(it.C.X - it.W / 2, it.C.Y - it.H / 2, it.C.X + it.W / 2, it.C.Y + it.H / 2);
                if (!r.Contains(at)) continue;
                it.Life = Math.Min(it.Life, _t - it.At + 0.25);
                var row = Row(h, "FlashClick");
                if (row.On) Kick(row.Amp * temper.Kick, temper);
                return;
            }
            Kick(TestPulse, temper);
            TestPulses++;
            if (h.ToyInputEnabled) _standUntil = _t + StandAside(h.UserOverrideCooldownSec);
        }

        // ---------------------------------------------------------------- drawing

        public void Render(SKCanvas c, SKRect screen, SKRect visible)
        {
            if (_disposed || _band.Height <= 0) return;
            var h = Toy;
            DrawShow(c);
            DrawMonitor(c, h);
        }

        private SKImage? Pic(int i)
        {
            i = ((i % _pics.Length) + _pics.Length) % _pics.Length;
            return _pics[i] ??= PreviewPictures.Make(i * 2 + 1, PreviewPictures.Aspects[i % PreviewPictures.Aspects.Length], 30 + 50 * i, 160);
        }

        private void DrawShow(SKCanvas c)
        {
            var cap = CapsuleCentre();
            for (int i = 0; i < _items.Length; i++)
            {
                var it = _items[i];
                if (!it.Live) continue;
                double alpha = ItemAlpha(it);
                if (alpha <= 0.004) continue;
                byte a = (byte)(255 * alpha);
                var r = new SKRect(it.C.X - it.W / 2, it.C.Y - it.H / 2, it.C.X + it.W / 2, it.C.Y + it.H / 2);
                switch (it.Kind)
                {
                    case Kind.Flash:
                    {
                        float grow = (float)(0.92 + 0.08 * Math.Min(1, (_t - it.At) / 0.2));
                        var g = new SKRect(it.C.X - it.W / 2 * grow, it.C.Y - it.H / 2 * grow, it.C.X + it.W / 2 * grow, it.C.Y + it.H / 2 * grow);
                        _fill.Color = new SKColor(0, 0, 0, (byte)(a * 0.35));
                        c.DrawRoundRect(new SKRect(g.Left + 2, g.Top + 4, g.Right + 2, g.Bottom + 5), 8, 8, _fill);
                        var img = Pic(it.Pic);
                        if (img != null)
                        {
                            c.Save();
                            c.ClipRoundRect(new SKRoundRect(g, 8, 8), antialias: true);
                            _fill.Color = SKColors.White.WithAlpha(a);
                            c.DrawImage(img, g, _fill);
                            c.Restore();
                        }
                        break;
                    }
                    case Kind.Word:
                        _text.Typeface = Bold;
                        _text.TextSize = 24;
                        _text.Color = new SKColor(255, 105, 180, (byte)(a * 0.45));
                        c.DrawText(Words[(_word + Words.Length - 1) % Words.Length], it.C.X + 1.5f, it.C.Y + 10, _text);
                        _text.Color = SKColors.White.WithAlpha(a);
                        c.DrawText(Words[(_word + Words.Length - 1) % Words.Length], it.C.X, it.C.Y + 8.5f, _text);
                        break;
                    case Kind.Bubble:
                    {
                        float rad = it.W / 2;
                        float rise = (float)(Math.Min(_t, it.PopAt) - it.At) * 14;
                        var at = new SKPoint(it.C.X, it.C.Y - rise);
                        if (it.Popped)
                        {
                            float p = (float)Math.Clamp((_t - it.PopAt) / 0.3, 0, 1);
                            _stroke.StrokeWidth = 2.5f * (1 - p) + 0.5f;
                            _stroke.Color = new SKColor(255, 190, 235, a);
                            c.DrawCircle(at, rad * (1 + 0.6f * p), _stroke);
                        }
                        else
                        {
                            _fill.Color = new SKColor(255, 160, 220, (byte)(a * 0.22));
                            c.DrawCircle(at, rad, _fill);
                            _stroke.StrokeWidth = 1.6f;
                            _stroke.Color = new SKColor(255, 220, 245, (byte)(a * 0.85));
                            c.DrawCircle(at, rad, _stroke);
                            _fill.Color = SKColors.White.WithAlpha((byte)(a * 0.7));
                            c.DrawOval(new SKRect(at.X - rad * 0.55f, at.Y - rad * 0.62f, at.X - rad * 0.15f, at.Y - rad * 0.38f), _fill);
                        }
                        r = new SKRect(at.X - rad, at.Y - rad, at.X + rad, at.Y + rad);
                        break;
                    }
                }
                // A short dotted thread from the effect to the toy while its kick is fresh: this is what drives it.
                double since = it.Kind == Kind.Bubble ? _t - it.PopAt : _t - it.At;
                if (since >= 0 && since < 0.45 && (it.Kind != Kind.Bubble || it.Popped))
                {
                    _dash.Color = new SKColor(255, 140, 215, (byte)(150 * (1 - since / 0.45)));
                    c.DrawLine(new SKPoint(r.MidX, r.Bottom), new SKPoint(cap.X, MonitorRect().Top), _dash);
                }
            }
        }

        private void DrawMonitor(SKCanvas c, HapticSettings h)
        {
            var m = MonitorRect();
            // The window: shadow, body, title strip with three dots.
            _fill.Color = new SKColor(0, 0, 0, 90);
            c.DrawRoundRect(new SKRect(m.Left + 2, m.Top + 5, m.Right + 2, m.Bottom + 7), 12, 12, _fill);
            _fill.Color = new SKColor(20, 17, 42, 242);
            c.DrawRoundRect(m, 12, 12, _fill);
            _stroke.StrokeWidth = 1.2f;
            _stroke.Color = new SKColor(70, 62, 120);
            c.DrawRoundRect(m, 12, 12, _stroke);
            _fill.Color = new SKColor(255, 255, 255, 14);
            c.Save();
            c.ClipRoundRect(new SKRoundRect(m, 12, 12), antialias: true);
            c.DrawRect(new SKRect(m.Left, m.Top, m.Right, m.Top + 22), _fill);
            c.Restore();
            for (int i = 0; i < 3; i++)
            {
                _fill.Color = i == 0 ? new SKColor(255, 105, 180, 200) : new SKColor(255, 255, 255, 60);
                c.DrawCircle(m.Left + 14 + i * 11, m.Top + 11, 3.2f, _fill);
            }

            DrawToy(c, h);
            if (AudioOn(h)) DrawAudio(c, h);
            DrawTrace(c, h);
        }

        private void DrawToy(SKCanvas c, HapticSettings h)
        {
            var at = CapsuleCentre();
            float jx = JitterNow(), jy = Off ? 0 : (float)(JitterNow() * 0.6 * Math.Cos(_t * 71.0));
            const float W = 74, H = 26;
            bool on = h.Enabled;

            // Rings: one per kick, spreading and fading.
            if (on && !Off)
            {
                for (int i = 0; i < _rings.Length; i++)
                {
                    if (!_rings[i].Live) continue;
                    double p = (_t - _rings[i].At) / RingSeconds;
                    if (p < 0 || p >= 1) continue;
                    float grow = (float)(4 + 30 * (1 - (1 - p) * (1 - p)));
                    _stroke.StrokeWidth = (float)(2.4 - 1.6 * p);
                    _stroke.Color = new SKColor(255, 140, 215, (byte)(200 * (1 - p) * (1 - p)));
                    var rr = new SKRect(at.X - W / 2 - grow, at.Y - H / 2 - grow * 0.6f, at.X + W / 2 + grow, at.Y + H / 2 + grow * 0.6f);
                    c.DrawRoundRect(rr, H / 2 + grow * 0.6f, H / 2 + grow * 0.6f, _stroke);
                }
            }

            // Buzz marks beside the capsule, as strong as the level.
            if (on && _level01 > 0.04)
            {
                byte ba = (byte)Math.Clamp(60 + 190 * _level01, 0, 255);
                _stroke.StrokeWidth = 2f;
                _stroke.StrokeCap = SKStrokeCap.Round;
                _stroke.Color = new SKColor(255, 190, 235, ba);
                for (int side = -1; side <= 1; side += 2)
                    for (int k = 0; k < 2; k++)
                    {
                        float off = W / 2 + 7 + k * 7 + (Off ? 0 : (float)(1.5 * Math.Sin(_t * 40 + k)));
                        var arc = new SKRect(at.X + side * off - 6, at.Y - 9 - k * 2, at.X + side * off + 6, at.Y + 9 + k * 2);
                        c.DrawArc(arc, side < 0 ? 140 : -40, 80, false, _stroke);
                    }
                _stroke.StrokeCap = SKStrokeCap.Butt;
            }

            // The capsule: a plain rounded pill with a soft band at one end.
            c.Save();
            c.Translate(at.X + jx, at.Y + jy);
            var body = new SKRect(-W / 2, -H / 2, W / 2, H / 2);
            _fill.Color = new SKColor(0, 0, 0, 80);
            c.DrawRoundRect(new SKRect(body.Left + 1, body.Top + 4, body.Right + 1, body.Bottom + 5), H / 2, H / 2, _fill);
            if (on)
            {
                _capsuleShader ??= SKShader.CreateLinearGradient(new SKPoint(0, -H / 2), new SKPoint(0, H / 2),
                    new[] { new SKColor(0xff, 0x8f, 0xc8), new SKColor(0xd2, 0x4f, 0xb0) }, null, SKShaderTileMode.Clamp);
                _fill.Shader = _capsuleShader;
                _fill.Color = SKColors.White;
                c.DrawRoundRect(body, H / 2, H / 2, _fill);
                _fill.Shader = null;
            }
            else
            {
                _fill.Color = new SKColor(96, 92, 112);
                c.DrawRoundRect(body, H / 2, H / 2, _fill);
            }
            _fill.Color = new SKColor(255, 255, 255, (byte)(on ? 70 : 40));
            c.DrawRoundRect(new SKRect(body.Left + 10, body.Top + 4, body.Right - 14, body.Top + 9), 3, 3, _fill);
            _fill.Color = new SKColor(255, 255, 255, (byte)(on ? 110 : 50));
            c.DrawRect(new SKRect(body.Right - 20, body.Top + 2, body.Right - 17, body.Bottom - 2), _fill);
            c.Restore();
        }

        private void DrawAudio(SKCanvas c, HapticSettings h)
        {
            var a = h.AudioSync ?? new AudioSyncSettings();
            var cap = CapsuleCentre();
            var (bass, rms, onset) = Music(_t);
            float x0 = cap.X + 74, baseY = cap.Y + 16;
            double[] v =
            {
                bass * Math.Clamp(a.BassWeight, 0, 1) * 2.2, rms * Math.Clamp(a.RmsWeight, 0, 1) * 2.2,
                onset * Math.Clamp(a.OnsetWeight, 0, 1) * 2.6,
            };
            for (int i = 0; i < 3; i++)
            {
                float bh = (float)Math.Clamp(v[i], 0.04, 1) * 30;
                _fill.Color = new SKColor(255, 255, 255, 24);
                c.DrawRoundRect(new SKRect(x0 + i * 10, baseY - 30, x0 + i * 10 + 6, baseY), 2, 2, _fill);
                _fill.Color = i == 2 ? new SKColor(255, 214, 120) : new SKColor(120, 200, 255);
                c.DrawRoundRect(new SKRect(x0 + i * 10, baseY - bh, x0 + i * 10 + 6, baseY), 2, 2, _fill);
            }
            // The beat dot: lights on the music's beat, NOW. The trace answers it later by Delay.
            float r = 4 + (float)(3 * onset);
            _fill.Color = new SKColor(255, 214, 120, (byte)(70 + 185 * onset));
            c.DrawCircle(x0 + 13, cap.Y - 22, r, _fill);
        }

        private void DrawTrace(SKCanvas c, HapticSettings h)
        {
            var tr = TraceRect();
            _fill.Color = new SKColor(8, 7, 20, 255);
            c.DrawRoundRect(tr, 7, 7, _fill);
            _stroke.StrokeWidth = 1;
            _stroke.Color = new SKColor(255, 255, 255, 14);
            for (int i = 1; i < 4; i++)
            {
                float y = tr.Bottom - 4 - (tr.Height - 8) * i / 4f;
                c.DrawLine(tr.Left + 4, y, tr.Right - 4, y, _stroke);
            }

            float inner = tr.Height - 8, bottom = tr.Bottom - 4;
            double cap = CapOf(h);
            float capY = bottom - inner * (float)cap;

            // The trace: newest on the right, a filled area under a bright line.
            c.Save();
            c.ClipRoundRect(new SKRoundRect(tr, 7, 7), antialias: true);
            float dx = (tr.Width - 8) / (TraceSamples - 1);
            _path.Rewind();
            _path.MoveTo(tr.Left + 4, bottom);
            for (int i = 0; i < TraceSamples; i++)
            {
                float v = _trace[(_head + i) % TraceSamples];
                _path.LineTo(tr.Left + 4 + i * dx, bottom - inner * Math.Min(v, (float)cap));
            }
            _path.LineTo(tr.Right - 4, bottom);
            _path.Close();
            _fill.Color = h.Enabled ? new SKColor(255, 105, 180, 60) : new SKColor(150, 150, 170, 30);
            c.DrawPath(_path, _fill);
            _path.Rewind();
            for (int i = 0; i < TraceSamples; i++)
            {
                float v = _trace[(_head + i) % TraceSamples];
                float x = tr.Left + 4 + i * dx, y = bottom - inner * Math.Min(v, (float)cap);
                if (i == 0) _path.MoveTo(x, y); else _path.LineTo(x, y);
            }
            _stroke.StrokeWidth = 2;
            _stroke.StrokeJoin = SKStrokeJoin.Round;
            _stroke.Color = h.Enabled ? new SKColor(255, 120, 200) : new SKColor(150, 150, 170);
            c.DrawPath(_path, _stroke);
            _stroke.StrokeJoin = SKStrokeJoin.Miter;

            // A soft bead at the newest sample.
            float headY = bottom - inner * Math.Min(_trace[(_head + TraceSamples - 1) % TraceSamples], (float)cap);
            _fill.Color = h.Enabled ? new SKColor(255, 200, 235) : new SKColor(170, 170, 190);
            c.DrawCircle(tr.Right - 4, headY, 3, _fill);
            c.Restore();

            // The ceiling (Max power): faint, dashed, with a small notch on the right edge.
            _dash.Color = new SKColor(255, 214, 120, 120);
            c.DrawLine(tr.Left + 4, capY, tr.Right - 4, capY, _dash);
            _fill.Color = new SKColor(255, 214, 120, 150);
            c.DrawRoundRect(new SKRect(tr.Right - 3, capY - 4, tr.Right + 1, capY + 4), 1.5f, 1.5f, _fill);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var p in _pics) p?.Dispose();
            _capsuleShader?.Dispose();
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
            _dash.Dispose();
            _dashes.Dispose();
            _path.Dispose();
        }
    }
}
