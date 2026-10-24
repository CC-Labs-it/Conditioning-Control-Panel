using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services.Flash;
using ConditioningControlPanel.Services.Fyp.Online;

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
        // The picture, its glow card and the TvOff whiten move together on an exit; the sparks do not.
        private readonly Panel _card, _root;
        private readonly Border _glow, _whiten;
        internal Border? CreditMark { get; private set; }
        private Canvas? _sparks;
        private Panel? _slices;
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
            if (CreditMark != null) CreditMark.IsVisible = false; // WPF FlashLayer: no credit on exit fragments
            IsHitTestVisible = false;
            X11Overlay.SetClickThrough(this, true);
            GrowForExit();
            _exitScale = new ScaleTransform();
            _exitRotate = new RotateTransform();
            _exitShift = new TranslateTransform();
            _card.RenderTransform = new TransformGroup { Children = { _exitScale, _exitRotate, _exitShift } };
            BuildExitLayers(exit);
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
            var pic = _card.Bounds;
            double w = Bounds.Width, h = Bounds.Height;
            if (pic.Width <= 0 || pic.Height <= 0) return;
            var k = DesktopScaling > 0 ? DesktopScaling : 1;
            var side = Math.Max(pic.Width, pic.Height) / 2;
            _card.Width = pic.Width;
            _card.Height = pic.Height;
            _card.HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left;
            _card.VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top;
            _card.Margin = new Thickness(side + pic.X, side + pic.Y, 0, 0);
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

        /// <summary>WPF FlashLayer.DrawGlitch draws the picture as 7 horizontal slices knocked sideways;
        /// here 7 clipped copies of the same bitmap. Pop gets its spark layer (FlashLayer.DrawSparks).</summary>
        private void BuildExitLayers(FlashExitState exit)
        {
            if (exit.Style == FlashExitStyle.Glitch && _image.Source is { } src)
            {
                const int n = 7;
                double w = _card.Bounds.Width, h = _card.Bounds.Height;
                _slices = new Panel();
                for (var k = 0; k < n; k++)
                    _slices.Children.Add(new Image
                    {
                        Source = src, Stretch = Stretch.Uniform, RenderTransform = new TranslateTransform(),
                        Clip = new RectangleGeometry(new Rect(0, h * k / n, w, h / n)),
                    });
                _image.IsVisible = false;
                _card.Children.Insert(_card.Children.IndexOf(_image) + 1, _slices);
            }
            if (FlashExit.SparkCount(exit) > 0)
            {
                _sparks = new Canvas { IsHitTestVisible = false };
                for (var i = 0; i < FlashExit.SparkCount(exit); i++)
                    _sparks.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)) });
                _root.Children.Add(_sparks);
            }
        }

        private void Apply(FlashExitSample s)
        {
            // Avalonia's origin defaults to Center; WPF's pivot is (0.5, PivotY) of the rect.
            _card.RenderTransformOrigin = new RelativePoint(0.5, s.PivotY, RelativeUnit.Relative);
            _exitScale!.ScaleX = s.ScaleX;
            _exitScale.ScaleY = s.ScaleY;
            _exitRotate!.Angle = s.RotationDeg;
            _exitShift!.Y = s.OffsetY * _card.Bounds.Height;
            // TV off: the picture burns white as it collapses (FlashLayer: white at Whiten x alpha).
            _whiten.Opacity = Math.Clamp(s.Whiten, 0, 1);
            if (_slices != null && Exit is { } exit)
                for (var k = 0; k < _slices.Children.Count; k++)
                    ((TranslateTransform)_slices.Children[k].RenderTransform!).X = FlashExit.SliceOffset(exit, k) * _card.Bounds.Width;
            if (_sparks != null && Exit is { } pop)
            {
                // World space around the flash's box: centre + direction x distance x half its longer side.
                var b = _card.Bounds;
                double cx = b.X + b.Width / 2, cy = b.Y + b.Height / 2, half = Math.Max(b.Width, b.Height) / 2;
                for (var i = 0; i < _sparks.Children.Count; i++)
                {
                    var sp = FlashExit.Spark(pop, i);
                    var dot = _sparks.Children[i];
                    var r = sp.RadiusPx;
                    dot.IsVisible = sp.Alpha > 0 && r > 0.2;
                    dot.Width = dot.Height = 2 * r;
                    dot.Opacity = Math.Clamp(sp.Alpha, 0, 1);
                    Canvas.SetLeft(dot, cx + sp.Dx * sp.Distance * half - r);
                    Canvas.SetTop(dot, cy + sp.Dy * sp.Distance * half - r);
                }
            }
            X11Overlay.SetOpacity(this, _alpha * Math.Clamp(s.Alpha, 0, 1));
        }

        /// <summary>WPF SpawnFlashWindow glow (FlashService.cs:1881-1967; values from FlashFxRules): a
        /// halo round the picture, drawn as a BoxShadow on a sibling Border (P82: no Effect). The window
        /// grows by half the blur on every edge, as WPF pads the glow card; returns that pad in px.</summary>
        internal int ApplyGlow(FlashGlowLook look, double scaling)
        {
            if (!look.HasGlow) return 0;
            var c = Color.FromArgb((byte)Math.Clamp(look.Opacity * 255, 0, 255), (byte)(look.Rgb >> 16), (byte)(look.Rgb >> 8), (byte)look.Rgb);
            _glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = look.BlurRadius, Color = c });
            _glow.Background = Brushes.Black;
            var pad = look.BlurRadius / 2;
            _card.Margin = new Thickness(pad);
            return GlowPadPx = (int)Math.Round(pad * (scaling > 0 ? scaling : 1));
        }

        /// <summary>Pixels the glow added on each edge of the picture (0 without a glow).</summary>
        internal int GlowPadPx { get; private set; }

        /// <summary>Test seams: the picture's current transform (null before an exit), the glow, the
        /// whiten, the glitch slices and the sparks.</summary>
        internal Transform? PictureTransform => _card.RenderTransform as Transform;
        internal BoxShadows GlowShadow => _glow.BoxShadow;
        internal double WhitenOpacity => _whiten.Opacity;
        internal Panel? Slices => _slices;
        internal Canvas? Sparks => _sparks;

        // Hydra bookkeeping (WPF FlashWindow.HydraGeneration / OriginalLifetimeMs / ExpiresAt / Monitor).
        internal int HydraGeneration { get; set; }
        internal bool Ambient { get; set; }
        internal TimeSpan Lifetime { get; set; }
        internal long SpawnedAt { get; set; }
        internal Screen? SpawnScreen { get; set; }
        /// <summary>The motion a hydra parent passes down (WPF inheritMotion), and the one this flash resolved.</summary>
        internal FlashMotionStyle? InheritMotion { get; set; }
        internal FlashMotionStyle MotionStyle { get; set; }

        /// <summary>--render-all only: the flash card art as a sample picture.</summary>
        internal FlashOverlayWindow()
            : this(new Bitmap(AssetLoader.Open(new Uri("avares://CCP.Avalonia/Resources/features/flash.png"))))
        {
            Width = 400;
            Height = 300;
        }

        public FlashOverlayWindow(Bitmap picture, string? path = null)
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
            _glow = new Border { IsHitTestVisible = false };
            _whiten = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
            // Centred, so the card is the letterboxed picture, not the window (as the bare Image was).
            _card = new Panel
            {
                Children = { _glow, _image, _whiten },
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            };
            if (ScrolllerCredit.IsScrolllerMedia(path))
            {
                CreditMark = new Border
                {
                    Opacity = 0.75, CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2),
                    Background = new SolidColorBrush(Color.FromArgb(0x99, 0x10, 0x10, 0x14)),
                    Child = new TextBlock { Text = ScrolllerCredit.Mark, FontSize = 11, Foreground = Brushes.White, MaxLines = 1, IsHitTestVisible = false },
                    IsHitTestVisible = false,
                    HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Right,
                    VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Bottom,
                    Margin = new Thickness(0, 0, 6, 4),
                };
                _card.Children.Add(CreditMark); // same picture geometry, transform and native window fade
            }
            _root = new Panel { Children = { _card } };
            Content = _root;
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
