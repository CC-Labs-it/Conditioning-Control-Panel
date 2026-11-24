using System;
using ConditioningControlPanel.Looks;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Compositor;
using ConditioningControlPanel.Services.Looks;
using ConditioningControlPanel.Services.Safety;
using SkiaSharp;

namespace ConditioningControlPanel.Controls.Preview.Scenes
{
    /// <summary>
    /// Subliminals in the live preview (sweep lane P2, owner 2026-11-24). The card draws through the
    /// app's REAL <see cref="SubliminalLayer"/> on a private, disposed engine (as FirstShowEffects does),
    /// with the page's colours, font, opacity and the worn word look, so it looks exactly like the real
    /// card, over the visible band of the slice. Only the timer is the preview's: a compressed pace from
    /// the frequency (1 a minute = a gap of 4 s, 30 = 1.1 s) and the hold from Duration (frames x 17 ms,
    /// doubled so a 100 ms card can be seen at all). Neutral words only, never the user's list or a mod's.
    /// Gentle visuals: no card behind the word and the gentle fades, as the real service does.
    /// Options it does not show (Solid mode, Steal focus, the word list, Afterglow) do nothing here.
    /// </summary>
    public sealed class SubliminalPreviewScene : IPreviewScene
    {
        public static readonly string[] Watched =
        {
            nameof(AppSettings.SubliminalFrequency), nameof(AppSettings.SubliminalDuration), nameof(AppSettings.SubliminalOpacity),
            nameof(AppSettings.SubBackgroundColor), nameof(AppSettings.SubBackgroundTransparent), nameof(AppSettings.SubTextColor),
            nameof(AppSettings.SubBorderColor), nameof(AppSettings.SubliminalFont),
        };

        public static readonly string[] Words = { "DEEPER", "RELAX", "DRIFT", "FOCUS" };
        /// <summary>The real card's 50 ms fades (SubliminalService.DefaultFadeMs).</summary>
        public const int FadeMs = 50;
        /// <summary>The preview holds a card this many times the real hold.</summary>
        public const double HoldStretch = 2.0;

        private readonly CompositorEngine _engine = new();
        private readonly SubliminalLayer _layer;
        private readonly Func<AppSettings?> _settings;
        private readonly Func<MotionLevel> _level;
        private readonly Func<bool> _gentle;
        private readonly Func<(WordStyle Style, WordMotion Motion)> _look;
        private readonly Random _rng;
        private static AppSettings? _defaults;

        private SKRect _screen, _band;
        private double _t, _next = 0.4, _shownAt = -9, _shownFor;
        private int _word = -1;
        private bool _disposed;

        /// <summary>The host builds a scene by its id with no arguments (EffectPreview.Create).</summary>
        public SubliminalPreviewScene() : this(settings: null) { }

        public SubliminalPreviewScene(Func<AppSettings?>? settings = null, Func<MotionLevel>? level = null,
            Func<bool>? gentle = null, Func<(WordStyle, WordMotion)>? look = null, Random? rng = null)
        {
            _settings = settings ?? (() => App.Settings?.Current);
            _level = level ?? (() => MotionFx.Level);
            _gentle = gentle ?? (() => GentleVisuals.On);
            _look = look ?? (() => (WornWordStyle.Current, WornWordStyle.Motion));
            _rng = rng ?? new Random();
            _engine.Dispose();
            _layer = new SubliminalLayer(_engine);
        }

        public string[] Settings => Watched;

        // ---- read-outs for tests ----
        public int Shown { get; private set; }
        public string? LastWord => _word < 0 ? null : Words[_word];
        public double Clock => _t;
        public SKRect SpawnBand => _band;
        /// <summary>True while a card is on screen (inside its fades and hold).</summary>
        public bool CardUp => _t - _shownAt < _shownFor;
        /// <summary>The card's last hold, fades included, in seconds.</summary>
        public double LastCardSeconds => _shownFor;

        private AppSettings S => _settings() ?? (_defaults ??= new AppSettings());
        private float FieldBottom => Math.Max(_screen.Top + 1, _screen.Bottom - PreviewDesktop.TaskbarHeight);

