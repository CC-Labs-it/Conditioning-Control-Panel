using System;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Takeover in the live preview (second batch, lane P5, 2026-11-24). A DRAWN stand-in: one browser
    /// window on the slice (title bar, a tab, an address pill of bars, a grey video box), no real browser,
    /// no site, no sound. A compressed takeover plays on it, each beat only while its switch is on:
    ///
    /// <list type="bullet">
    /// <item>Announce (<see cref="AppSettings.AutonomyAnnouncementChance"/> rolled per takeover): a small
    /// notice card slides in at the top right first.</item>
    /// <item>Web video (<see cref="AppSettings.AutonomyCanTriggerWebVideo"/>, "plays fullscreen in browser"):
    /// the video starts and the window grows to fill the slice, then eases back.</item>
    /// <item>Wallpaper (<see cref="AppSettings.AutonomyCanTriggerWallpaper"/>): the slice's wallpaper, under
    /// its icons and windows, crossfades to a fake one while it lasts and comes back after the compressed
    /// <see cref="AppSettings.WallpaperPulseSeconds"/>, or stays with <see cref="AppSettings.WallpaperEnabled"/>
    /// (Keep) until the next takeover brings another.</item>
    /// <item>Mantras (<see cref="AppSettings.AutonomyCanTriggerVoiceCommand"/> and mic consent, as the tab's
    /// switch reads it): one neutral word, sized a little by Intensity. Chant
    /// (<see cref="AppSettings.MantraChantEnabled"/>): a soft silent ring pulses round it, its pace from the
    /// gap, its reach from the volume.</item>
    /// <item>Countdown bar (<see cref="AppSettings.ShowTakeoverCountdownBar"/>): between takeovers a thin bar
    /// above the taskbar runs down to the next one.</item>
    /// </list>
    ///
    /// <para>Pace: Interval 30 s to 300 s is a wait of 2 s to 6 s, Cooldown 10 s to 300 s at least 1 s to 4 s;
    /// nothing on screen states the real figure. With neither the Random nor the Idle trigger on, only the
    /// open, a change and a click start one. A click starts a takeover now. Audio, haptics, companion
    /// comments, browser protection and the other behaviours do nothing here. Motion Off is a still of the
    /// takeover's middle.</para>
    /// </summary>
    public sealed class TakeoverPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.AutonomyRandomIntervalSeconds), nameof(AppSettings.AutonomyCooldownSeconds),
            nameof(AppSettings.AutonomyIntensity), nameof(AppSettings.AutonomyAnnouncementChance),
            nameof(AppSettings.AutonomyRandomTriggerEnabled), nameof(AppSettings.AutonomyIdleTriggerEnabled),
            nameof(AppSettings.AutonomyCanTriggerWebVideo), nameof(AppSettings.AutonomyCanTriggerWallpaper),
            nameof(AppSettings.WallpaperEnabled), nameof(AppSettings.WallpaperPulseSeconds),
            nameof(AppSettings.AutonomyCanTriggerVoiceCommand), nameof(AppSettings.MicConsentGiven),
            nameof(AppSettings.MantraChantEnabled), nameof(AppSettings.MantraChantGapSeconds), nameof(AppSettings.MantraChantVolume),
            nameof(AppSettings.ShowTakeoverCountdownBar),
        };

        /// <summary>The words a mantra shows (neutral, never the mod's own mantras).</summary>
        public static readonly string[] Words = { "FOCUS", "DEEPER", "RELAX" };

        // Timeline of one takeover in seconds at the Full pace; Gentle and Reduced stretch it by PaceGentle.
        private const double NoticeLead = 1.0, NoticeIn = 0.35, NoticeOut = 0.4, NoticeLeaveAt = 0.6;
        private const double GrowAt = 0.3, GrowLen = 0.8, MantraAt = 1.1, MantraIn = 0.4, MantraOutAt = 3.4, MantraOut = 0.45;
        private const double ShrinkAt = 3.8, ShrinkLen = 0.8, BodyLen = 4.6, WallFade = 0.6, EnvFade = 0.3;
        /// <summary>Where the Motion Off still sits in a takeover (body seconds).</summary>
        public const double MiddleSeconds = 2.4;
        private const double PaceGentle = 1.35;

        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Random _rng;
        private readonly SKPaint _fill = new() { IsAntialias = true };
        private readonly SKPaint _img = new() { IsAntialias = true, FilterQuality = SKFilterQuality.Low };
        private readonly SKPaint _stroke = new() { IsAntialias = true, Style = SKPaintStyle.Stroke };
        private readonly SKPaint _text = new() { IsAntialias = true, TextAlign = SKTextAlign.Center };
        private readonly SKImageFilter _shadow = SKImageFilter.CreateDropShadowOnly(0, 4, 7, 7, new SKColor(0, 0, 0, 110));
        private static readonly SKTypeface Bold = SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold) ?? SKTypeface.Default;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _start = double.NaN, _lead, _next = double.PositiveInfinity, _endedAt, _wallReturnAt = double.NegativeInfinity;
        private double _envNotice, _envGrow, _envMantra, _envChant, _envCountdown, _wallMix, _wallFadeAt = double.NegativeInfinity;
        private bool _announce, _forceAnnounce, _disposed, _started, _primed;
        private int _word = -1, _hue;
        private SKImage? _wall, _wallOld, _clip;

        /// <summary>The page's scene (found by name, built without arguments).</summary>
        public TakeoverPreviewScene() : this(settings: null) { }

        public TakeoverPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _rng = rng ?? new Random();
            _hue = _rng.Next(PreviewPictures.Hues.Length);
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests (each 0..1 is what is drawn right now) ----
        public bool InTakeover => !double.IsNaN(_start);
        public int Takeovers { get; private set; }
        public bool Announced => InTakeover && _announce;
        public double Notice { get; private set; }
        public double Grow { get; private set; }
        public double Mantra { get; private set; }
        public double Chant { get; private set; }
        public double Countdown { get; private set; }
        /// <summary>How much of the countdown is left (1 = just started, 0 = the next takeover is due).</summary>
        public double CountdownLeft { get; private set; } = 1;
        public double WallMix => _wallMix;
        public string Word => _word < 0 ? Words[0] : Words[_word % Words.Length];
        public double Clock => _t;
        public double NextAt => _next;

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private bool Off => _level() == MotionLevel.Off;
        private double Pace => _gentle() || _level() == MotionLevel.Reduced ? PaceGentle : 1.0;
        private float Floor => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);

        private static bool WebVideo(AppSettings s) => s.AutonomyCanTriggerWebVideo;
        private static bool Wallpaper(AppSettings s) => s.AutonomyCanTriggerWallpaper;
        private static bool Mantras(AppSettings s) => s.AutonomyCanTriggerVoiceCommand && s.MicConsentGiven;
        private static bool ChantOn(AppSettings s) => s.MantraChantEnabled;
        private static bool AutoTriggers(AppSettings s) => s.AutonomyRandomTriggerEnabled || s.AutonomyIdleTriggerEnabled;

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, screen.Bottom), bottom = Math.Clamp(visible.Bottom, screen.Top, screen.Bottom);
            _band = bottom - top < 120 ? screen : new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start()
        {
            if (_started) return;
            _started = true;
            Prime();
            _next = Math.Min(_next, _t + 0.8);
        }

        public void Tick(double dt)
        {
            if (_disposed) return;
            var s = S;
            Prime();
            if (Off) { Middle(s); return; }
            if (dt <= 0) return;
            _t += dt;
            if (!InTakeover && _t >= _next) Begin(s);
            Step(s, dt);
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            var s = S;
            if (name == nameof(AppSettings.AutonomyAnnouncementChance)) _forceAnnounce = s.AutonomyAnnouncementChance > 0;
            if (!InTakeover) _next = Math.Min(_next, _t + 0.3);
        }

        public void Click(SKPoint at)
        {
            if (_disposed || InTakeover) return;
            Begin(S);
        }

        // ---------------------------------------------------------------- driver

        /// <summary>The beats start where their switches are, so a switch that is off never shows at all.</summary>
        private void Prime()
        {
            if (_primed) return;
            _primed = true;
            var s = S;
            _envGrow = WebVideo(s) ? 1 : 0;
            _envMantra = Mantras(s) ? 1 : 0;
            _envChant = ChantOn(s) ? 1 : 0;
            _envNotice = 1;
        }

        private void Begin(AppSettings s)
        {
            _start = _t;
            int chance = Math.Clamp(s.AutonomyAnnouncementChance, 0, 100);
            _announce = chance > 0 && (_forceAnnounce || _rng.Next(100) < chance);
            _forceAnnounce = false;
            _lead = _announce ? NoticeLead : 0;
            _word++;
            _hue++;
            Takeovers++;
            _next = double.PositiveInfinity;
            _clip?.Dispose();
            _clip = PreviewPictures.Make(_hue, 1.36f, 120 + _rng.Next(50), 260);   // fresh per takeover
            if (Wallpaper(s)) NewWall();
        }

        private void End(AppSettings s)
        {
            _start = double.NaN;
            _endedAt = _t;
            double pulse = (Math.Clamp(s.WallpaperPulseSeconds, 10, 600) - 10) / 590.0;
            _wallReturnAt = _t + (0.3 + 2.7 * pulse) * Pace;
            _next = AutoTriggers(s) ? _t + Gap(s) : double.PositiveInfinity;
        }

        /// <summary>The wait to the next takeover: the compressed Interval, never under the compressed Cooldown.</summary>
        private double Gap(AppSettings s)
        {
            double i = (Math.Clamp(s.AutonomyRandomIntervalSeconds, 30, 300) - 30) / 270.0;
            double c = (Math.Clamp(s.AutonomyCooldownSeconds, 10, 300) - 10) / 290.0;
            return Math.Max(2.0 + 4.0 * i, 1.0 + 3.0 * c) * Pace;
        }

        private void NewWall()
        {
            if (_screen.Width <= 0 || _screen.Height <= 0) return;
            var next = PaintWall(_hue + 3);
            if (next == null) return;
            _wallOld?.Dispose();
            _wallOld = _wallMix > 0 ? _wall : null;
            if (_wallOld == null) _wall?.Dispose();
            _wall = next;
            _wallFadeAt = _wallOld != null ? _t : double.NegativeInfinity;
        }

        /// <summary>The fake wallpaper with the desktop's own windows, icons and taskbar baked over it, so the
        /// slice's wallpaper changes UNDER them; one image per takeover, drawn as one with one alpha.</summary>
        private SKImage? PaintWall(int hue)
        {
            int pw = Math.Max(8, (int)Math.Ceiling(_screen.Width)), ph = Math.Max(8, (int)Math.Ceiling(_screen.Height));
            using var surface = SKSurface.Create(new SKImageInfo(pw, ph, SKColorType.Bgra8888, SKAlphaType.Premul));
            if (surface == null) return null;
            var c = surface.Canvas;
            float w = _screen.Width, h = _screen.Height;
            // Warm hue pairs only, so the swap reads clearly against the stock blue desktop.
            int[] warm = { 0, 2, 4, 5 };
            using (var pic = PreviewPictures.Make(warm[((hue % warm.Length) + warm.Length) % warm.Length], 0.74f, 150 + _rng.Next(40), 320))
            {
                float iw = Math.Max(w, h * 0.74f), ih = iw / 0.74f;   // cover the slice
                c.DrawImage(pic, new SKRect(w / 2 - iw / 2, h / 2 - ih / 2, w / 2 + iw / 2, h / 2 + ih / 2), _img);
            }
            using (var dim = new SKPaint { Color = new SKColor(0x10, 0x08, 0x20, 70) }) c.DrawRect(0, 0, w, h, dim);
            PreviewDesktop.Paint(c, w, h, wallpaper: false);
            c.Flush();
            return surface.Snapshot();
        }

        private void Step(AppSettings s, double dt)
        {
            double k = Pace, ease = dt / (EnvFade * k);
            _envGrow = Toward(_envGrow, WebVideo(s) ? 1 : 0, ease);
            _envMantra = Toward(_envMantra, Mantras(s) ? 1 : 0, ease);
            _envChant = Toward(_envChant, ChantOn(s) ? 1 : 0, ease);
            _envNotice = Toward(_envNotice, s.AutonomyAnnouncementChance > 0 ? 1 : 0, ease);

            double u = InTakeover ? (_t - _start) / k : double.NaN;
            if (InTakeover && u >= _lead + BodyLen) { End(s); u = double.NaN; }
            Beats(s, u);

            // The wallpaper: up while a takeover's body runs, then until the pulse ends, or kept.
            bool wallOn = Wallpaper(s) && (
                (InTakeover && u >= _lead) || s.WallpaperEnabled || _t < _wallReturnAt);
            if (wallOn && _wall == null) NewWall();
            _wallMix = Toward(_wallMix, wallOn && _wall != null ? 1 : 0, dt / (WallFade * k));
            if (_wallOld != null && WallCross() >= 1) { _wallOld.Dispose(); _wallOld = null; }
            if (_wallMix <= 0 && !wallOn) { _wall?.Dispose(); _wall = null; _wallOld?.Dispose(); _wallOld = null; }

            bool counting = !InTakeover && s.ShowTakeoverCountdownBar && !double.IsPositiveInfinity(_next) && _next > _endedAt;
            _envCountdown = Toward(_envCountdown, counting ? 1 : 0, ease);
            // Eased, so a change that brings the next takeover nearer shortens the bar instead of cutting it.
            if (counting) CountdownLeft = Toward(CountdownLeft, Math.Clamp((_next - _t) / (_next - _endedAt), 0, 1), dt * 2.5);
            else if (Countdown <= 0) CountdownLeft = 1;
            Countdown = _envCountdown;
        }

        private double WallCross() => Math.Clamp((_t - _wallFadeAt) / (WallFade * Pace), 0, 1);

        /// <summary>Every beat's value from the takeover's own clock (body seconds) and its switch's envelope.</summary>
        private void Beats(AppSettings s, double u)
        {
            if (double.IsNaN(u)) { Notice = Grow = Mantra = Chant = 0; return; }
            double b = _lead;
            Notice = _announce ? Math.Min(Smooth(u / NoticeIn), 1 - Smooth((u - (b + NoticeLeaveAt)) / NoticeOut)) * _envNotice : 0;
            Grow = Smooth((u - (b + GrowAt)) / GrowLen) * (1 - Smooth((u - (b + ShrinkAt)) / ShrinkLen)) * _envGrow;
            double word = Smooth((u - (b + MantraAt)) / MantraIn) * (1 - Smooth((u - (b + MantraOutAt)) / MantraOut));
            Mantra = word * _envMantra;
            double period = 0.6 + Math.Clamp(s.MantraChantGapSeconds, 0, 60) / 60.0;
            double phase = u < b + MantraAt ? 0 : ((u - (b + MantraAt)) / period) % 1.0;
            _ringPhase = phase;
            Chant = word * _envChant * (1 - phase);
        }

        private double _ringPhase;

        /// <summary>Motion Off: the still of a takeover's middle, every beat where its switch puts it.</summary>
        private void Middle(AppSettings s)
        {
            if (!InTakeover) Begin(s);
            _announce = false;
            _lead = 0;
            _start = _t - MiddleSeconds * Pace;
            _envGrow = WebVideo(s) ? 1 : 0;
            _envMantra = Mantras(s) ? 1 : 0;
            _envChant = ChantOn(s) ? 1 : 0;
            Beats(s, MiddleSeconds);
            _ringPhase = 0.35;
            Chant = 0.65 * _envChant * (Mantra > 0 ? 1 : 0.8);
            if (Wallpaper(s) && _wall == null) NewWall();
            _wallMix = Wallpaper(s) && _wall != null ? 1 : 0;
            if (_wallOld != null) { _wallOld.Dispose(); _wallOld = null; }
            Countdown = _envCountdown = 0;
        }

        // ---------------------------------------------------------------- drawing

        public void Render(SKCanvas c, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            DrawWall(c, screen);
            DrawBrowser(c, screen);
            DrawMantra(c);
            DrawNotice(c);
            DrawCountdown(c);
        }

        private void DrawWall(SKCanvas c, SKRect screen)
        {
            if (_wallMix <= 0 || _wall == null) return;
            byte a = A(_wallMix);
            if (_wallOld != null)
            {
                _img.Color = SKColors.White.WithAlpha(a);
                c.DrawImage(_wallOld, screen, _img);
                a = A(_wallMix * WallCross());
            }
            _img.Color = SKColors.White.WithAlpha(a);
            c.DrawImage(_wall, screen, _img);
            _img.Color = SKColors.White;
        }

        /// <summary>The browser window at rest (cut by the slice's right edge, like part of a bigger screen).</summary>
        public SKRect RestWindow()
        {
            float w = _screen.Width, floor = Floor;
            float left = Math.Max(84, w * 0.26f), top = _screen.Top + 28;
            float h = Math.Clamp(floor - 22 - top, 60, 168);
            return new SKRect(left, top, w + 26, top + h);
        }

        private void DrawBrowser(SKCanvas c, SKRect screen)
        {
            if (screen.Width <= 0 || screen.Height <= 0) return;
            float g = (float)Grow, keep = 1 - g;
            var rest = RestWindow();
            var r = new SKRect(Lerp(rest.Left, screen.Left, g), Lerp(rest.Top, screen.Top, g),
                Lerp(rest.Right, screen.Right, g), Lerp(rest.Bottom, screen.Bottom, g));
            float radius = 7 * keep;

            if (keep > 0.01f)
            {
                _fill.ImageFilter = _shadow;
                _fill.Color = SKColors.Black.WithAlpha(A(keep));
                c.DrawRoundRect(r, radius, radius, _fill);
                _fill.ImageFilter = null;
            }
            c.Save();
            c.ClipRoundRect(new SKRoundRect(r, radius, radius), antialias: true);
            _fill.Color = PreviewDesktop.Css(0x1c1e2e);
            c.DrawRect(r, _fill);

            // Chrome: a title strip with one tab, then an address row with a pill of bars. It folds away as
            // the window goes full screen.
            float titleH = 22 * keep, addrH = 22 * keep;
            if (keep > 0.01f)
            {
                byte ca = A(keep);
                _fill.Color = PreviewDesktop.Css(0x2a2d44).WithAlpha(ca);
                c.DrawRect(r.Left, r.Top, r.Width, titleH, _fill);
                var tab = new SKRect(r.Left + 8, r.Top + 5 * keep, r.Left + 8 + 78, r.Top + titleH);
                _fill.Color = PreviewDesktop.Css(0x3a3e5c).WithAlpha(ca);
                using (var rr = new SKRoundRect())
                {
                    rr.SetRectRadii(tab, new[] { new SKPoint(5, 5), new SKPoint(5, 5), new SKPoint(0, 0), new SKPoint(0, 0) });
                    c.DrawRoundRect(rr, _fill);
                }
                _fill.Color = PreviewDesktop.Css(0xff69b4).WithAlpha(ca);
                c.DrawCircle(tab.Left + 9, tab.MidY + 1, 3 * keep, _fill);
                _fill.Color = PreviewDesktop.Css(0x8a8fb0).WithAlpha(ca);
                c.DrawRoundRect(new SKRect(tab.Left + 17, tab.MidY - 1.5f * keep, tab.Left + 58, tab.MidY + 2.5f * keep), 2, 2, _fill);
                _fill.Color = PreviewDesktop.Css(0x23263a).WithAlpha(ca);
                c.DrawRect(r.Left, r.Top + titleH, r.Width, addrH, _fill);
                var pill = new SKRect(r.Left + 30, r.Top + titleH + 4 * keep, Math.Min(r.Right - 14, r.Left + 30 + (rest.Width - 60)), r.Top + titleH + addrH - 4 * keep);
                _fill.Color = PreviewDesktop.Css(0x14162a).WithAlpha(ca);
                c.DrawRoundRect(pill, 7 * keep, 7 * keep, _fill);
                _fill.Color = PreviewDesktop.Css(0x4a4f72).WithAlpha(ca);
                c.DrawRoundRect(new SKRect(pill.Left + 9, pill.MidY - 2 * keep, pill.Left + 9 + pill.Width * 0.34f, pill.MidY + 2 * keep), 2, 2, _fill);
                c.DrawRoundRect(new SKRect(pill.Left + 13 + pill.Width * 0.36f, pill.MidY - 2 * keep, pill.Left + 13 + pill.Width * 0.52f, pill.MidY + 2 * keep), 2, 2, _fill);
                // back / forward marks
                c.DrawCircle(r.Left + 11, pill.MidY, 3 * keep, _fill);
                c.DrawCircle(r.Left + 21, pill.MidY, 3 * keep, _fill);
            }

            // The video box: grey at rest, the (fake) clip playing while the takeover runs it full screen.
            float inset = 9 * keep;
            var video = new SKRect(r.Left + inset, r.Top + titleH + addrH + inset, r.Right - inset - 26 * keep, r.Bottom - inset);
            if (video.Width > 4 && video.Height > 4)
            {
                float vr = 5 * keep;
                _fill.Color = PreviewDesktop.Css(0x3b3e52);
                c.DrawRoundRect(video, vr, vr, _fill);
                if (_clip != null && g > 0.001f)
                {
                    c.Save();
                    c.ClipRoundRect(new SKRoundRect(video, vr, vr), antialias: true);
                    float drift = _level() == MotionLevel.Full ? (float)Math.Sin(_t * 0.5) * video.Width * 0.03f : 0;
                    float cw = Math.Max(video.Width, video.Height * 1.36f) * 1.08f, ch = cw / 1.36f;
                    if (ch < video.Height * 1.08f) { ch = video.Height * 1.08f; cw = ch * 1.36f; }
                    _img.Color = SKColors.White.WithAlpha(A(Math.Min(1, g * 1.6)));
                    c.DrawImage(_clip, new SKRect(video.MidX - cw / 2 + drift, video.MidY - ch / 2, video.MidX + cw / 2 + drift, video.MidY + ch / 2), _img);
                    _img.Color = SKColors.White;
                    c.Restore();
                }
                // Play mark fades out as it plays; the progress strip fills while it plays.
                float play = 1 - Math.Min(1, g * 2);
                if (play > 0.01f)
                {
                    using var tri = new SKPath();
                    float s = Math.Min(16, video.Height * 0.16f);
                    tri.MoveTo(video.MidX - s * 0.45f, video.MidY - s * 0.6f);
                    tri.LineTo(video.MidX + s * 0.65f, video.MidY);
                    tri.LineTo(video.MidX - s * 0.45f, video.MidY + s * 0.6f);
                    tri.Close();
                    _fill.Color = new SKColor(255, 255, 255, (byte)(150 * play));
                    c.DrawPath(tri, _fill);
                }
                if (g > 0.001f && InTakeover)
                {
                    double u = (_t - _start) / Pace - _lead;
                    float f = (float)Math.Clamp(u / BodyLen, 0, 1);
                    float y = video.Bottom - 5;
                    _fill.Color = new SKColor(255, 255, 255, A(0.25 * g));
                    c.DrawRect(video.Left + 10, y, video.Width - 20, 2.5f, _fill);
                    _fill.Color = PreviewDesktop.Css(0xff69b4).WithAlpha(A(0.9 * g));
                    c.DrawRect(video.Left + 10, y, (video.Width - 20) * f, 2.5f, _fill);
                }
            }
            c.Restore();
        }

        private SKPoint MantraCentre()
        {
            float bottom = Grow > 0.5 ? _band.Bottom : Math.Min(_band.Bottom, Floor);
            float top = _band.Top;
            return new SKPoint(_screen.MidX + (Grow > 0.5 ? 0 : 20), (top + bottom) / 2 + 4);
        }

        private void DrawMantra(SKCanvas c)
        {
            if (Mantra <= 0.001 && Chant <= 0.001) return;
            var s = S;
            var at = MantraCentre();
            float size = Math.Min(_screen.Width * 0.15f, 40) * (0.85f + Math.Clamp(s.AutonomyIntensity, 1, 10) * 0.03f);
            _text.Typeface = Bold;
            _text.TextSize = size;
            float half = Math.Max(_text.MeasureText(Word) / 2, size);
            if (Chant > 0.001)
            {
                float vol = (float)Math.Clamp(s.MantraChantVolume, 0, 100) / 100f;
                float reach = _screen.Width * (0.10f + 0.22f * vol);
                float rad = half * 0.9f + (float)_ringPhase * reach;
                _stroke.StrokeWidth = 2.5f;
                _stroke.Color = PreviewDesktop.Css(0xffb3dc).WithAlpha(A(0.75 * Chant));
                c.DrawCircle(at, rad, _stroke);
                _stroke.StrokeWidth = 1.2f;
                _stroke.Color = SKColors.White.WithAlpha(A(0.35 * Chant));
                c.DrawCircle(at, rad * 0.82f, _stroke);
            }
            if (Mantra > 0.001)
            {
                float y = at.Y + size * 0.35f;
                _text.Color = new SKColor(0x20, 0x08, 0x24, A(0.55 * Mantra));
                c.DrawText(Word, at.X + 1.5f, y + 2, _text);
                _text.Color = SKColors.White.WithAlpha(A(Mantra));
                c.DrawText(Word, at.X, y, _text);
            }
        }

        /// <summary>The announce card: a small notice that slides in at the top right (a pink face, two bars).</summary>
        private void DrawNotice(SKCanvas c)
        {
            if (Notice <= 0.001) return;
            float cw = Math.Min(150, _screen.Width * 0.56f), ch = 44;
            float slide = (float)(1 - Notice) * 28;
            var r = new SKRect(_screen.Right - 10 - cw + slide, _band.Top + 10, _screen.Right - 10 + slide, _band.Top + 10 + ch);
            byte a = A(Notice);
            _fill.ImageFilter = _shadow;
            _fill.Color = SKColors.Black.WithAlpha(a);
            c.DrawRoundRect(r, 9, 9, _fill);
            _fill.ImageFilter = null;
            _fill.Color = PreviewDesktop.Css(0x2a1f3d).WithAlpha(a);
            c.DrawRoundRect(r, 9, 9, _fill);
            _stroke.StrokeWidth = 1.2f;
            _stroke.Color = PreviewDesktop.Css(0xff69b4).WithAlpha(A(0.8 * Notice));
            c.DrawRoundRect(r, 9, 9, _stroke);
            _fill.Shader = SKShader.CreateLinearGradient(new SKPoint(r.Left + 10, r.Top + 8), new SKPoint(r.Left + 38, r.Bottom - 8),
                new[] { PreviewDesktop.Css(0xff69b4).WithAlpha(a), PreviewDesktop.Css(0x8f4bff).WithAlpha(a) }, null, SKShaderTileMode.Clamp);
            c.DrawCircle(r.Left + 24, r.MidY, 13, _fill);
            _fill.Shader?.Dispose();
            _fill.Shader = null;
            _fill.Color = PreviewDesktop.Css(0xe8e4f4).WithAlpha(a);
            c.DrawRoundRect(new SKRect(r.Left + 45, r.MidY - 8, r.Right - 14, r.MidY - 3), 2.5f, 2.5f, _fill);
            _fill.Color = PreviewDesktop.Css(0x9c94b8).WithAlpha(a);
            c.DrawRoundRect(new SKRect(r.Left + 45, r.MidY + 3, r.Left + 45 + (r.Width - 59) * 0.62f, r.MidY + 7), 2, 2, _fill);
        }

        private void DrawCountdown(SKCanvas c)
        {
            if (Countdown <= 0.001) return;
            float y = Floor - 9, left = _screen.Left + 12, right = _screen.Right - 12;
            byte a = A(Countdown);
            _fill.Color = new SKColor(10, 8, 24, (byte)(170 * Countdown));
            c.DrawRoundRect(new SKRect(left - 2, y - 2, right + 2, y + 6), 4, 4, _fill);
            _fill.Color = new SKColor(255, 255, 255, (byte)(45 * Countdown));
            c.DrawRoundRect(new SKRect(left, y, right, y + 4), 2, 2, _fill);
            _fill.Color = PreviewDesktop.Css(0xff69b4).WithAlpha(a);
            c.DrawRoundRect(new SKRect(left, y, left + (right - left) * (float)CountdownLeft, y + 4), 2, 2, _fill);
        }

        // ---------------------------------------------------------------- helpers

        private static double Toward(double v, double target, double step)
            => v < target ? Math.Min(target, v + step) : Math.Max(target, v - step);

        private static double Smooth(double x)
        {
            x = Math.Clamp(x, 0, 1);
            return x * x * (3 - 2 * x);
        }

        private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        private static byte A(double v) => (byte)Math.Round(255 * Math.Clamp(v, 0, 1));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _wall?.Dispose(); _wall = null;
            _wallOld?.Dispose(); _wallOld = null;
            _clip?.Dispose(); _clip = null;
            _fill.Dispose(); _img.Dispose(); _stroke.Dispose(); _text.Dispose(); _shadow.Dispose();
        }
    }
}
