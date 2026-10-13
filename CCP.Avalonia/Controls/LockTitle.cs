using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Immutable;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.VisualTree;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Models;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/LockTitle.cs: the lock's name as a title card,
    /// Fredoka bold in the candy gradient with an ice sliver along the top, every o and O a padlock
    /// leaning opposite ways from its neighbour, shrunk to <see cref="FitWidth"/>, never wrapped.
    ///
    /// <para>Same juice and timing as WPF (constants copied), gated the same way: <see cref="PlayEntry"/>
    /// and <see cref="Jolt"/>/<see cref="Rattle"/> at MotionLevel Full, <see cref="Start"/> under
    /// AllowAmbientLoops. WPF ran one Storyboard per transform; here every value is a pure function of
    /// time (<see cref="Wobble"/>, <see cref="Swing"/>, <see cref="Squash"/>, ...) applied on ONE
    /// <see cref="FrameClock"/> (PLAYBOOK P82: no infinite Animation, ambient loops on FrameClock), at
    /// 20 fps idle like WPF's DesiredFrameRate. The clock only runs while attached and effectively
    /// visible (P01/P65), so a hidden or closed tab costs nothing.</para>
    ///
    /// <para>Deviation: no shadow. WPF hung a DropShadowEffect (drifting 2-5 px) on the moving word
    /// when AllowGlow; an Effect re-blurs every frame the word moves (P82, ratcheted by
    /// AmbientBeatLoopsTests.NoNewEffectInCodeBehind) and a BoxShadow cannot follow glyph shapes.</para>
    /// </summary>
    public sealed class LockTitle : Grid
    {
        public const string PadlockArt = "avares://CCP.Avalonia/Resources/features/chaster_padlock_o.png";
        private const double PadlockAspect = 152.0 / 213.0;
        private const double PadlockHeightEm = 0.72;
        private const double TiltDegrees = 10;
        internal const double BreathTo = 1.02;
        internal const double BreathSeconds = 2.6;
        internal const double WobbleDegrees = 1.2;
        internal const double WobbleSeconds = 2.8;
        internal const double SwayPx = 1.5;
        internal const double SwaySeconds = 3.3;
        internal const int DropMs = 340;
        internal const int DropStaggerMs = 40;
        internal const double DropFromPx = -70;
        internal const int SquashMs = 260;
        internal const int SwingMs = 1200;
        internal const double SwingDegrees = 14;
        internal const int JoltMs = 600;
        internal const double JoltDegrees = 10;
        internal const int RattleMs = 250;
        internal const double RattleDegrees = 4;
        private const int ShowMs = 70;
        private static readonly TimeSpan IdleInterval = TimeSpan.FromMilliseconds(50); // WPF DesiredFrameRate 20

        public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<LockTitle, string>(nameof(Text), "");
        public static readonly StyledProperty<double> FontSizeProperty = AvaloniaProperty.Register<LockTitle, double>(nameof(FontSize), 88.0);
        public static readonly StyledProperty<double> FitWidthProperty = AvaloniaProperty.Register<LockTitle, double>(nameof(FitWidth), double.NaN);

        private static readonly FontFamily Display = new("Fredoka, Segoe UI");
        private static readonly Typeface Face = new(Display, FontStyle.Normal, FontWeight.Bold);
        internal static readonly IBrush Candy = MakeCandy();
        private static readonly IBrush Ice = new ImmutableSolidColorBrush(Color.FromRgb(0xEA, 0xF2, 0xFF));
        private static readonly IBrush IceMask = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Colors.White, 0.0), new GradientStop(Colors.White, 0.14), new GradientStop(Colors.Transparent, 0.17) },
        }.ToImmutable();
        private static Bitmap? _padlock;
        private static readonly Stopwatch Clock = Stopwatch.StartNew();

        /// <summary>One glyph: its element, the centres its transforms turn about, and its swing in flight.</summary>
        private sealed class Glyph
        {
            public required Control Element;
            public required bool Padlock;
            public required double W, H, Lean;
            public TimeSpan? EntryAt;                       // when this glyph starts to drop
            public TimeSpan SwingAt; public double SwingDeg; public int SwingMs, SwingBeats; // SwingMs 0 = none
        }

        private readonly StackPanel _row = Row();
        private readonly List<Glyph> _glyphs = new();
        private readonly FrameClock _clock;
        private IDisposable? _visibilityWatch;
        private bool _running;
        private TimeSpan _idleFrom;
        private TimeSpan _entryUntil = TimeSpan.MinValue;
        private double _effectiveFontSize;

        public LockTitle()
        {
            Children.Add(_row);
            Background = Brushes.Transparent; // hit-testable for the hover rattle, paints nothing
            PointerEntered += (_, _) => Rattle();
            _clock = new FrameClock(this);
            _clock.Tick += (_, _) => Tick();
            AttachedToVisualTree += (_, _) => { _visibilityWatch?.Dispose(); _visibilityWatch = EffectiveVisibility.Watch(this, Arm); Arm(); };
            DetachedFromVisualTree += (_, _) => { _visibilityWatch?.Dispose(); _visibilityWatch = null; _clock.Stop(); };
            Rebuild(entry: false);
        }

        public string Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public double FontSize { get => GetValue(FontSizeProperty); set => SetValue(FontSizeProperty, value); }
        /// <summary>The width the word must fit in; NaN means no limit.</summary>
        public double FitWidth { get => GetValue(FitWidthProperty); set => SetValue(FitWidthProperty, value); }

        public double EffectiveFontSize => _effectiveFontSize;
        public int PadlockCount => _glyphs.Count(g => g.Padlock);
        public IEnumerable<Control> Glyphs => _row.Children;
        public IEnumerable<Control> Padlocks => _glyphs.Where(g => g.Padlock).Select(g => g.Element);

        /// <summary>Test seams: the time source, and an override for both motion gates.</summary>
        internal Func<TimeSpan> Now { get; set; } = () => Clock.Elapsed;
        internal bool? MotionOverride { get; set; }
        internal bool IsTicking => _clock.IsEnabled;
        internal bool IsRunning => _running;

        private bool Full => MotionOverride ?? AmbientFxCanvas.Env.Level == MotionLevel.Full;
        private bool AmbientAllowed => MotionOverride ?? AmbientFxCanvas.Env.AllowAmbientLoops;

        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);
            if (change.Property == TextProperty) Rebuild(entry: true);
            else if (change.Property == FontSizeProperty || change.Property == FitWidthProperty) Rebuild(entry: false);
        }

        /// <summary>The runs of a title: letters between padlocks, and the padlocks.</summary>
        public static IReadOnlyList<(string Run, bool Padlock)> Split(string? text)
        {
            var parts = new List<(string, bool)>();
            if (string.IsNullOrEmpty(text)) return parts;
            var run = "";
            foreach (var ch in text)
            {
                if (ch == 'o' || ch == 'O')
                {
                    if (run.Length > 0) { parts.Add((run, false)); run = ""; }
                    parts.Add((ch.ToString(), true));
                }
                else run += ch;
            }
            if (run.Length > 0) parts.Add((run, false));
            return parts;
        }

        public static double NaturalWidth(string? text, double size)
        {
            double width = 0;
            foreach (var (run, padlock) in Split(text))
                width += padlock ? size * PadlockHeightEm * PadlockAspect - size * 0.04 : Measure(run, size).WidthIncludingTrailingWhitespace;
            return width;
        }

        private static FormattedText Measure(string text, double size) =>
            new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, Face, size, Brushes.White);

        private static StackPanel Row() => new()
        {
            Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };

        private void Rebuild(bool entry)
        {
            var wasRunning = _running;
            try
            {
                Stop();
                _row.Children.Clear();
                _glyphs.Clear();
                // _entryUntil survives a rebuild, as WPF's did: a resize mid-drop does not re-arm the entry or the rattle
                var text = Text ?? "";
                var size = Math.Max(8, FontSize);
                var fit = FitWidth;
                if (!double.IsNaN(fit) && fit > 0 && text.Length > 0)
                {
                    var natural = NaturalWidth(text, size);
                    if (natural > fit) size = Math.Max(8, size * fit / natural);
                }
                _effectiveFontSize = size;
                if (text.Length == 0) return;

                var probe = Measure("Hg", size);
                var descent = Math.Max(0, probe.Height - probe.Baseline);
                var padlockHeight = size * PadlockHeightEm;
                var padlockWidth = padlockHeight * PadlockAspect;
                var tilt = 0;
                foreach (var (run, padlock) in Split(text))
                {
                    if (!padlock)
                    {
                        var measured = Measure(run, size);
                        _row.Children.Add(Run(run, size));
                        _glyphs.Add(new Glyph { Element = _row.Children[^1], Padlock = false, W = measured.WidthIncludingTrailingWhitespace, H = measured.Height, Lean = 0 });
                        continue;
                    }
                    // the glyph's own light bleeds a little past its box: overlap the neighbours by that
                    var margin = new Thickness(-size * 0.02, 0, -size * 0.02, descent - padlockHeight * 0.06);
                    var lean = (tilt++ & 1) == 0 ? -TiltDegrees : TiltDegrees;
                    var image = new Image { Source = Padlock(), Width = padlockWidth, Height = padlockHeight, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Bottom, Margin = margin };
                    RenderOptions.SetBitmapInterpolationMode(image, BitmapInterpolationMode.HighQuality);
                    _row.Children.Add(image);
                    _glyphs.Add(new Glyph { Element = image, Padlock = true, W = padlockWidth, H = padlockHeight, Lean = lean });
                }
                foreach (var g in _glyphs) g.Element.RenderTransformOrigin = RelativePoint.TopLeft;
                Apply(Now());
            }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[LockTitle] rebuild"); }
            if (wasRunning) Start();
            if (entry && IsEffectivelyVisible && this.IsAttachedToVisualTree()) PlayEntry();
        }

        /// <summary>A run of letters: the candy fill with the ice sliver over it.</summary>
        private static Control Run(string run, double size)
        {
            TextBlock Letters(IBrush fill) => new()
            {
                Text = run, FontFamily = Display, FontSize = size, FontWeight = FontWeight.Bold, Foreground = fill, VerticalAlignment = VerticalAlignment.Bottom,
            };
            var ice = Letters(Ice);
            ice.Opacity = 0.55;
            ice.OpacityMask = IceMask;
            ice.IsHitTestVisible = false;
            return new Grid { Children = { Letters(Candy), ice }, VerticalAlignment = VerticalAlignment.Bottom };
        }

        private static Bitmap? Padlock()
        {
            if (_padlock != null) return _padlock;
            try { _padlock = new Bitmap(AssetLoader.Open(new Uri(PadlockArt))); }
            catch (Exception ex) { Serilog.Log.Debug(ex, "[LockTitle] padlock art"); }
            return _padlock;
        }

        // ------------------------------------------------------------------ the beats

        /// <summary>Letters first, then the padlocks, each dropping in 40 ms apart with the house thud and
        /// a squash on landing; a padlock then swings from its shackle. Ignored while one is in flight.</summary>
        public void PlayEntry()
        {
            if (!Full || _glyphs.Count == 0) return;
            var now = Now();
            if (now < _entryUntil) return;
            var order = _glyphs.Where(g => !g.Padlock).Concat(_glyphs.Where(g => g.Padlock)).ToList();
            _entryUntil = now + TimeSpan.FromMilliseconds(DropStaggerMs * (order.Count - 1) + DropMs + SwingMs);
            for (var i = 0; i < order.Count; i++)
            {
                var g = order[i];
                g.EntryAt = now + TimeSpan.FromMilliseconds(DropStaggerMs * i);
                if (g.Padlock) SetSwing(g, g.EntryAt.Value + Land, SwingDegrees, SwingMs, 4);
            }
            Arm();
        }

        /// <summary>A price landed: the padlocks take a tug.</summary>
        public void Jolt()
        {
            if (!Full) return;
            var now = Now();
            foreach (var g in _glyphs.Where(g => g.Padlock)) SetSwing(g, now, JoltDegrees, JoltMs, 3);
            Arm();
        }

        /// <summary>The pointer crossed the word: the padlocks rattle once, 30 ms apart.</summary>
        public void Rattle()
        {
            var now = Now();
            if (!Full || now < _entryUntil) return;
            var i = 0;
            foreach (var g in _glyphs.Where(g => g.Padlock)) SetSwing(g, now + TimeSpan.FromMilliseconds(30 * i++), RattleDegrees, RattleMs, 3);
            Arm();
        }

        private static void SetSwing(Glyph g, TimeSpan at, double deg, int ms, int beats) { g.SwingAt = at; g.SwingDeg = deg; g.SwingMs = ms; g.SwingBeats = beats; }

        /// <summary>The idle: wobble, sway, padlocks breathing. Parked by <see cref="Stop"/>.</summary>
        public void Start()
        {
            Stop();
            if (!AmbientAllowed || _glyphs.Count == 0) return;
            _running = true;
            _idleFrom = Now();
            Arm();
        }

        public void Stop()
        {
            if (!_running) return;
            _running = false;
            Apply(Now());
            Arm();
        }

        // ------------------------------------------------------------------ the clock

        private static readonly TimeSpan Land = TimeSpan.FromMilliseconds(DropMs * 0.55);

        private bool Transient(TimeSpan now) =>
            now < _entryUntil || _glyphs.Any(g => g.SwingMs > 0 && now < g.SwingAt + TimeSpan.FromMilliseconds(g.SwingMs));

        /// <summary>Runs the clock only while something moves AND the word is on screen.</summary>
        private void Arm()
        {
            var now = Now();
            var moving = _running || Transient(now);
            if (moving && this.IsAttachedToVisualTree() && IsEffectivelyVisible)
            {
                _clock.Interval = Transient(now) ? TimeSpan.Zero : IdleInterval;
                _clock.Start();
            }
            else _clock.Stop();
        }

        private void Tick()
        {
            Apply(Now());
            Arm();
        }

        /// <summary>Every glyph's transform and opacity at <paramref name="now"/>. Internal for the tests.</summary>
        internal void Apply(TimeSpan now)
        {
            for (var i = 0; i < _glyphs.Count; i++)
            {
                var g = _glyphs[i];
                double sx = 1, sy = 1, drop = 0, opacity = 1;
                var squashEnd = TimeSpan.MinValue;
                if (g.EntryAt is { } at)
                {
                    var ms = (now - at).TotalMilliseconds;
                    opacity = ms < 0 ? 0 : Math.Min(1, ms / ShowMs);
                    drop = Drop(ms);
                    var squashMs = (now - at - Land).TotalMilliseconds;
                    sy = Squash(squashMs, 0.82, 1.05);
                    sx = Squash(squashMs, 1.12, 0.97);
                    squashEnd = at + Land + TimeSpan.FromMilliseconds(SquashMs);
                }
                if (g.Padlock && _running && now >= squashEnd)
                    sx = sy = Breath((now - (squashEnd > _idleFrom ? squashEnd : _idleFrom)).TotalMilliseconds, i);
                var wobble = _running ? Wobble((now - _idleFrom).TotalMilliseconds, i) : 0;
                var sway = _running ? Sway((now - _idleFrom).TotalMilliseconds, i) : 0;
                var swing = g.SwingMs > 0 ? Swing((now - g.SwingAt).TotalMilliseconds, g.SwingDeg, g.SwingMs, g.SwingBeats) : 0;

                // WPF TransformGroup order: scale, (lean), wobble, (swing), drop, sway
                double cx = g.W / 2, midY = g.H / 2;
                var m = About(Matrix.CreateScale(sx, sy), cx, g.Padlock ? midY : g.H)
                        * About(Matrix.CreateRotation(Rad(g.Lean + wobble)), cx, midY)
                        * About(Matrix.CreateRotation(Rad(swing)), cx, g.H * 0.06)
                        * Matrix.CreateTranslation(0, drop + sway);
                if (g.Element.RenderTransform is MatrixTransform mt) mt.Matrix = m;
                else g.Element.RenderTransform = new MatrixTransform(m);
                g.Element.Opacity = opacity;
            }
        }

        private static Matrix About(Matrix m, double x, double y) => Matrix.CreateTranslation(-x, -y) * m * Matrix.CreateTranslation(x, y);
        private static double Rad(double deg) => deg * Math.PI / 180;

        // ------------------------------------------------------------------ the curves (WPF timelines, as functions)

        private static double SineInOut(double t) => (1 - Math.Cos(Math.PI * t)) / 2;
        private static double QuadOut(double t) => 1 - (1 - t) * (1 - t);
        private static double QuadInOut(double t) => t < 0.5 ? 2 * t * t : 1 - 2 * (1 - t) * (1 - t);

        /// <summary>A from/to AutoReverse Forever DoubleAnimation with a SineEase InOut and a BeginTime:
        /// the base value until it begins, then from -> to -> from every 2 x seconds.</summary>
        internal static double PingPong(double ms, double beginMs, double seconds, double from, double to, double rest)
        {
            if (ms < beginMs) return rest;
            var d = seconds * 1000;
            var u = (ms - beginMs) % (2 * d);
            var p = u < d ? u / d : 2 - u / d;
            return from + (to - from) * SineInOut(p);
        }

        internal static double Wobble(double ms, int i) => PingPong(ms, 370 * i, WobbleSeconds, -WobbleDegrees, WobbleDegrees, 0);
        internal static double Sway(double ms, int i) => PingPong(ms, 530 * i, SwaySeconds, SwayPx, -SwayPx, 0);
        internal static double Breath(double ms, int phase) => PingPong(ms, 420 * phase, BreathSeconds, 1.0, BreathTo, 1.0);

        /// <summary>The thud: -70 px to 0 over 340 ms on a BackEase out, amplitude 0.55.</summary>
        internal static double Drop(double ms)
        {
            if (ms < 0) return DropFromPx;
            if (ms >= DropMs) return 0;
            var t = 1 - ms / DropMs; // WPF EaseOut = 1 - EaseIn(1 - t), EaseIn = t^3 - t*a*sin(pi t)
            var ease = 1 - (t * t * t - t * 0.55 * Math.Sin(Math.PI * t));
            return DropFromPx * (1 - ease);
        }

        /// <summary>The landing squash: 1 -> hit (30%, quad out) -> rebound (70%) -> 1 over 260 ms.</summary>
        internal static double Squash(double ms, double hit, double rebound)
        {
            if (ms <= 0 || ms >= SquashMs) return 1;
            var p = ms / SquashMs;
            if (p < 0.3) return 1 + (hit - 1) * QuadOut(p / 0.3);
            if (p < 0.7) return hit + (rebound - hit) * QuadInOut((p - 0.3) / 0.4);
            return rebound + (1 - rebound) * QuadInOut((p - 0.7) / 0.3);
        }

        /// <summary>A decaying swing about the shackle: beat b at (b - 0.5)/beats of the way through,
        /// amplitude degrees x 0.55^(b-1), alternating sides, sine-eased between, 0 at both ends.</summary>
        internal static double Swing(double ms, double degrees, int duration, int beats)
        {
            if (ms <= 0 || ms >= duration) return 0;
            var p = ms / duration;
            double prevAt = 0, prev = 0, sign = 1;
            for (var b = 1; b <= beats + 1; b++)
            {
                double at = b <= beats ? (b - 0.5) / beats : 1;
                double value = b <= beats ? sign * degrees * Math.Pow(0.55, b - 1) : 0;
                if (p < at) return prev + (value - prev) * SineInOut((p - prevAt) / (at - prevAt));
                prevAt = at; prev = value; sign = -sign;
            }
            return 0;
        }

        private static IBrush MakeCandy() => new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
            GradientStops =
            {
                new GradientStop(Color.FromRgb(0xEF, 0xE0, 0xF3), 0.00),
                new GradientStop(Color.FromRgb(0xEE, 0xBC, 0xD9), 0.10),
                new GradientStop(Color.FromRgb(0xF7, 0x8F, 0xB4), 0.22),
                new GradientStop(Color.FromRgb(0xFF, 0x68, 0x91), 0.38),
                new GradientStop(Color.FromRgb(0xFC, 0x64, 0x93), 0.50),
                new GradientStop(Color.FromRgb(0xE3, 0x86, 0xCC), 0.62),
                new GradientStop(Color.FromRgb(0xD3, 0x9F, 0xF4), 0.78),
                new GradientStop(Color.FromRgb(0xCE, 0xA8, 0xFF), 0.92),
                new GradientStop(Color.FromRgb(0xC3, 0xA0, 0xF0), 1.00),
            },
        }.ToImmutable();
    }
}
