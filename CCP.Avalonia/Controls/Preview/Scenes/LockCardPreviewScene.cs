using System;
using ConditioningControlPanel.Avalonia;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Lock Card in the live preview (overnight sweep, lane P1, 2026-11-24). A drawn card in the middle of
    /// the visible band: a neutral word ("RELAX", "DEEPER", "FOCUS"; never the player's own phrases and
    /// never the completion line), an input box that types itself letter by letter, a progress bar, and
    /// one dot per repeat that fills as each repeat is typed. When the last repeat is in, the card ticks
    /// and fades; the next one comes after a compressed gap (1 an hour = 5 s, 10 an hour = 1.5 s).
    ///
    /// <para>Follows: Frequency, Repeats (or a roll between Fewest and it when Random is on), Strict (a
    /// small padlock on the card), Reset on typo (now and then a red letter: on, the repeat starts over;
    /// off, the letter is taken back), and the five card colours. A click types the next letters at
    /// once. Voice mode and the target length do nothing here.</para>
    /// </summary>
    public sealed class LockCardPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.LockCardFrequency), nameof(AppSettings.LockCardRepeats), nameof(AppSettings.LockCardRandomRepeats),
            nameof(AppSettings.LockCardRepeatsMin), nameof(AppSettings.LockCardStrict), nameof(AppSettings.LockCardResetOnTypo),
            nameof(AppSettings.LockCardBackgroundColor), nameof(AppSettings.LockCardTextColor),
            nameof(AppSettings.LockCardInputBackgroundColor), nameof(AppSettings.LockCardInputTextColor),
            nameof(AppSettings.LockCardAccentColor),
        };

        /// <summary>The neutral words a preview card asks for.</summary>
        public static readonly string[] Words = { "RELAX", "DEEPER", "FOCUS" };

        public const double CharsPerSecond = 8.0;
        private const double FadeIn = 0.35, FadeOut = 0.45, DoneHold = 0.7, TypoHold = 0.35;

        private enum Phase { Waiting, In, Typing, Done, Out }

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Random _rng;
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
        private static readonly SKTypeface Bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private Phase _phase = Phase.Waiting;
        private double _t, _phaseAt, _next = 0.5, _typed, _typoAt = -1;
        private int _word, _repeats = 1, _done, _typoChar = -1;
        private bool _disposed;

        /// <summary>The page's scene (found by name, built without arguments).</summary>
        public LockCardPreviewScene() : this(settings: null) { }

        public LockCardPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.ResolveLevel(App.Settings?.Current?.MotionLevel ?? MotionLevel.Full, global::ConditioningControlPanel.Avalonia.Platform.UiMotionPreference.AnimationsEnabled, GentleVisuals.On));
            _gentle = gentle ?? (() => GentleVisuals.On);
            _rng = rng ?? new Random();
        }

        public string[] Settings => Watched;

        /// <summary>How often a repeat carries one wrong letter (tests set 0).</summary>
        internal double TypoChance { get; set; } = 0.35;

        // ---- read-outs for tests ----
        public bool CardUp => _phase is Phase.In or Phase.Typing or Phase.Done or Phase.Out;
        public bool Typing => _phase == Phase.Typing;
        public bool Finished => _phase is Phase.Done or Phase.Out;
        public int Repeats => _repeats;
        public int RepeatsDone => _done;
        public int Typed => (int)Math.Floor(_typed);
        public string Word => Words[_word];
        public double Alpha => CardAlpha();
        public double Clock => _t;
        public int CardsFinished { get; private set; }
        public SKRect Card => CardRect();

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
        }

        public void Start()
        {
            if (_phase == Phase.Waiting) _next = Math.Min(_next, _t + 0.15);
        }

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            var s = S;
            switch (_phase)
            {
                case Phase.Waiting:
                    if (_t >= _next) Show(s);
                    break;
                case Phase.In:
                    if (_t - _phaseAt >= Fade(FadeIn)) Go(Phase.Typing);
                    break;
                case Phase.Typing:
                    Type(s, dt * CharsPerSecond);
                    break;
                case Phase.Done:
                    if (_t - _phaseAt >= DoneHold) Go(Phase.Out);
                    break;
                case Phase.Out:
                    if (_t - _phaseAt >= Fade(FadeOut))
                    {
                        CardsFinished++;
                        _phase = Phase.Waiting;
                        _next = _t + Gap(s);
                    }
                    break;
            }
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            var s = S;
            switch (name)
            {
                case nameof(AppSettings.LockCardRepeats):
                case nameof(AppSettings.LockCardRandomRepeats):
                case nameof(AppSettings.LockCardRepeatsMin):
                    if (CardUp && !Finished) { _repeats = Roll(s); _done = Math.Min(_done, _repeats - 1); }
                    break;
            }
            // A changed setting plays at once: a card comes soon if none is up.
            if (_phase == Phase.Waiting) _next = Math.Min(_next, _t + 0.15);
        }

        public void Click(SKPoint at)
        {
            if (_disposed) return;
            if (_phase == Phase.Typing) Type(S, 2);
            else if (_phase == Phase.Waiting) _next = Math.Min(_next, _t + 0.15);
        }

        // ---------------------------------------------------------------- driver

        private double Gap(AppSettings s) => 5.0 - 3.5 * (Math.Clamp(s.LockCardFrequency, 1, 10) - 1) / 9.0;

        private static int Roll(AppSettings s, Random? rng = null)
        {
            int max = Math.Clamp(s.LockCardRepeats, 1, 10);
            if (!s.LockCardRandomRepeats) return max;
            int min = Math.Clamp(s.LockCardRepeatsMin, 1, max);
            return (rng ?? Shared).Next(min, max + 1);
        }

        private static readonly Random Shared = new();

        private double Fade(double secs)
        {
            if (Off) return 0;
            if (!Gentle) return secs;
            return Math.Max(secs * 1.6, GentleVisuals.RiseFloorMs / 1000.0);
        }

        private void Go(Phase p) { _phase = p; _phaseAt = _t; }

        private void Show(AppSettings s)
        {
            _word = _rng.Next(Words.Length);
            _repeats = Roll(s, _rng);
            _done = 0;
            NewRepeat(s);
            Go(Phase.In);
        }

        private void NewRepeat(AppSettings s)
        {
            _typed = 0;
            _typoAt = -1;
            int n = Words[_word].Length;
            _typoChar = n > 2 && _rng.NextDouble() < TypoChance ? _rng.Next(1, n - 1) : -1;
        }

        private void Type(AppSettings s, double chars)
        {
            int n = Words[_word].Length;
            if (_typoAt >= 0)
            {
                if (_t - _typoAt < TypoHold) return;
                // The red letter goes: the repeat starts over (Reset on typo) or the letter is taken back.
                _typoAt = -1;
                _typoChar = -1;
                if (s.LockCardResetOnTypo) _typed = 0;
                return;
            }
            _typed = Math.Min(n, _typed + chars);
            if (_typoChar >= 0 && _typed >= _typoChar)
            {
                _typed = _typoChar;
                _typoAt = _t;
                return;
            }
            if (_typed < n) return;
            _done++;
            if (_done >= _repeats) { Go(Phase.Done); return; }
            NewRepeat(s);
        }

        private double CardAlpha()
        {
            double since = _t - _phaseAt;
            switch (_phase)
            {
                case Phase.In: { var f = Fade(FadeIn); return f <= 0 ? 1 : Math.Clamp(since / f, 0, 1); }
                case Phase.Typing:
                case Phase.Done: return 1;
                case Phase.Out: { var f = Fade(FadeOut); return f <= 0 ? 0 : Math.Clamp(1 - since / f, 0, 1); }
                default: return 0;
            }
        }

        private SKRect CardRect()
        {
            float w = Math.Min(_screen.Width * 0.86f, 340), h = 214;
            float cx = _screen.MidX, cy = _band.MidY;
            return new SKRect(cx - w / 2, cy - h / 2, cx + w / 2, cy + h / 2);
        }

        // ---------------------------------------------------------------- drawing

        public void Render(SKCanvas c, SKRect screen, SKRect visible)
        {
            if (_disposed || !CardUp) return;
            double alpha = CardAlpha();
            if (alpha <= 0.004) return;
            var s = S;
            byte a = (byte)(255 * alpha);
            var bg = Colour(s.LockCardBackgroundColor, 0x1a1a2e);
            var ink = Colour(s.LockCardTextColor, 0xff69b4);
            var inBg = Colour(s.LockCardInputBackgroundColor, 0x252542);
            var inInk = Colour(s.LockCardInputTextColor, 0xffffff);
            var accent = Colour(s.LockCardAccentColor, 0xff69b4);

            // The room dims behind the card, as the real card does.
            _fill.Color = new SKColor(0, 0, 0, (byte)(110 * alpha));
            c.DrawRect(new SKRect(_screen.Left, _band.Top - 40, _screen.Right, _band.Bottom + 40), _fill);

            var r = CardRect();
            float rise = (float)((1 - alpha) * 10);
            r.Offset(0, rise);
            _fill.Color = new SKColor(0, 0, 0, (byte)(90 * alpha));
            c.DrawRoundRect(new SKRect(r.Left + 2, r.Top + 6, r.Right + 2, r.Bottom + 8), 16, 16, _fill);
            _fill.Color = bg.WithAlpha(a);
            c.DrawRoundRect(r, 16, 16, _fill);
            _stroke.StrokeWidth = 2;
            _stroke.Color = accent.WithAlpha((byte)(a * 0.8));
            c.DrawRoundRect(Inset(r, 1), 15, 15, _stroke);

            if (s.LockCardStrict) Padlock(c, new SKPoint(r.Right - 24, r.Top + 24), accent.WithAlpha(a));

            // The word to type.
            string word = Words[_word];
            _text.Typeface = Bold;
            _text.TextSize = 38;
            _text.Color = ink.WithAlpha(a);
            c.DrawText(word, r.MidX, r.Top + 76, _text);

            // The input box, typing itself; a typo shows as one red letter.
            var box = new SKRect(r.Left + 22, r.Top + 98, r.Right - 22, r.Top + 140);
            _fill.Color = inBg.WithAlpha(a);
            c.DrawRoundRect(box, 9, 9, _fill);
            _stroke.StrokeWidth = 1.5f;
            _stroke.Color = accent.WithAlpha((byte)(a * (_phase == Phase.Typing ? 0.9 : 0.4)));
            c.DrawRoundRect(box, 9, 9, _stroke);
            int typed = _phase is Phase.Done or Phase.Out ? word.Length : Typed;
            string shown = word.Substring(0, Math.Min(typed, word.Length));
            _text.TextSize = 22;
            _text.TextAlign = SKTextAlign.Left;
            float tx = box.Left + 14, ty = box.MidY + 8;
            _text.Color = inInk.WithAlpha(a);
            c.DrawText(shown, tx, ty, _text);
            float after = tx + _text.MeasureText(shown);
            if (_typoAt >= 0)
            {
                _text.Color = new SKColor(0xff, 0x4d, 0x5e, a);
                char wrong = (char)('A' + ((word[Math.Min(typed, word.Length - 1)] - 'A' + 3) % 26));
                c.DrawText(wrong.ToString(), after, ty, _text);
                after += _text.MeasureText(wrong.ToString());
            }
            if (_phase == Phase.Typing && ((int)(_t * 2.4)) % 2 == 0)
            {
                _fill.Color = inInk.WithAlpha((byte)(a * 0.8));
                c.DrawRect(new SKRect(after + 2, box.MidY - 11, after + 4, box.MidY + 11), _fill);
            }
            _text.TextAlign = SKTextAlign.Center;

            // Progress over the whole card (every repeat), then one dot per repeat.
            double total = _repeats * word.Length;
            double got = _phase is Phase.Done or Phase.Out ? total : _done * word.Length + Math.Min(typed, word.Length);
            var bar = new SKRect(r.Left + 22, r.Top + 156, r.Right - 22, r.Top + 162);
            _fill.Color = SKColors.White.WithAlpha((byte)(a * 0.16));
            c.DrawRoundRect(bar, 3, 3, _fill);
            _fill.Color = accent.WithAlpha(a);
            c.DrawRoundRect(new SKRect(bar.Left, bar.Top, bar.Left + bar.Width * (float)Math.Clamp(got / Math.Max(1, total), 0, 1), bar.Bottom), 3, 3, _fill);

            float dot = 5, gap = 9, rowW = _repeats * dot * 2 + (_repeats - 1) * gap, dx = r.MidX - rowW / 2 + dot, dy = r.Top + 186;
            int filled = _phase is Phase.Done or Phase.Out ? _repeats : _done;
            for (int i = 0; i < _repeats; i++)
            {
                var at = new SKPoint(dx + i * (dot * 2 + gap), dy);
                if (i < filled) { _fill.Color = accent.WithAlpha(a); c.DrawCircle(at, dot, _fill); }
                else { _stroke.StrokeWidth = 1.5f; _stroke.Color = accent.WithAlpha((byte)(a * 0.6)); c.DrawCircle(at, dot - 0.75f, _stroke); }
            }

            if (_phase is Phase.Done or Phase.Out) Tick(c, new SKPoint(box.Right - 22, box.MidY), accent.WithAlpha(a));
        }

        private void Padlock(SKCanvas c, SKPoint at, SKColor colour)
        {
            _stroke.StrokeWidth = 2.4f;
            _stroke.Color = colour;
            using (var shackle = new SKPath())
            {
                shackle.MoveTo(at.X - 5.5f, at.Y);
                shackle.LineTo(at.X - 5.5f, at.Y - 4);
                shackle.ArcTo(new SKRect(at.X - 5.5f, at.Y - 10, at.X + 5.5f, at.Y + 1), 180, 180, false);
                shackle.LineTo(at.X + 5.5f, at.Y);
                c.DrawPath(shackle, _stroke);
            }
            _fill.Color = colour;
            c.DrawRoundRect(new SKRect(at.X - 8.5f, at.Y - 1, at.X + 8.5f, at.Y + 11), 2.5f, 2.5f, _fill);
        }

        private void Tick(SKCanvas c, SKPoint at, SKColor colour)
        {
            _stroke.StrokeWidth = 3;
            _stroke.StrokeCap = SKStrokeCap.Round;
            _stroke.Color = colour;
            using var p = new SKPath();
            p.MoveTo(at.X - 8, at.Y);
            p.LineTo(at.X - 2, at.Y + 6);
            p.LineTo(at.X + 9, at.Y - 7);
            c.DrawPath(p, _stroke);
            _stroke.StrokeCap = SKStrokeCap.Butt;
        }

        private static SKColor Colour(string? hex, uint fallback)
            => !string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex.Trim(), out var c) ? c.WithAlpha(255) : PreviewDesktop.Css(fallback);

        private static SKRect Inset(SKRect r, float by) => new(r.Left + by, r.Top + by, r.Right - by, r.Bottom - by);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _fill.Dispose();
            _stroke.Dispose();
            _text.Dispose();
        }
    }
}
