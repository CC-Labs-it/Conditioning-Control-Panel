using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// VibePopping's buzz telegraph, the twin of WPF ConditioningControlPanel/Chaos/ChaosVibeTrailOverlay.cs:
    /// while a vibe runs the pointer wears a warm amber glow and leaves a short fading trail. One
    /// click-through, full-stage window with a fixed ring of dots; the maths (emit distance, fade,
    /// shrink, buzz pulse) is <see cref="ChaosVibeTrail"/> in Core. Same keep-alive contract:
    /// created at run start, shown per vibe, hidden between, closed at teardown or panic.
    ///
    /// <para>Mapping: WPF's DoubleAnimations become one 16 ms follow tick that reads every dot
    /// from Core (P82: no Animation on a Transform), so nothing ticks while hidden (P01).
    /// <c>GetCursorPos</c> becomes <see cref="X11Pointer"/> (physical pixels; <c>Position</c> is
    /// physical too, only the offset is divided by scaling). <c>WS_EX_TRANSPARENT</c> becomes
    /// <see cref="X11Overlay.SetClickThrough"/>; a platform that refuses click-through or
    /// per-pixel transparency gets no trail rather than a full-screen block (PinkFilterOverlay.Accept).</para>
    ///
    /// <para>Lifecycle: ChaosRunHost uses the WPF ChaosModeService.cs:455,2706,2943 callers.
    /// Windows/native Wayland desktop pointer support remains missing.</para>
    /// </summary>
    internal sealed class ChaosVibeTrailOverlay : Window
    {
        private static ChaosVibeTrailOverlay? _active;
        internal static bool SkipPlatformChecksForTests;
        private static bool _refused;   // the platform said no once: later vibes don't rebuild and re-warn
        internal static ChaosVibeTrailOverlay? Active => _active;

        /// <summary>Create the (hidden) trail window ahead of time at run start (WPF :33).</summary>
        public static void EnsureCreated() => Dispatcher.UIThread.Post(() =>
        {
            try { _active ??= new ChaosVibeTrailOverlay(); }
            catch (Exception ex) { Log.Debug("ChaosVibeTrail.EnsureCreated: {E}", ex.Message); }
        });

        /// <summary>The vibe started: show the glow + trail and follow the pointer (WPF :57).</summary>
        public static void Start() => Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (_refused) return;
                if (!SkipPlatformChecksForTests && (!X11Overlay.IsAvailable || X11Pointer.Read() == null))
                { Log.Debug("ChaosVibeTrail: no desktop pointer, no trail"); return; }
                _active ??= new ChaosVibeTrailOverlay();
                var w = _active;
                if (!w.IsVisible)
                {
                    w.SizeToStage();
                    w.Show();
                    if (!SkipPlatformChecksForTests && (!X11Overlay.SetClickThrough(w, true) || w.ActualTransparencyLevel == WindowTransparencyLevel.None))
                    {
                        Log.Warning("ChaosVibeTrail: no click-through or no per-pixel transparency here, so no trail - it would cover the desktop");
                        _refused = true;
                        CloseNow();
                        return;
                    }
                }
                if (!SkipPlatformChecksForTests) X11Overlay.Raise(w);   // un-hiding doesn't re-stack (WPF ChaosWindowZ.RaiseAboveVideo)
                w.BeginFollow();
            }
            catch (Exception ex) { Log.Debug("ChaosVibeTrail.Start: {E}", ex.Message); }
        });

        /// <summary>The vibe ended: stop following and hide; the window stays for the next buzz (WPF :82).</summary>
        public static void Stop() => Dispatcher.UIThread.Post(() => { try { _active?.EndFollow(); } catch { } });

        /// <summary>Re-stack above a mandatory video (WPF :93). UI thread only.</summary>
        internal static Action<Window> RaiseWindow = w => X11Overlay.Raise(w);
        public static void RaiseActive() { if (_active is { IsVisible: true } w) RaiseWindow(w); }

        /// <summary>Run end, shutdown and panic: the only place this window dies (WPF :96).
        /// Synchronous on the UI thread so panic never waits on a post (P29); posted otherwise.</summary>
        public static void CloseActive()
        {
            if (Dispatcher.UIThread.CheckAccess()) CloseNow();
            else Dispatcher.UIThread.Post(CloseNow);
        }

        private static void CloseNow()
        {
            var w = _active;
            _active = null;
            if (w == null) return;
            try { w._follow.Stop(); w.Close(); } catch (Exception ex) { Log.Debug("ChaosVibeTrail close: {E}", ex.Message); }
        }

        // ---------------------------------------------------------------------------------------

        private readonly ChaosVibeTrail _trail = new();
        private readonly Ellipse _glow;
        private readonly ScaleTransform _glowScale = new(1, 1);
        private readonly Ellipse[] _dots = new Ellipse[ChaosVibeTrail.Dots];
        private readonly ScaleTransform[] _dotScales = new ScaleTransform[ChaosVibeTrail.Dots];
        private readonly DispatcherTimer _follow;
        private long _pulseStart;
        private double _stageScale;   // the scaling SizeToStage sized with; Tick maps the pointer with the same one

        /// <summary>Desktop-wide pointer in physical pixels; a seam so tests play a sweep.</summary>
        internal Func<PixelPoint?> ReadPointer = () => X11Pointer.Read()?.At;
        /// <summary>The stepped clock (P08): tests advance it, the timer only calls <see cref="Tick"/>.</summary>
        internal Func<long> NowMs = () => Environment.TickCount64;

        internal ChaosVibeTrailOverlay()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = CoreSettings.Current.ChaosPinOnTop;   // WPF ChaosWindowZ.BornTopmost
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = 640; Height = 400;   // SizeToStage replaces this before every show

            var canvas = new Canvas { IsHitTestVisible = false };
            Content = canvas;

            // Trail dots first (under the glow): warm amber sparks (WPF :140-143).
            var dotBrush = Radial((0.0, Color.FromArgb(190, 0xFF, 0xD7, 0x6A)),
                                  (0.55, Color.FromArgb(110, 0xFF, 0xB0, 0x3A)),
                                  (1.0, Color.FromArgb(0, 0xFF, 0x8A, 0x3A)));
            for (int i = 0; i < _dots.Length; i++)
            {
                _dotScales[i] = new ScaleTransform(1, 1);
                _dots[i] = new Ellipse
                {
                    Width = ChaosVibeTrail.DotSize, Height = ChaosVibeTrail.DotSize, Fill = dotBrush,
                    IsHitTestVisible = false, Opacity = 0,
                    RenderTransformOrigin = RelativePoint.Center, RenderTransform = _dotScales[i],
                };
                canvas.Children.Add(_dots[i]);
            }

            // The pointer glow: amber like the VibePopping banner (WPF :163-166).
            _glow = new Ellipse
            {
                Width = ChaosVibeTrail.GlowSize, Height = ChaosVibeTrail.GlowSize,
                Fill = Radial((0.12, Color.FromArgb(0, 0xFF, 0xE9, 0xA0)),
                              (0.5, Color.FromArgb(160, 0xFF, 0xB0, 0x3A)),
                              (1.0, Color.FromArgb(0, 0xFF, 0x69, 0xB4))),
                IsHitTestVisible = false, Opacity = 0,
                RenderTransformOrigin = RelativePoint.Center, RenderTransform = _glowScale,
            };
            canvas.Children.Add(_glow);

            _follow = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ChaosVibeTrail.TickMs) };
            _follow.Tick += (_, _) => Tick();

            // --render-all photographs a fresh window: draw a sample sweep so the frame shows the
            // look. It is never visible otherwise - BeginFollow clears it before any real vibe.
            PaintSample();
        }

        private static RadialGradientBrush Radial(params (double At, Color C)[] stops)
        {
            var b = new RadialGradientBrush { Center = RelativePoint.Center, GradientOrigin = RelativePoint.Center };
            foreach (var (at, c) in stops) b.GradientStops.Add(new GradientStop(c, at));
            return b;
        }

        internal bool IsFollowing => _follow.IsEnabled;

        internal void BeginFollow()
        {
            _trail.Begin();
            _trail.Clear();   // the render sample; WPF's EndFollow already left it empty
            _pulseStart = NowMs();
            _glow.Opacity = 1;
            Tick();              // snap to the pointer before the first frame renders
            _follow.Start();
        }

        internal void EndFollow()
        {
            _follow.Stop();
            _trail.Clear();
            _glow.Opacity = 0;
            foreach (var d in _dots) d.Opacity = 0;
            try { Hide(); } catch { }   // idle hidden between vibes
        }

        /// <summary>One follow tick (WPF FollowTick :221). Does nothing while hidden (P01).</summary>
        internal void Tick()
        {
            if (!IsVisible) { _follow.Stop(); return; }
            try
            {
                long now = NowMs();
                if (ReadPointer() is PixelPoint px)
                {
                    double s = _stageScale > 0 ? _stageScale : RenderScaling > 0 ? RenderScaling : 1;
                    _trail.Move((px.X - Position.X) / s, (px.Y - Position.Y) / s, now);
                }
                Paint(now, ChaosVibeTrail.GlowScale(now - _pulseStart));
            }
            catch (Exception ex) { Log.Debug("ChaosVibeTrail tick: {E}", ex.Message); }
        }

        private void Paint(double now, double glowScale)
        {
            Canvas.SetLeft(_glow, _trail.GlowX - ChaosVibeTrail.GlowSize / 2);
            Canvas.SetTop(_glow, _trail.GlowY - ChaosVibeTrail.GlowSize / 2);
            _glowScale.ScaleX = _glowScale.ScaleY = glowScale;
            for (int i = 0; i < _dots.Length; i++)
            {
                var (x, y, o, sc) = _trail.Dot(i, now);
                _dots[i].Opacity = o;
                if (o <= 0) continue;
                Canvas.SetLeft(_dots[i], x - ChaosVibeTrail.DotSize / 2);
                Canvas.SetTop(_dots[i], y - ChaosVibeTrail.DotSize / 2);
                _dotScales[i].ScaleX = _dotScales[i].ScaleY = sc;
            }
        }

        private void PaintSample()
        {
            for (int i = 0; i <= ChaosVibeTrail.Dots; i++)
                _trail.Move(120 + i * 28, 300 - i * 14, i * 24);
            _glow.Opacity = 1;
            Paint(ChaosVibeTrail.Dots * 24, ChaosVibeTrail.GlowScale(90));
        }

        /// <summary>WPF ChaosWindowZ.StageBounds: the primary screen, or every screen when
        /// multi-monitor is on. Bounds are physical; Width/Height are DIPs.</summary>
        private void SizeToStage()
        {
            try
            {
                var all = Screens?.All;
                var primary = Screens?.Primary ?? all?.FirstOrDefault();
                if (primary is null) return;
                var b = CoreSettings.Current.DualMonitorEnabled && all is { Count: > 1 }
                    ? all.Select(x => x.Bounds).Aggregate((a, c) => a.Union(c))
                    : primary.Bounds;
                double scale = _stageScale = primary.Scaling > 0 ? primary.Scaling : 1;
                Position = b.Position;
                Width = b.Width / scale;
                Height = b.Height / scale;
            }
            catch (Exception ex) { Log.Debug("ChaosVibeTrail: no screen to size to ({E})", ex.Message); }
        }
    }
}
