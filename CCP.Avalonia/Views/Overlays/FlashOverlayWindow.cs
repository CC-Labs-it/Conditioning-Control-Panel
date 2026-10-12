using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Services.Flash;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// One flash image on the desktop: the Avalonia twin of WPF's per-flash <c>FlashWindow</c>
    /// (ConditioningControlPanel/Services/Flash/FlashService.cs, SpawnFlashWindow). Exactly the
    /// image's size, fades in to the Opacity slider, holds for its lifetime, fades out, closes.
    /// <see cref="FlashOverlay"/> places it and makes it an override-redirect click-through window.
    ///
    /// <para><b>No geometry in the constructor, on purpose.</b> Width/Height/Position set before
    /// <c>X11Overlay.SetOverrideRedirect</c> are replayed by the WM later and can land after the
    /// real size (see that method's notes); the host sets geometry through it.</para>
    ///
    /// <para><b>The fade is the compositor's, not ours</b> (<see cref="X11Overlay.SetOpacity"/>):
    /// animating the Image's Opacity re-renders the window every frame, and on the default GLX
    /// renderer under XWayland that stalled the UI dispatcher ~1.7 s per burst (staggers and
    /// timers late). With the property the app renders each flash exactly once.</para>
    /// </summary>
    internal sealed class FlashOverlayWindow : Window
    {
        private readonly Image _image;
        private double _alpha = 1;
        private DispatcherTimer? _exitTimer;
        private long _lastExitTick;
        private ScaleTransform? _exitScale;
        private RotateTransform? _exitRotate;
        private TranslateTransform? _exitShift;

        /// <summary>The leave animation running after a pop, or null.</summary>
        internal FlashExitState? Exit { get; private set; }

        /// <summary>WPF FlashService.cs:1765 IsClickable: this flash takes the mouse, and a press
        /// pops it (<paramref name="onPop"/>). Without this call the window stays hit-test invisible.</summary>
        internal void MakeClickable(Action onPop)
        {
            IsHitTestVisible = true;
            PointerPressed += (_, e) => { e.Handled = true; onPop(); };
        }

        /// <summary>Play <paramref name="exit"/> (WPF FlashLayer draws FlashExit.Sample the same way)
        /// and close when it is done. The flash no longer takes clicks while it leaves.</summary>
        internal void BeginExit(FlashExitState exit)
        {
            if (Exit != null) return;
            Exit = exit;
            IsHitTestVisible = false;
            X11Overlay.SetClickThrough(this, true);
            GrowForExit();
            _exitScale = new ScaleTransform();
            _exitRotate = new RotateTransform();
            _exitShift = new TranslateTransform();
            _image.RenderTransform = new TransformGroup { Children = { _exitScale, _exitRotate, _exitShift } };
            _lastExitTick = FlashOverlay.Clock.GetTimestamp();
            Apply(FlashExit.Sample(exit));
            _exitTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => ExitFrame());
            _exitTimer.Start();
            Closed += (_, _) => { _exitTimer?.Stop(); _exitTimer = null; };
        }

        /// <summary>The window is exactly the picture, so Pop's swell, Spiral's corners and Melt's drop
        /// would clip at its edge (WPF draws exits on a full-screen layer). Grow the window around the
        /// picture, which stays the same size at the same screen spot: half its longer side on every
        /// edge, plus 1.1x its height below for the melt.</summary>
        private void GrowForExit()
        {
            var pic = _image.Bounds;
            double w = Bounds.Width, h = Bounds.Height;
            if (pic.Width <= 0 || pic.Height <= 0) return;
            var k = DesktopScaling > 0 ? DesktopScaling : 1;
            var side = Math.Max(pic.Width, pic.Height) / 2;
            _image.Width = pic.Width;
            _image.Height = pic.Height;
            _image.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            _image.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            _image.Margin = new Thickness(side + pic.X, side + pic.Y, 0, 0);
            var shift = (int)Math.Round(side * k);
            Position = new PixelPoint(Position.X - shift, Position.Y - shift);
            Width = w + 2 * side;
            Height = h + 2 * side + pic.Height * 1.1;
        }

        /// <summary>One exit frame on real elapsed time (P40); closes the window when it is done.</summary>
        internal void ExitFrame()
        {
            if (Exit is not { } exit) return;
            var now = FlashOverlay.Clock.GetTimestamp();
            FlashExit.Step(exit, FlashOverlay.Clock.GetElapsedTime(_lastExitTick, now).TotalSeconds);
            _lastExitTick = now;
            Apply(FlashExit.Sample(exit));
            if (exit.Done) { _exitTimer?.Stop(); _exitTimer = null; Close(); }
        }

        // ponytail: WPF also draws the TvOff whiten, the Glitch slices and the Pop sparks
        // (FlashExit.SliceOffset/Spark); this window plays scale, spin, drop and alpha only.
        private void Apply(FlashExitSample s)
        {
            // Avalonia's origin defaults to Center; WPF's pivot is (0.5, PivotY) of the rect.
            _image.RenderTransformOrigin = new RelativePoint(0.5, s.PivotY, RelativeUnit.Relative);
            _exitScale!.ScaleX = s.ScaleX;
            _exitScale.ScaleY = s.ScaleY;
            _exitRotate!.Angle = s.RotationDeg;
            _exitShift!.Y = s.OffsetY * _image.Bounds.Height;
            X11Overlay.SetOpacity(this, _alpha * Math.Clamp(s.Alpha, 0, 1));
        }

        /// <summary>Test seam: the picture's current transform (null before an exit).</summary>
        internal Transform? PictureTransform => _image.RenderTransform as Transform;

        /// <summary>--render-all only: the flash card art as a sample picture.</summary>
        internal FlashOverlayWindow()
            : this(new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))))
        {
            Width = 400;
            Height = 300;
        }

        public FlashOverlayWindow(Bitmap picture)
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
            _image = new Image { Source = picture, Stretch = Stretch.Uniform };
            Content = _image;
            Closed += (_, _) => picture.Dispose();
        }

        /// <summary>
        /// Fade in to <paramref name="alpha"/> over <paramref name="fade"/>, hold until
        /// <paramref name="lifetime"/> after spawn, fade out over <paramref name="fade"/>, close.
        /// Same envelope as WPF's heartbeat: ExpiresAt = spawn + lifetime, ramps inside it.
        /// </summary>
        public void Run(double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            _alpha = alpha;
            Fade(0, alpha, fade);
            DispatcherTimer.RunOnce(() => Fade(alpha, 0, fade, Close), lifetime);
        }

        /// <summary>WPF's heartbeat ramp - linear, alpha written in 1/32 steps (FADE_ALPHA_EPSILON) -
        /// applied by the compositor through <see cref="X11Overlay.SetOpacity"/>.</summary>
        private void Fade(double from, double to, TimeSpan fade, Action? done = null)
        {
            // WPF moves alpha by dt/fadeSeconds per frame, so a ramp takes |to-from| x fade:
            // at Opacity 50% the fade is half as long, not a slower full-length one.
            var span = fade * Math.Abs(to - from);
            var steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(to - from) * 32));
            for (var i = 1; i <= steps; i++)
            {
                var v = from + (to - from) * i / steps;
                var last = i == steps;
                DispatcherTimer.RunOnce(() => { if (Exit != null) return; X11Overlay.SetOpacity(this, v); if (last) done?.Invoke(); }, span * i / steps + TimeSpan.FromMilliseconds(1));
            }
        }
    }
}
