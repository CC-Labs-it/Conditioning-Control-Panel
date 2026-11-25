using System;
using System.Collections.Generic;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// The Voice tab (She's Listening) in the live preview (lane P6, 2026-11-24). Listening cannot be seen and
    /// the preview is always silent and deaf: it opens no input device and reads no audio, so this is a drawn
    /// stand-in. A small dark "listening" pill sits on the slice with a mic mark and a wave of bars made in
    /// code. Every couple of seconds a NEUTRAL word is "heard" (fixed list, never the player's commands, the
    /// wake word, the safety words or anything from a mod): the wave swells, a word chip appears under the
    /// pill and lights when it is taken, and a small reaction plays (a fake flash card or a bubble that pops,
    /// with a soft ring). An unlit chip fades away.
    /// <para>What drives it (the tab's real settings): the master switch (consent and wake word or
    /// push-to-talk on; off = the pill sits dim with a flat wave and nothing is heard), the wake word (a bell
    /// chip must light before a command word is taken), push-to-talk (a key cap shows and the wave only moves
    /// while it is held; it stands in for the wake word, as on the real path), mic sensitivity (how big a
    /// swell is needed, drawn as two faint marks on the wave), command precision and wake strictness (how often
    /// a heard word is NOT taken), headphones (off = the wave goes flat for a moment after a taken word, the
    /// app's own voice is not listened to). A click hears one word now.</para>
    /// </summary>
    public sealed class VoicePreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.MicConsentGiven),
            nameof(AppSettings.SpeechWakeWordEnabled),
            nameof(AppSettings.SpeechPushToTalkEnabled),
            nameof(AppSettings.SpeechLoudnessThreshold),
            nameof(AppSettings.SpeechMatchThreshold),
            nameof(AppSettings.SpeechWakeMatchThreshold),
            nameof(AppSettings.SpeechWakeThreshold),
            nameof(AppSettings.SpeechHeadphonesMode),
        };

        /// <summary>The only words the preview ever "hears". Neutral on purpose.</summary>
        public static readonly string[] Words = { "RELAX", "DEEPER", "FOCUS", "PAUSE" };

        /// <summary>Rest between two heard words, after one has played out.</summary>
        public const double Rest = 0.7;
        private const double Swell = 0.85, SwellRise = 0.25;
        private const int Bars = 11;

        /// <summary>One heard thing: a word, maybe behind a wake chip or a held key.</summary>
        private sealed class Cycle
        {
            public double Start;
            public string Word = "";
            public bool Gate;           // the wake chip comes first
            public bool Key;            // push-to-talk: the key cap is held
            public float WakePeak, WordPeak;
            public bool WakeHeard, WakeTaken, WordHeard, WordTaken;
            public int Reaction;        // 0 = card, 1 = bubble
            public SKPoint ReactAt;
            public int Hue;
            // Times inside the cycle (seconds from Start).
            public double WakeSwell, WakeAppear, WakeDecide;
            public double WordSwell, WordAppear, WordDecide, KeyUp, End;
            public int Logged;          // 0 none, 1 wake appear, 2 wake decide, 3 word appear, 4 word decide
        }

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Random _rng;
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeCap = SKStrokeCap.Round };
        private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
        private readonly SKPaint _image = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
        private readonly SKPath _path = new();
        private readonly SKImage?[] _cards = new SKImage?[3];
        private static readonly SKTypeface Bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        private static readonly SKColor Coral = new(0xFF, 0x7E, 0x6B);
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private Cycle? _cycle;
        private double _t, _next = 0.5, _shown;
        private int _word;
        private bool _disposed;

        /// <summary>The page's scene (EffectPreview finds it by name and needs a constructor without arguments).</summary>
        public VoicePreviewScene() : this(settings: null) { }

        public VoicePreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _rng = rng ?? new Random(11);
            _text.Typeface = Bold;
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public double Clock => _t;
        /// <summary>What happened, in order: "wake+", "wake-", "word+:RELAX", "word-:FOCUS" (+ lit, - faded away).</summary>
        public List<string> Log { get; } = new();
        /// <summary>Word chips that appeared (a word loud enough to be heard), wake chips not counted.</summary>
        public int WordsHeard { get; private set; }
        public int WordsTaken { get; private set; }
        public int WakesHeard { get; private set; }
        public bool Busy => _cycle != null;
        public SKPoint PillCenter => new(_band.MidX, _band.Top + _band.Height * 0.30f);
        public SKPoint ChipCenter => new(_band.MidX, PillCenter.Y + 50);

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Still => _level() == MotionLevel.Off;
        private bool Gentle => _gentle() || _level() == MotionLevel.Reduced;

        /// <summary>The tab's master switch: consent given and the wake word or push-to-talk on.</summary>
        public static bool Armed(AppSettings s) => s.MicConsentGiven && (s.SpeechWakeWordEnabled || s.SpeechPushToTalkEnabled);

        /// <summary>The tab's sensitivity slider, 0..100, from the loudness gate (same ends as the tab).</summary>
        public static double Sensitivity(AppSettings s) => Math.Clamp((0.045 - s.SpeechLoudnessThreshold) / (0.045 - 0.004) * 100.0, 0, 100);

        /// <summary>How big a swell must be to be heard (share of the wave's height): soft at high sensitivity.</summary>
        public static float Need(AppSettings s) => (float)(0.85 - 0.7 * Sensitivity(s) / 100.0);

        /// <summary>How often a heard command word is NOT taken: stricter matching misses more.</summary>
        public static double CommandMiss(AppSettings s) => Math.Clamp((s.SpeechMatchThreshold - 0.35) * 0.9, 0.05, 0.7);

        /// <summary>How often a heard wake chip is NOT taken (fuzzy strictness or the spotter's threshold, the stricter).</summary>
        public static double WakeMiss(AppSettings s)
            => Math.Clamp(Math.Max((s.SpeechWakeMatchThreshold - 0.35) * 0.9, (s.SpeechWakeThreshold - 0.05) * 1.2), 0.05, 0.7);

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float field = Math.Max(screen.Top + 1, screen.Bottom - PreviewDesktop.TaskbarHeight);
            float top = Math.Clamp(visible.Top, screen.Top, field), bottom = Math.Clamp(visible.Bottom, screen.Top, field);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(field, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start() => _next = Math.Min(_next, _t + 0.4);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            double rise = Still ? 0 : Gentle ? Math.Max(0.6, GentleVisuals.RiseFloorMs / 1000.0) : 0.35;
            _shown = rise <= 0 ? 1 : Math.Min(1, _shown + dt / rise);

            var s = S;
            if (!Armed(s)) { _cycle = null; return; }
            if (_cycle == null && _t >= _next) Begin(s, forced: false);
            if (_cycle is { } c)
            {
                Advance(c);
                if (_t - c.Start >= c.End) { _cycle = null; _next = _t + Rest; }
            }
        }

        private void Begin(AppSettings s, bool forced)
        {
            bool key = s.SpeechPushToTalkEnabled;
            bool gate = !key && s.SpeechWakeWordEnabled;
            float need = Need(s);
            var c = new Cycle
            {
                Start = _t,
                Word = Words[_word++ % Words.Length],
                Gate = gate,
                Key = key,
                Reaction = _word % 2,
                Hue = _rng.Next(6),
                // A click is a clear word: always loud enough. Otherwise mostly loud enough at the default.
                WakePeak = forced ? 0.95f : (float)(0.45 + 0.55 * _rng.NextDouble()),
                WordPeak = forced ? 0.95f : (float)(0.45 + 0.55 * _rng.NextDouble()),
            };
            c.WakeHeard = gate && c.WakePeak >= need;
            c.WakeTaken = c.WakeHeard && _rng.NextDouble() >= WakeMiss(s);
            bool reach = !gate || c.WakeTaken;
            c.WordHeard = reach && c.WordPeak >= need;
            c.WordTaken = c.WordHeard && _rng.NextDouble() >= CommandMiss(s);
            float x = _band.Left + _band.Width * (float)(0.24 + 0.52 * _rng.NextDouble());
            float y = _band.Top + _band.Height * (float)(0.66 + 0.14 * _rng.NextDouble());
            c.ReactAt = new SKPoint(x, y);

            double at = key ? 0.25 : 0;
            if (gate)
            {
                c.WakeSwell = at; c.WakeAppear = at + 0.25; c.WakeDecide = at + 0.48;
                at += 0.9;
            }
            c.WordSwell = at; c.WordAppear = at + 0.28; c.WordDecide = at + 0.5;
            c.KeyUp = key ? at + Swell + 0.35 : 0;
            c.End = !reach ? c.WakeDecide + 0.8 : c.WordTaken ? c.WordDecide + 1.7 : c.WordDecide + 0.8;
            _cycle = c;
        }

        /// <summary>Counts and logs the moments the cycle has passed (once each).</summary>
        private void Advance(Cycle c)
        {
            double ct = _t - c.Start;
            if (c.Gate)
            {
                if (c.Logged < 1 && ct >= c.WakeAppear) { c.Logged = 1; if (c.WakeHeard) WakesHeard++; }
                if (c.Logged < 2 && ct >= c.WakeDecide) { c.Logged = 2; if (c.WakeHeard) Log.Add(c.WakeTaken ? "wake+" : "wake-"); }
            }
            else if (c.Logged < 2) c.Logged = 2;
            if (c.Gate && !c.WakeTaken) return;
            if (c.Logged < 3 && ct >= c.WordAppear) { c.Logged = 3; if (c.WordHeard) WordsHeard++; }
            if (c.Logged < 4 && ct >= c.WordDecide)
            {
                c.Logged = 4;
                if (c.WordHeard) { Log.Add((c.WordTaken ? "word+:" : "word-:") + c.Word); if (c.WordTaken) WordsTaken++; }
            }
        }

        // ------------------------------------------------------------------ drawing

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed || _band.Height <= 0) return;
            var s = S;
            bool armed = Armed(s);
            Cycle? c = _cycle;
            double ct = c == null ? 0 : _t - c.Start, waveCt = -1;
            double t = _t;
            // Motion Off: one composed still, a taken word with its reaction.
            if (Still && armed) { c = StillCycle(s); ct = c.WordDecide + 0.55; waveCt = c.WordSwell + 0.33; t = 1.3; }

            float a = (float)(!armed ? 0.5 : Still ? 1 : Math.Max(0.35, _shown));
            var pc = PillCenter;
            var pill = new SKRect(pc.X - 86, pc.Y - 24, pc.X + 86, pc.Y + 24);

            if (c != null && armed) DrawReaction(canvas, c, ct);
            DrawPill(canvas, pill, armed, a);
            DrawWave(canvas, pill, s, c, waveCt < 0 ? ct : waveCt, t, armed, a);
            if (s.SpeechPushToTalkEnabled) DrawKey(canvas, pill, c, ct, armed, a);
            if (c != null && armed) DrawChips(canvas, c, ct);
        }

        private Cycle StillCycle(AppSettings s)
        {
            bool key = s.SpeechPushToTalkEnabled, gate = !key && s.SpeechWakeWordEnabled;
            var c = new Cycle
            {
                Word = Words[0], Gate = gate, Key = key, Reaction = 0, Hue = 2,
                WakePeak = 0.9f, WordPeak = 0.9f, WakeHeard = gate, WakeTaken = gate, WordHeard = true, WordTaken = true,
                ReactAt = new SKPoint(_band.Left + _band.Width * 0.6f, _band.Top + _band.Height * 0.74f),
            };
            double at = key ? 0.25 : 0;
            if (gate) { c.WakeSwell = at; c.WakeAppear = at + 0.25; c.WakeDecide = at + 0.48; at += 0.9; }
            c.WordSwell = at; c.WordAppear = at + 0.28; c.WordDecide = at + 0.5;
            c.KeyUp = key ? at + Swell + 0.35 : 0;
            c.End = c.WordDecide + 1.7;
            return c;
        }

        private void DrawPill(SKCanvas canvas, SKRect pill, bool armed, float a)
        {
            _fill.Color = new SKColor(0, 0, 0, (byte)(60 * a));
            canvas.DrawRoundRect(new SKRect(pill.Left + 1, pill.Top + 3, pill.Right + 1, pill.Bottom + 4), 24, 24, _fill);
            _fill.Color = new SKColor(18, 14, 34, (byte)(232 * Math.Max(a, 0.9f)));
            canvas.DrawRoundRect(pill, 24, 24, _fill);
            _stroke.StrokeWidth = 1.2f;
            _stroke.Color = armed ? Coral.WithAlpha((byte)(150 * a)) : new SKColor(255, 255, 255, (byte)(50 * a));
            canvas.DrawRoundRect(pill, 24, 24, _stroke);

            // The mic mark: a soft disc, a capsule, its cradle and stem.
            var m = new SKPoint(pill.Left + 26, pill.MidY);
            _fill.Color = armed ? Coral.WithAlpha((byte)(70 * a)) : new SKColor(255, 255, 255, (byte)(22 * a));
            canvas.DrawCircle(m, 16, _fill);
            var ink = armed ? new SKColor(255, 236, 230, (byte)(255 * a)) : new SKColor(170, 165, 190, (byte)(200 * a));
            _fill.Color = ink;
            canvas.DrawRoundRect(new SKRect(m.X - 4, m.Y - 10, m.X + 4, m.Y + 3), 4, 4, _fill);
            _stroke.Color = ink;
            _stroke.StrokeWidth = 1.8f;
            canvas.DrawArc(new SKRect(m.X - 7, m.Y - 7, m.X + 7, m.Y + 6), 20, 140, false, _stroke);
            canvas.DrawLine(m.X, m.Y + 6, m.X, m.Y + 10, _stroke);
            canvas.DrawLine(m.X - 4, m.Y + 10, m.X + 4, m.Y + 10, _stroke);
        }

        /// <summary>The wave's loudness (0..1) at cycle time <paramref name="ct"/>.</summary>
        private double Loudness(AppSettings s, Cycle? c, double ct)
        {
            double room = 0.08 + 0.14 * Sensitivity(s) / 100.0;
            if (c == null) return s.SpeechPushToTalkEnabled ? 0 : room;
            if (c.Key && (ct < 0.08 || ct > c.KeyUp)) return 0;
            // Headphones off: the app's own reply is not listened to, the wave lies flat for a moment.
            if (!s.SpeechHeadphonesMode && c.WordTaken && ct > c.WordDecide + 0.3 && ct < c.WordDecide + 1.1) return 0;
            double v = room;
            if (c.Gate) v = Math.Max(v, Bump(ct - c.WakeSwell) * c.WakePeak);
            if (!c.Gate || c.WakeTaken) v = Math.Max(v, Bump(ct - c.WordSwell) * c.WordPeak);
            return v;
        }

        private static double Bump(double x) => x <= 0 || x >= Swell ? 0
            : x < SwellRise ? Math.Sin(Math.PI / 2 * x / SwellRise) : Math.Cos(Math.PI / 2 * (x - SwellRise) / (Swell - SwellRise));

        private void DrawWave(SKCanvas canvas, SKRect pill, AppSettings s, Cycle? c, double ct, double t, bool armed, float a)
        {
            float left = pill.Left + 52, right = pill.Right - 18, mid = pill.MidY, half = 15;
            float step = (right - left) / (Bars - 1);
            double loud = armed ? Loudness(s, c, ct) : 0;
            _stroke.StrokeWidth = 3.4f;
            for (int i = 0; i < Bars; i++)
            {
                double shape = 0.5 + 0.5 * Math.Sin(Math.PI * (i + 0.5) / Bars);
                double jitter = 0.62 + 0.38 * Math.Sin(t * 9.3 + i * 1.9) * Math.Cos(t * 4.1 + i * 0.7);
                float h = (float)Math.Max(1.2, half * Math.Clamp(loud * shape * (0.7 + 0.3 * jitter) / 0.7, 0, 1));
                float x = left + i * step;
                byte al = (byte)(255 * a * (armed ? 0.55 + 0.45 * Math.Min(1, loud * 1.6) : 0.55));
                _stroke.Color = armed ? new SKColor(255, 160, 150, al) : new SKColor(150, 145, 170, al);
                canvas.DrawLine(x, mid - h, x, mid + h, _stroke);
            }
            if (!armed) return;
            // The sensitivity marks: a swell must reach them to be heard.
            float need = half * Math.Min(1, Need(s) / 0.7f);
            _stroke.StrokeWidth = 1;
            _stroke.Color = new SKColor(255, 255, 255, (byte)(70 * a));
            canvas.DrawLine(left - 6, mid - need, left - 2, mid - need, _stroke);
            canvas.DrawLine(left - 6, mid + need, left - 2, mid + need, _stroke);
            canvas.DrawLine(right + 2, mid - need, right + 6, mid - need, _stroke);
            canvas.DrawLine(right + 2, mid + need, right + 6, mid + need, _stroke);
        }

        /// <summary>The push-to-talk key cap left of the pill: pressed in and lit while held.</summary>
        private void DrawKey(SKCanvas canvas, SKRect pill, Cycle? c, double ct, bool armed, float a)
        {
            bool held = armed && c != null && c.Key && ct >= 0.08 && ct <= c.KeyUp;
            float k = 30, x = pill.Left - k - 12, y = pill.MidY - k / 2 + (held ? 2 : 0);
            var cap = new SKRect(x, y, x + k, y + k);
            if (!held)
            {
                _fill.Color = new SKColor(0, 0, 0, (byte)(90 * a));
                canvas.DrawRoundRect(new SKRect(cap.Left, cap.Top + 3, cap.Right, cap.Bottom + 3), 6, 6, _fill);
            }
            _fill.Color = held ? Coral.WithAlpha((byte)(235 * a)) : new SKColor(60, 54, 84, (byte)(235 * a));
            canvas.DrawRoundRect(cap, 6, 6, _fill);
            _fill.Color = held ? new SKColor(255, 255, 255, (byte)(70 * a)) : new SKColor(255, 255, 255, (byte)(28 * a));
            canvas.DrawRoundRect(new SKRect(cap.Left + 4, cap.Top + 3, cap.Right - 4, cap.Bottom - 7), 4, 4, _fill);
            _fill.Color = new SKColor(255, 255, 255, (byte)((held ? 230 : 120) * a));
            canvas.DrawRoundRect(new SKRect(cap.MidX - 6, cap.MidY - 2.5f, cap.MidX + 6, cap.MidY + 0.5f), 1.5f, 1.5f, _fill);
        }

        /// <summary>0..1 fade-in from <paramref name="from"/> (the rise floor when gentle).</summary>
        private double Rise(double ct, double from)
        {
            if (Still) return ct >= from ? 1 : 0;
            double d = Gentle ? Math.Max(0.3, GentleVisuals.RiseFloorMs / 1000.0) : 0.18;
            return Math.Clamp((ct - from) / d, 0, 1);
        }

        private void DrawChips(SKCanvas canvas, Cycle c, double ct)
        {
            var cc = ChipCenter;
            _text.TextSize = 13;
            float wordW = _text.MeasureText(c.Word) + 26;
            bool wake = c.Gate && c.WakeHeard;
            float total = (wake ? 30 + 8 : 0) + wordW;
            float x0 = cc.X - total / 2;
            double fadeOut = Math.Clamp((ct - (c.End - 0.45)) / 0.45, 0, 1);

            if (wake && ct >= c.WakeAppear)
            {
                double al = Rise(ct, c.WakeAppear);
                bool lit = c.WakeTaken && ct >= c.WakeDecide;
                double drop = 0;
                if (!c.WakeTaken && ct >= c.WakeDecide) { double f = Math.Clamp((ct - c.WakeDecide) / 0.6, 0, 1); al *= 1 - f; drop = 8 * f; }
                al *= 1 - fadeOut;
                if (al > 0.01) DrawBell(canvas, new SKPoint(x0 + 15, cc.Y + (float)drop), lit, al, Pop(ct, c.WakeDecide, lit));
            }
            float wx = x0 + (wake ? 38 : 0);
            if (c.WordHeard && ct >= c.WordAppear)
            {
                double al = Rise(ct, c.WordAppear);
                bool lit = c.WordTaken && ct >= c.WordDecide;
                double drop = 0;
                if (!c.WordTaken && ct >= c.WordDecide) { double f = Math.Clamp((ct - c.WordDecide) / 0.6, 0, 1); al *= 1 - f; drop = 8 * f; }
                al *= 1 - fadeOut;
                var r = new SKRect(wx, cc.Y - 14 + (float)drop, wx + wordW, cc.Y + 14 + (float)drop);
                if (al > 0.01) DrawWordChip(canvas, r, c.Word, lit, al, Pop(ct, c.WordDecide, lit));
                if (lit && !Still) Ring(canvas, new SKPoint(r.MidX, r.MidY), ct - c.WordDecide, 0.9, 14, 70);
            }
        }

        private double Pop(double ct, double at, bool lit)
            => !lit || Still || Gentle ? 1 : 1 + 0.12 * Math.Sin(Math.PI * Math.Clamp((ct - at) / 0.25, 0, 1));

        private void DrawBell(SKCanvas canvas, SKPoint p, bool lit, double al, double scale)
        {
            byte A(double v) => (byte)Math.Clamp(v * al * 255, 0, 255);
            canvas.Save();
            canvas.Translate(p.X, p.Y);
            canvas.Scale((float)scale);
            _fill.Color = lit ? Coral.WithAlpha(A(1)) : new SKColor(40, 34, 62, A(0.92));
            canvas.DrawCircle(0, 0, 14, _fill);
            _stroke.StrokeWidth = 1.2f;
            _stroke.Color = lit ? new SKColor(255, 255, 255, A(0.6)) : new SKColor(255, 255, 255, A(0.35));
            canvas.DrawCircle(0, 0, 14, _stroke);
            // A small bell: a dome, its lip and the clapper.
            _path.Rewind();
            _path.MoveTo(-6, 4);
            _path.CubicTo(-5, 1, -5.5f, -6, 0, -6.5f);
            _path.CubicTo(5.5f, -6, 5, 1, 6, 4);
            _path.Close();
            _fill.Color = lit ? new SKColor(255, 255, 255, A(1)) : new SKColor(200, 195, 220, A(0.9));
            canvas.DrawPath(_path, _fill);
            canvas.DrawRoundRect(new SKRect(-7, 3.5f, 7, 5.5f), 1, 1, _fill);
            canvas.DrawCircle(0, 7.5f, 1.8f, _fill);
            canvas.DrawCircle(0, -7.5f, 1.2f, _fill);
            canvas.Restore();
        }

        private void DrawWordChip(SKCanvas canvas, SKRect r, string word, bool lit, double al, double scale)
        {
            byte A(double v) => (byte)Math.Clamp(v * al * 255, 0, 255);
            canvas.Save();
            canvas.Translate(r.MidX, r.MidY);
            canvas.Scale((float)scale);
            var b = new SKRect(-r.Width / 2, -r.Height / 2, r.Width / 2, r.Height / 2);
            if (lit)
            {
                _fill.Color = Coral.WithAlpha(A(0.28));
                canvas.DrawRoundRect(new SKRect(b.Left - 4, b.Top - 4, b.Right + 4, b.Bottom + 4), 18, 18, _fill);
            }
            _fill.Color = lit ? Coral.WithAlpha(A(1)) : new SKColor(40, 34, 62, A(0.92));
            canvas.DrawRoundRect(b, 14, 14, _fill);
            _stroke.StrokeWidth = 1.2f;
            _stroke.Color = new SKColor(255, 255, 255, A(lit ? 0.6 : 0.3));
            canvas.DrawRoundRect(b, 14, 14, _stroke);
            _text.TextSize = 13;
            _text.Color = lit ? new SKColor(255, 255, 255, A(1)) : new SKColor(200, 195, 220, A(0.75));
            canvas.DrawText(word, 0, 4.6f, _text);
            canvas.Restore();
        }

        private void Ring(SKCanvas canvas, SKPoint c, double since, double life, float from, float to)
        {
            if (since < 0 || since >= life) return;
            double p = since / life;
            double al = Math.Min(1, p / 0.15) * (1 - p) * (1 - p) * (Gentle ? 0.5 : 0.8);
            _stroke.StrokeWidth = (float)(4 - 2.5 * p);
            _stroke.Color = Coral.WithAlpha((byte)Math.Clamp(al * 255, 0, 255));
            canvas.DrawCircle(c, from + (to - from) * (float)(1 - (1 - p) * (1 - p)), _stroke);
        }

        /// <summary>What a taken word does on the slice: a fake flash card, or a bubble that pops.</summary>
        private void DrawReaction(SKCanvas canvas, Cycle c, double ct)
        {
            if (!c.WordTaken) return;
            double since = ct - (c.WordDecide + 0.08);
            if (since < 0) return;
            double life = c.End - (c.WordDecide + 0.08);
            var p = c.ReactAt;
            if (c.Reaction == 0)
            {
                var img = _cards[c.Hue % _cards.Length] ??= PreviewPictures.Make(c.Hue, PreviewPictures.Aspects[c.Hue % 3], 135, 200);
                double rise = Rise(since, 0);
                double fall = Still ? 0 : Math.Clamp((since - (life - 0.5)) / 0.5, 0, 1);
                double al = rise * (1 - fall);
                if (al <= 0.01) return;
                float scale = (float)(Still || Gentle ? 1 : 0.86 + 0.14 * Math.Min(1, since / 0.22));
                float w = 118 * scale, h = w * img.Height / img.Width;
                var r = new SKRect(p.X - w / 2, p.Y - h / 2, p.X + w / 2, p.Y + h / 2);
                _fill.Color = new SKColor(0, 0, 0, (byte)(70 * al));
                canvas.DrawRoundRect(new SKRect(r.Left + 2, r.Top + 4, r.Right + 2, r.Bottom + 5), 6, 6, _fill);
                _image.Color = new SKColor(255, 255, 255, (byte)(255 * al));
                canvas.Save();
                using (var rr = new SKRoundRect(r, 6, 6)) canvas.ClipRoundRect(rr, antialias: true);
                canvas.DrawImage(img, r, _image);
                canvas.Restore();
                _stroke.StrokeWidth = 1.5f;
                _stroke.Color = new SKColor(255, 255, 255, (byte)(120 * al));
                canvas.DrawRoundRect(r, 6, 6, _stroke);
                if (!Still) Ring(canvas, new SKPoint(r.MidX, r.MidY), since, 0.8, w * 0.45f, w * 0.75f);
            }
            else
            {
                // A bubble rises a little, then pops into a soft ring and a few drops.
                const double popAt = 0.75;
                if (since < popAt || Still)
                {
                    double al = Rise(since, 0);
                    float y = p.Y - (float)(Still ? 10 : 18 * since);
                    _fill.Color = new SKColor(255, 170, 220, (byte)(60 * al));
                    canvas.DrawCircle(p.X, y, 26, _fill);
                    _stroke.StrokeWidth = 2;
                    _stroke.Color = new SKColor(255, 220, 240, (byte)(200 * al));
                    canvas.DrawCircle(p.X, y, 26, _stroke);
                    _fill.Color = new SKColor(255, 255, 255, (byte)(190 * al));
                    canvas.DrawOval(new SKRect(p.X - 15, y - 17, p.X - 5, y - 10), _fill);
                    return;
                }
                double q = since - popAt;
                float cy = p.Y - (float)(18 * popAt);
                Ring(canvas, new SKPoint(p.X, cy), q, 0.6, 24, 46);
                if (Gentle || q >= 0.5) return;
                double f = q / 0.5;
                _fill.Color = new SKColor(255, 210, 235, (byte)(200 * (1 - f)));
                for (int i = 0; i < 6; i++)
                {
                    double ang = i * Math.PI / 3 + 0.4;
                    float d = 26 + (float)(22 * f);
                    canvas.DrawCircle(p.X + (float)Math.Cos(ang) * d, cy + (float)Math.Sin(ang) * d + (float)(10 * f * f), 2.4f, _fill);
                }
            }
        }

        // ------------------------------------------------------------------ input

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            // A changed setting plays at once: a new word comes soon (the one in flight finishes first).
            if (_cycle == null) _next = Math.Min(_next, _t + 0.15);
        }

        /// <summary>A click hears one word now (a clear one), unless the master switch is off.</summary>
        public void Click(SKPoint at)
        {
            if (_disposed) return;
            var s = S;
            if (!Armed(s)) return;
            if (_cycle != null && _t - _cycle.Start < _cycle.End - 0.5) return;
            Begin(s, forced: true);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
            _image.Dispose();
            _path.Dispose();
            for (int i = 0; i < _cards.Length; i++) { _cards[i]?.Dispose(); _cards[i] = null; }
        }
    }
}
