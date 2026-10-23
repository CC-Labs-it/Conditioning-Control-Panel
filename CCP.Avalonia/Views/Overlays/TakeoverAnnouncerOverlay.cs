// PORTED from ConditioningControlPanel/Overlays/TakeoverAnnouncerOverlay.cs (517 lines). The queue and
// timings (150/1700/250 ms, bloom 0.15/0.70 s, shake 0.26 s x 3.5 DIP) are Core's TakeoverCueQueue; this
// is the window. Mapping: SKElement.PaintSurface -> ICustomDrawOperation on the leased SKCanvas (the
// TakeoverOrb pattern), WS_EX_TRANSPARENT/TOOLWINDOW/NOACTIVATE -> X11Overlay.SetClickThrough +
// SetOverrideRedirect (the BouncingTextOverlay order), HWND_TOPMOST re-assert -> X11Overlay.Raise,
// layered-window alpha -> X11Overlay.SetOpacity. Like WPF, ONE window is kept alive and hidden between cues.
// Placed over the primary screen (WPF: centred over the primary work area of a virtual-screen window).
using System;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>WPF TakeoverAnnouncerOverlay's static half: Announce / CloseActive / ShowNext.</summary>
    internal static class TakeoverAnnouncerOverlay
    {
        internal static readonly TakeoverCueQueue Queue = new();
        /// <summary>Real elapsed time (P40). Tests step it.</summary>
        internal static Func<TimeSpan> Clock = () => Stopwatch.GetElapsedTime(0);
        /// <summary>False in headless tests only: a plain window, no X11 click-through calls.</summary>
        internal static bool UsePlatform = true;

        private static TakeoverAnnouncerWindow? _window;
        private static int _shownSerial, _chain;
        private static bool _frameRequested;

        internal static TakeoverAnnouncerWindow? Window => _window;

        /// <summary>WPF Announce: queue "TAKEOVER" + <paramref name="effectLabel"/>. Any thread; blank is a no-op.</summary>
        public static void Announce(string? effectLabel)
        {
            if (string.IsNullOrWhiteSpace(effectLabel)) return;
            if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => Announce(effectLabel)); return; }
            try
            {
                if (EnsureWindow() == null) { Queue.Clear(); return; }
                if (Queue.Enqueue(effectLabel, Clock())) Frame();
            }
            catch (Exception ex) { Log.Debug("TakeoverAnnouncer.Announce: {E}", ex.Message); }
        }

        /// <summary>WPF CloseActive: drop the visible and queued cues and tear the window down. Panic and exit.</summary>
        public static void CloseActive()
        {
            Queue.Clear();
            _chain++;
            _frameRequested = false;
            var w = _window;
            _window = null;
            try { w?.Close(); } catch { }
        }

        /// <summary>One frame (WPF CompositionTarget.Rendering -> TopLevel.RequestAnimationFrame): the next
        /// queued cue starts when the last fade-out ends; idle hides the window and stops asking for frames.</summary>
        internal static void Frame()
        {
            var w = _window;
            if (w == null || Queue.Tick(Clock()) is not { } age)
            {
                _chain++;
                _frameRequested = false;
                if (w != null) { w.ClearCue(); if (w.IsVisible) w.Hide(); }
                return;
            }
            if (Queue.Serial != _shownSerial)
            {
                _shownSerial = Queue.Serial;
                w.Display(Queue.Current!);
                if (!w.IsVisible) w.Show();
            }
            w.Advance(age, Clock());
            if (_frameRequested) return;
            _frameRequested = true;
            var chain = _chain;
            w.RequestAnimationFrame(_ => { if (chain != _chain) return; _frameRequested = false; if (ReferenceEquals(w, _window)) Frame(); });
        }

        private static TakeoverAnnouncerWindow? EnsureWindow()
        {
            if (_window != null) return _window;
            var platform = UsePlatform && X11Overlay.IsAvailable;
            if (!platform && UsePlatform) { Log.Warning("Takeover cue: this platform cannot show click-through overlays; skipped"); return null; }
            var w = new TakeoverAnnouncerWindow(live: true);
            var screen = w.Screens.Primary ?? w.Screens.All.FirstOrDefault();
            var bounds = screen?.Bounds ?? new PixelRect(0, 0, 1280, 720);
            var work = screen?.WorkingArea ?? bounds;
            var k = screen is { Scaling: > 0 } ? screen.Scaling : 1.0;
            if (platform && (!X11Overlay.SetClickThrough(w, true) || !X11Overlay.SetOpacity(w, 0) || !X11Overlay.SetOverrideRedirect(w, bounds)))
            {
                Log.Warning("Takeover cue: the platform refused a click-through topmost overlay window; skipped");
                w.Close();
                return null;
            }
            if (!platform) { w.Width = bounds.Width / k; w.Height = bounds.Height / k; }
            w.Place((work.X - bounds.X) / k, (work.Y - bounds.Y) / k, work.Width / k);
            return _window = w;
        }
    }

    /// <summary>The cue window: violet "TAKEOVER" eyebrow over the white effect word, an additive Skia
    /// bloom + sparkle burst behind it and a decaying impact shake (WPF DisplayCore/OnRendering/OnPaintSurface).</summary>
    internal sealed class TakeoverAnnouncerWindow : Window
    {
        private const double EyebrowFont = 24, EffectFont = 60, TopOffsetDip = 92;
        private static readonly IBrush EyebrowFill = new SolidColorBrush(Color.FromRgb(0xC9, 0xA0, 0xFF)).ToImmutable();
        private static readonly IBrush EffectFill = Brushes.White;
        private static readonly IBrush StrokeBrush = new SolidColorBrush(Color.FromRgb(0x0B, 0x08, 0x12)).ToImmutable();
        private static readonly FontFamily TakeoverFont = new("Segoe UI Rounded, Segoe UI");

        internal struct Spark { public float X, Y, VX, VY, Life, Max, Size; public bool White; }
        private const int MaxSparks = 48;
        private readonly Spark[] _sparks = new Spark[MaxSparks];
        private int _sparkN;
        private readonly Random _rng = new();
        private float _bloom;                 // current bloom envelope 0..1
        private float _centerX, _centerY;     // burst origin, window DIPs
        private TimeSpan? _last, _lastRaise;
        private double _alpha = double.NaN;
        private bool _fxDone;

        private readonly StackPanel _panel = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
        private readonly TranslateTransform _shake = new();
        private readonly Grid _anchor = new() { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        private readonly FxLayer _fx;
        internal OutlinedTextBlock? Eyebrow { get; private set; }
        internal OutlinedTextBlock? Effect { get; private set; }
        internal double ShakeX => _shake.X;
        internal float Bloom => _bloom;
        internal int SparkCount => _sparkN;
        internal int Raises { get; private set; }
        internal int AlphaWrites { get; private set; }

        /// <summary>--render-all only: a FLASH cue at the bloom peak over grey.</summary>
        internal TakeoverAnnouncerWindow() : this(live: false)
        {
            Width = 1280;
            Height = 720;
            Background = Brushes.DimGray;
            Place(0, 0, 1280);
            Display("FLASH");
            Advance(TimeSpan.Zero, TimeSpan.Zero);
            Advance(TimeSpan.FromSeconds(0.15), TimeSpan.FromSeconds(0.15));
        }

        internal TakeoverAnnouncerWindow(bool live)
        {
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            _panel.RenderTransform = _shake;
            _anchor.Children.Add(_panel);
            _fx = new FxLayer(this);
            Content = new Grid { Children = { _fx, _anchor } };   // FX behind the text
        }

        /// <summary>Anchor over the primary work area, 92 DIP below its top (window-local DIPs).</summary>
        internal void Place(double left, double top, double width)
        {
            _anchor.Margin = new Thickness(left, top + TopOffsetDip, 0, 0);
            _anchor.Width = width;
            _centerX = (float)(left + width / 2.0);
            _centerY = (float)(top + TopOffsetDip + EyebrowFont + EffectFont * 0.65);
        }

        internal void Display(string label)
        {
            Eyebrow = new OutlinedTextBlock { Text = "TAKEOVER", FontSize = EyebrowFont, Fill = EyebrowFill, Stroke = StrokeBrush, StrokeThickness = 2.0, Family = TakeoverFont, HorizontalAlignment = HorizontalAlignment.Center, Opacity = 0.75 };
            Eyebrow.Build();
            Effect = new OutlinedTextBlock { Text = label.ToUpperInvariant(), FontSize = EffectFont, Fill = EffectFill, Stroke = StrokeBrush, StrokeThickness = 3.5, Family = TakeoverFont, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
            Effect.Build();
            _panel.Children.Clear();
            _panel.Children.Add(Eyebrow);
            _panel.Children.Add(Effect);
            SeedBurst();
            _last = _lastRaise = null;
            _fxDone = false;
            SetAlpha(0);
        }

        internal void ClearCue()
        {
            _panel.Children.Clear();
            Eyebrow = Effect = null;
            _sparkN = 0;
            _bloom = 0;
            _shake.X = _shake.Y = 0;
            _fxDone = true;
            SetAlpha(0);
            _fx.InvalidateVisual();
        }

        /// <summary>One frame at a cue's <paramref name="age"/>; <paramref name="now"/> drives the spark physics.</summary>
        internal void Advance(TimeSpan age, TimeSpan now)
        {
            SetAlpha(TakeoverCueQueue.Opacity(age));
            if (_fxDone) return;
            var dt = _last is { } l ? (float)Math.Min(0.1, (now - l).TotalSeconds) : 0f;   // clamp a spike (WPF)
            _last = now;

            // WPF re-asserts topmost every 0.2 s while the FX run, so effect overlays never cover the cue.
            // Real elapsed time, not the capped dt (P40).
            _lastRaise ??= now;
            if ((now - _lastRaise.Value).TotalSeconds >= 0.2) { _lastRaise = now; Raises++; X11Overlay.Raise(this); }

            if (dt > 0)
            {
                var drag = MathF.Exp(-2.4f * dt);
                for (var i = _sparkN - 1; i >= 0; i--)
                {
                    ref var s = ref _sparks[i];
                    s.Life -= dt;
                    if (s.Life <= 0f) { _sparks[i] = _sparks[--_sparkN]; continue; }
                    s.X += s.VX * dt; s.Y += s.VY * dt;
                    s.VX *= drag; s.VY *= drag;
                    s.VY += 34f * dt;   // settle
                }
            }

            var sec = age.TotalSeconds;
            _bloom = TakeoverCueQueue.BloomEnvelope(sec);
            var amp = TakeoverCueQueue.ShakeAmplitude(sec);
            _shake.X = amp > 0 ? (_rng.NextDouble() * 2 - 1) * amp : 0;
            _shake.Y = amp > 0 ? (_rng.NextDouble() * 2 - 1) * amp : 0;
            _fx.InvalidateVisual();
            _fxDone = _sparkN == 0 && sec > TakeoverCueQueue.BloomTotal && amp == 0;   // idle: text stays, FX stop
        }

        /// <summary>Only on change: the 1.7 s hold writes nothing (one X11 property write per change).</summary>
        private void SetAlpha(double a)
        {
            if (a == _alpha) return;
            _alpha = a;
            AlphaWrites++;
            if (!X11Overlay.SetOpacity(this, a)) Opacity = a;
        }

        private void SeedBurst()
        {
            _sparkN = 0;
            var count = 22 + _rng.Next(7);   // ~22-28
            for (var i = 0; i < count && _sparkN < MaxSparks; i++)
            {
                var ang = _rng.NextDouble() * Math.PI * 2;
                var spd = 60f + (float)_rng.NextDouble() * 110f;
                var life = 0.5f + (float)_rng.NextDouble() * 0.35f;
                _sparks[_sparkN++] = new Spark
                {
                    X = _centerX, Y = _centerY,
                    VX = (float)Math.Cos(ang) * spd, VY = (float)Math.Sin(ang) * spd,
                    Life = life, Max = life,
                    Size = 5f + (float)_rng.NextDouble() * 4f,
                    White = _rng.NextDouble() < 0.35,
                };
            }
        }

        /// <summary>The SKElement stand-in: snapshots the FX state on the UI thread, paints it on the leased canvas.</summary>
        private sealed class FxLayer : Control
        {
            private readonly TakeoverAnnouncerWindow _w;
            public FxLayer(TakeoverAnnouncerWindow w) { _w = w; IsHitTestVisible = false; }

            public override void Render(DrawingContext context)
            {
                if (_w._sparkN == 0 && _w._bloom <= 0.001f) return;
                context.Custom(new Op(new Rect(Bounds.Size), _w._sparks.AsSpan(0, _w._sparkN).ToArray(), _w._bloom, _w._centerX, _w._centerY));
            }
        }

        private sealed class Op : ICustomDrawOperation
        {
            private static readonly SKColorFilter VioletCF = SKColorFilter.CreateBlendMode(new SKColor(0xC9, 0xA0, 0xFF), SKBlendMode.Modulate);
            private static readonly SKColorFilter WhiteCF = SKColorFilter.CreateBlendMode(SKColors.White, SKBlendMode.Modulate);
            private static SKImage? _dot;
            private readonly Spark[] _sparks;
            private readonly float _env, _cx, _cy;

            public Op(Rect bounds, Spark[] sparks, float env, float cx, float cy) { Bounds = bounds; _sparks = sparks; _env = env; _cx = cx; _cy = cy; }
            public Rect Bounds { get; }
            public bool HitTest(Point p) => false;
            public bool Equals(ICustomDrawOperation? other) => false;
            public void Dispose() { }

            public void Render(ImmediateDrawingContext context)
            {
                var lease = context.TryGetFeature<ISkiaSharpApiLeaseFeature>();
                if (lease is null) return;
                using var leased = lease.Lease();
                try { Paint(leased.SkCanvas); }
                catch (Exception ex) { Log.Debug("TakeoverAnnouncer.Paint: {E}", ex.Message); }
            }

            private void Paint(SKCanvas canvas)
            {
                var img = Dot();
                using var paint = new SKPaint { BlendMode = SKBlendMode.Plus, IsAntialias = true };
                if (_env > 0.001f)
                {
                    var baseR = (float)EffectFont * 4.0f;
                    DrawDot(canvas, img, paint, _cx, _cy, baseR * (0.8f + 0.3f * _env), (byte)(70 * _env), VioletCF);         // wide bloom
                    DrawDot(canvas, img, paint, _cx, _cy, baseR * 0.5f * (0.8f + 0.3f * _env), (byte)(55 * _env), WhiteCF);  // inner lift
                }
                foreach (var s in _sparks)
                {
                    var t = s.Life / s.Max;          // 1 -> 0
                    var ease = t * t;                // fade fast at the tail
                    var rad = s.Size * (0.35f + 0.75f * t);
                    var cf = s.White ? WhiteCF : VioletCF;
                    DrawDot(canvas, img, paint, s.X, s.Y, rad * 2.1f, (byte)(40 * ease), cf);        // bloom
                    DrawDot(canvas, img, paint, s.X, s.Y, rad, (byte)(210 * ease), cf);              // body
                    DrawDot(canvas, img, paint, s.X, s.Y, rad * 0.5f, (byte)(230 * ease), WhiteCF);  // hot core
                }
            }

            private static void DrawDot(SKCanvas canvas, SKImage img, SKPaint paint, float cx, float cy, float radius, byte alpha, SKColorFilter tint)
            {
                if (radius <= 0.2f || alpha == 0) return;
                paint.ColorFilter = tint;
                paint.Color = new SKColor(255, 255, 255, alpha);
                canvas.DrawImage(img, new SKRect(cx - radius, cy - radius, cx + radius, cy + radius), new SKSamplingOptions(SKFilterMode.Linear), paint);
            }

            /// <summary>WPF Dot(): a soft white radial dot, tinted and added per draw.</summary>
            private static SKImage Dot()
            {
                if (_dot != null) return _dot;
                const int size = 128;
                using var surf = SKSurface.Create(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul));
                surf.Canvas.Clear(SKColors.Transparent);
                var r = size / 2f;
                using var shader = SKShader.CreateRadialGradient(new SKPoint(r, r), r,
                    new[] { new SKColor(255, 255, 255, 255), new SKColor(255, 255, 255, 160), new SKColor(255, 255, 255, 0) },
                    new[] { 0f, 0.32f, 1f }, SKShaderTileMode.Clamp);
                using var p = new SKPaint { Shader = shader, IsAntialias = true };
                surf.Canvas.DrawCircle(r, r, r, p);
                return _dot = surf.Snapshot();
            }
        }
    }
}