        public void Layout(SKRect screen, SKRect visible)
        {
            _screen = screen;
            float top = Math.Clamp(visible.Top, screen.Top, FieldBottom), bottom = Math.Clamp(visible.Bottom, screen.Top, FieldBottom);
            if (bottom - top < 160) { top = screen.Top; bottom = Math.Min(FieldBottom, screen.Top + 560); }
            _band = new SKRect(screen.Left, top, screen.Right, bottom);
        }

        public void Start() => _next = Math.Min(_next, _t + 0.2);

        public void Tick(double dt)
        {
            if (_disposed || dt <= 0) return;
            _t += dt;
            if (_t >= _next) { Show(S); _next = _t + Gap(S); }
            _layer.Update(TimeSpan.FromSeconds(dt));
        }

        public void Render(SKCanvas canvas, SKRect screen, SKRect visible)
        {
            if (_disposed) return;
            _layer.Render(canvas, new SKRectI((int)Math.Floor(visible.Left), (int)Math.Floor(visible.Top),
                (int)Math.Ceiling(visible.Right), (int)Math.Ceiling(visible.Bottom)), 1.0, TimeSpan.FromSeconds(_t));
        }

        public void SettingChanged(string name)
        {
            if (_disposed) return;
            // A changed look shows at once on a fresh card.
            _next = Math.Min(_next, _t + 0.15);
        }

        /// <summary>A click is a nudge: the next card comes now (the real card takes no clicks).</summary>
        public void Click(SKPoint at)
        {
            if (_disposed) return;
            _next = Math.Min(_next, _t + 0.05);
        }

        // ---------------------------------------------------------------- driver

        private static double Gap(AppSettings s) => 4.0 - 2.9 * (Math.Clamp(s.SubliminalFrequency, 1, 30) - 1) / 29.0;

        private void Show(AppSettings s)
        {
            if (_screen.Width <= 0) return;
            _word = (_word + 1 + _rng.Next(Words.Length - 1)) % Words.Length;
            bool gentle = _gentle();
            int hold = (int)(Math.Max(100, Math.Clamp(s.SubliminalDuration, 1, 10) * 17) * HoldStretch);
            int fadeIn = FadeMs, fadeOut = FadeMs;
            if (_level() == MotionLevel.Off) { fadeIn = fadeOut = 1; hold = (int)(PreviewDriver.SettleSeconds * 1000) + 600; }
            else if (gentle || _level() == MotionLevel.Reduced)
            {
                var g = GentleVisuals.On ? GentleVisuals.FadeSeconds : GentleVisuals.DefaultFadeSeconds;
                fadeIn = Math.Max(GentleVisuals.RiseFloorMs, (int)(GentleVisuals.Fade(FadeMs / 1000.0, true, g) * 1000));
                fadeOut = (int)(GentleVisuals.Fade(FadeMs / 1000.0, true, g) * 1000);
            }
            var bg = Colour(s.SubBackgroundColor, SKColors.Black);
            var text = Colour(s.SubTextColor, new SKColor(255, 0, 255));
            var border = Colour(s.SubBorderColor, SKColors.White);
            bool bgTransparent = s.SubBackgroundTransparent || gentle;
            // The real card's 120 DIP word is about a quarter of a 1920 screen: here a bit more, to be read.
            float scale = Math.Max(0.12f, _screen.Width / 1100f);
            var (style, motion) = _look();
            var bounds = new SKRectI((int)_band.Left, (int)_band.Top, (int)Math.Ceiling(_band.Right), (int)Math.Ceiling(_band.Bottom));
            _layer.Flash(new[] { new SubliminalLayer.Placement(bounds, scale) }, Words[_word], bg, text, border,
                bgTransparent, Math.Clamp(s.SubliminalOpacity / 100.0, 0, 1), hold, fadeIn, fadeOut,
                SubliminalLayer.ResolveFace(s.SubliminalFont), style, motion);
            _shownAt = _t;
            _shownFor = (hold + fadeIn + fadeOut) / 1000.0;
            Shown++;
        }

        private static SKColor Colour(string? hex, SKColor fallback)
            => !string.IsNullOrWhiteSpace(hex) && SKColor.TryParse(hex, out var c) ? c.WithAlpha(255) : fallback;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _layer.Clear();
        }
    }
}
