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
        private readonly Panel _card, _picture, _root;
        private readonly Border _glow, _whiten;
        private Canvas? _sparks;
        private Panel? _slices;
        private Image? _redGhost, _cyanGhost;
        private Panel? _shards;
        internal FlashShatterState? Shatter { get; private set; }
        private double _alpha = 1;
        private DispatcherTimer? _lifetimeTimer;
        private TimeSpan _fade;
        private long _expiresAt;
        internal long ExpiresAt => _expiresAt;
        internal bool IsFadingOut { get; private set; }
        internal double DwellScale => (_card.RenderTransform as ScaleTransform)?.ScaleX ?? 1;
        internal void SetGazeDwellProgress(double progress)
        {
            if (IsFadingOut || Exit != null) return;
            var scale = 1 + Math.Clamp(progress, 0, 1) * 0.10;
            if (_card.RenderTransform is ScaleTransform st) st.ScaleX = st.ScaleY = scale;
            else _card.RenderTransform = new ScaleTransform(scale, scale);
        }
        internal void BoostLifetime(int extraMs)
        {
            if (extraMs <= 0 || IsFadingOut || Exit != null || _lifetimeTimer == null) return;
            var deadline = FlashOverlay.Clock.GetTimestamp() + (long)(extraMs / 1000.0 * FlashOverlay.Clock.TimestampFrequency);
            if (deadline <= _expiresAt) return; // WPF: never shorten a stay-until-popped lifetime
            _expiresAt = deadline;
            ScheduleExpiry();
        }
        private void ScheduleExpiry()
        {
            _lifetimeTimer?.Stop();
            var remaining = FlashOverlay.Clock.GetElapsedTime(FlashOverlay.Clock.GetTimestamp(), _expiresAt);
            _lifetimeTimer = new DispatcherTimer(remaining > TimeSpan.Zero ? remaining : TimeSpan.FromMilliseconds(1), DispatcherPriority.Normal, (_, _) => Expire());
            _lifetimeTimer.Start();
        }
        internal void Expire()
        {
            _lifetimeTimer?.Stop(); _lifetimeTimer = null;
            IsFadingOut = true;
            Fade(_alpha, 0, _fade, Close);
        }
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
            if (Exit != null || Shatter != null) return;
            Exit = exit;
            _lifetimeTimer?.Stop(); _lifetimeTimer = null;
            IsHitTestVisible = false;
            X11Overlay.SetClickThrough(this, true);
            GrowForExit();
            _exitScale = new ScaleTransform();
            _exitRotate = new RotateTransform();
            _exitShift = new TranslateTransform();
            _card.RenderTransform = new TransformGroup { Children = { _exitScale, _exitRotate, _exitShift } };
            BuildExitLayers(exit);
            Apply(FlashExit.Sample(exit));
            StartExitClock();
        }

        /// <summary>WPF BuildShatter/DrawShards: hard-edged pieces of the letterboxed image,
        /// no glow halo, stepping the shared pixel-space physics on the monitor it is on now.</summary>
        internal bool TryBeginShatter(MotionLevel motion, Random rng)
        {
            if (Exit != null || Shatter != null || _image.Source is not { } source) return false;
            UpdateLayout();
            var fit = Stretch.Uniform.CalculateSize(_image.Bounds.Size, source.Size);
            var scale = DesktopScaling > 0 ? DesktopScaling : 1;
            var imageOrigin = _image.TranslatePoint(default, this) ?? default;
            var local = imageOrigin + new Point((_image.Bounds.Width - fit.Width) / 2, (_image.Bounds.Height - fit.Height) / 2);
            var x = Position.X + local.X * scale;
            var y = Position.Y + local.Y * scale;
            var monitor = Screens.ScreenFromPoint(new PixelPoint((int)(x + fit.Width * scale / 2), (int)(y + fit.Height * scale / 2))) ?? SpawnScreen;
            var bounds = monitor?.Bounds ?? new PixelRect(Position, PixelSize.FromSize(Bounds.Size, scale));
            var state = FlashShatter.Create(x, y, fit.Width * scale, fit.Height * scale,
                bounds.X, bounds.Y, bounds.Width, bounds.Height, motion, rng);
            if (state.Shards.Length == 0) return false; // Motion Off falls through to FlashExit, like WPF.
            Shatter = state;
            IsHitTestVisible = false;
            X11Overlay.SetClickThrough(this, true);
            // WPF draws across its full-screen compositor. Give this short-lived break the same room.
            Position = bounds.Position;
            Width = bounds.Width / scale;
            Height = bounds.Height / scale;
            _card.IsVisible = false;
            _shards = new Panel
            {
                Width = fit.Width, Height = fit.Height,
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top,
                Margin = new Thickness((x - bounds.X) / scale, (y - bounds.Y) / scale, 0, 0),
            };
            foreach (var shard in state.Shards)
            {
                var cut = new Rect(shard.U0 * fit.Width, shard.V0 * fit.Height,
                    (shard.U1 - shard.U0) * fit.Width, (shard.V1 - shard.V0) * fit.Height);
                _shards.Children.Add(new Border
                {
                    Name = "FlashShard",
                    Background = _look.HasGlow ? null : Brushes.Black,
                    Child = new Image { Source = source, Stretch = Stretch.Fill },
                    Clip = new RectangleGeometry(cut),
                    RenderTransformOrigin = new RelativePoint(cut.Center, RelativeUnit.Absolute),
                    RenderTransform = new TransformGroup { Children = { new RotateTransform(), new TranslateTransform() } },
                });
            }
            _root.Children.Add(_shards);
            StartExitClock();
            return true;
        }

        private void StartExitClock()
        {
            _lastExitTick = FlashOverlay.Clock.GetTimestamp();
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
            if (Shatter is { } shatter && _shards is { } shards)
            {
                var stamp = FlashOverlay.Clock.GetTimestamp();
                FlashShatter.Step(shatter, FlashOverlay.Clock.GetElapsedTime(_lastExitTick, stamp).TotalSeconds);
                _lastExitTick = stamp;
                var scale = DesktopScaling > 0 ? DesktopScaling : 1;
                for (var i = 0; i < shards.Children.Count; i++)
                {
                    var piece = shatter.Shards[i];
                    var image = shards.Children[i];
                    var transform = (TransformGroup)image.RenderTransform!;
                    ((RotateTransform)transform.Children[0]).Angle = piece.AngleRad * 180 / Math.PI;
                    var shift = (TranslateTransform)transform.Children[1];
                    shift.X = piece.Dx / scale;
                    shift.Y = piece.Dy / scale;
                    image.Opacity = Math.Clamp(piece.Alpha, 0, 1);
                }
                X11Overlay.SetOpacity(this, _alpha);
                if (shatter.Done) { _exitTimer?.Stop(); _exitTimer = null; Close(); }
                return;
            }
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
                var pixels = CopyPixels(src as Bitmap);
                _redGhost = BuildGhost("FlashGlitchRed", src, pixels, Color.FromRgb(255, 40, 90));
                _cyanGhost = BuildGhost("FlashGlitchCyan", src, pixels, Color.FromRgb(40, 230, 255));
                var index = _picture.Children.IndexOf(_image) + 1;
                _picture.Children.Insert(index, _redGhost);
                _picture.Children.Insert(index + 1, _cyanGhost);
                for (var k = 0; k < n; k++)
                    _slices.Children.Add(new Image
                    {
                        Source = src, Stretch = Stretch.Uniform, RenderTransform = new TranslateTransform(),
                        Clip = new RectangleGeometry(new Rect(0, h * k / n, w, h / n)),
                    });
                _image.IsVisible = false;
                _picture.Children.Insert(_picture.Children.IndexOf(_cyanGhost) + 1, _slices);
            }
            if (FlashExit.SparkCount(exit) > 0)
            {
                _sparks = new Canvas { IsHitTestVisible = false };
                for (var i = 0; i < FlashExit.SparkCount(exit); i++)
                    _sparks.Children.Add(new global::Avalonia.Controls.Shapes.Ellipse { Fill = new SolidColorBrush(Color.FromRgb(0xFF, 0x69, 0xB4)) });
                _root.Children.Add(_sparks);
            }
        }

        // WPF FlashLayer.DrawGlitch modulates the source pixels, not a solid colored rectangle.
        // ponytail: cache two tinted frames only for this 280 ms exit; no Effect or animated filter.
        // One CopyPixels per exit, then a byte-wise modulate per ghost: no encode/decode on the UI thread.
        private static (byte[] Data, Bitmap Source)? CopyPixels(Bitmap? bmp)
        {
            if (bmp?.Format is not { } fmt || (fmt != PixelFormat.Bgra8888 && fmt != PixelFormat.Rgba8888)) return null;
            var size = bmp.PixelSize;
            var data = new byte[size.Width * 4 * size.Height];
            var pin = System.Runtime.InteropServices.GCHandle.Alloc(data, System.Runtime.InteropServices.GCHandleType.Pinned);
            try { bmp.CopyPixels(new PixelRect(size), pin.AddrOfPinnedObject(), data.Length, size.Width * 4); }
            finally { pin.Free(); }
            return (data, bmp);
        }

        private Image BuildGhost(string name, IImage source, (byte[] Data, Bitmap Source)? pixels, Color tint)
        {
            var ghost = source;
            if (pixels is var (data, bmp))
            {
                var bgra = bmp.Format == PixelFormat.Bgra8888;
                byte c0 = bgra ? tint.B : tint.R, c2 = bgra ? tint.R : tint.B;
                var tinted = new byte[data.Length];
                // Modulate (Skia SKBlendMode.Modulate): channel * tint / 255; alpha unchanged, so premul stays valid.
                for (var i = 0; i < data.Length; i += 4)
                {
                    tinted[i] = (byte)(data[i] * c0 / 255);
                    tinted[i + 1] = (byte)(data[i + 1] * tint.G / 255);
                    tinted[i + 2] = (byte)(data[i + 2] * c2 / 255);
                    tinted[i + 3] = data[i + 3];
                }
                var wb = new WriteableBitmap(bmp.PixelSize, bmp.Dpi, bmp.Format!.Value, bmp.AlphaFormat ?? AlphaFormat.Premul);
                using (var fb = wb.Lock())
                {
                    var stride = bmp.PixelSize.Width * 4;
                    for (var y = 0; y < bmp.PixelSize.Height; y++)
                        System.Runtime.InteropServices.Marshal.Copy(tinted, y * stride, fb.Address + y * fb.RowBytes, stride);
                }
                Closed += (_, _) => wb.Dispose();
                ghost = wb;
            }
            return new Image
            {
                Name = name, Source = ghost, Stretch = Stretch.Uniform,
                RenderTransform = new TranslateTransform(), IsHitTestVisible = false, Opacity = 0,
            };
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
            if (_redGhost != null && _cyanGhost != null)
            {
                var fit = Stretch.Uniform.CalculateSize(_card.Bounds.Size, _image.Source!.Size);
                var split = s.Glitch * 0.04 * fit.Width;
                ((TranslateTransform)_redGhost.RenderTransform!).X = -split;
                ((TranslateTransform)_cyanGhost.RenderTransform!).X = split;
                // WPF applies alpha both in the modulation filter and in the image paint.
                _redGhost.Opacity = _cyanGhost.Opacity = split > 0.5 ? Math.Clamp(_alpha * s.Alpha / 2, 0, 1) : 0;
            }
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
            _look = look;
            SetGlow(look.BlurRadius, look.Opacity);
            _glow.Background = Brushes.Black;
            var pad = look.BlurRadius / 2;
            _card.Margin = new Thickness(pad);
            return GlowPadPx = (int)Math.Round(pad * (scaling > 0 ? scaling : 1));
        }

        /// <summary>Pixels the glow added on each edge of the picture (0 without a glow).</summary>
        internal int GlowPadPx { get; private set; }

        private FlashGlowLook _look;
        private ConditioningControlPanel.Avalonia.Helpers.BeatLoop? _pulse;

        private void SetGlow(double blur, double opacity)
        {
            var c = Color.FromArgb((byte)Math.Clamp(opacity * 255, 0, 255), (byte)(_look.Rgb >> 16), (byte)(_look.Rgb >> 8), (byte)_look.Rgb);
            _glow.BoxShadow = new BoxShadows(new BoxShadow { Blur = blur, Color = c });
        }

        /// <summary>WPF FlashService.cs:2002-2015 (FlashLayer.cs:417 on the compositor): a lucky flash's
        /// gold glow pulses, blur r..1.6r and opacity 0.7..1.0, 400 ms autoreverse, until it closes.
        /// Core <see cref="FlashFxRules.LuckyPulseAt"/> is the curve, on the shared BeatLoop.</summary>
        internal void StartLuckyPulse()
        {
            if (!_look.LuckyPulse || _pulse != null) return;
            _pulse = new ConditioningControlPanel.Avalonia.Helpers.BeatLoop(_glow, t =>
            {
                var (blur, opacity) = FlashFxRules.LuckyPulseAt(_look.BlurRadius, t);
                SetGlow(blur, opacity);
            });
            _pulse.Start();   // the shared 30 fps beat; it stops itself when the window closes
        }

        internal bool Pulsing => _pulse?.IsRunning == true;

        /// <summary>The corner radius (DIPs) from Core <see cref="FlashCorners"/>: WPF rounds the glow card
        /// and clips the picture to it (FlashService.cs:1956-1966, 2020-2040, ApplyCornerClip :2411).</summary>
        internal double CornerRadiusDip { get; private set; }

        internal void ApplyCorners(double radius)
        {
            CornerRadiusDip = radius;
            _glow.CornerRadius = new CornerRadius(radius);
            ClipCorners();
        }

        /// <summary>WPF FlashLayer.cs:441-470 clips the picture, Glitch and TV whiten together,
        /// after drawing the glow. The clip follows the letterboxed picture, not the outer card.</summary>
        private void ClipCorners()
        {
            if (CornerRadiusDip <= 0 || _image.Source is not { } src || _image.Bounds.Width <= 0 || _image.Bounds.Height <= 0)
            {
                _picture.Clip = null;
                return;
            }
            var box = _image.Bounds.Size;
            var fit = Stretch.Uniform.CalculateSize(box, src.Size);
            var r = Math.Min(CornerRadiusDip, Math.Min(fit.Width, fit.Height) / 2);
            _picture.Clip = new RectangleGeometry(new Rect((box.Width - fit.Width) / 2, (box.Height - fit.Height) / 2, fit.Width, fit.Height), r, r);
        }

        /// <summary>Test seams: the picture's current transform (null before an exit), the glow, the
        /// whiten, the glitch slices and the sparks.</summary>
        internal Transform? PictureTransform => _card.RenderTransform as Transform;
        internal BoxShadows GlowShadow => _glow.BoxShadow;
        internal Geometry? PictureClip => _picture.Clip;
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
            _image.SizeChanged += (_, _) => ClipCorners();
            _glow = new Border { Background = Brushes.Black, IsHitTestVisible = false };
            _whiten = new Border { Background = Brushes.White, Opacity = 0, IsHitTestVisible = false };
            _picture = new Panel { Children = { _image, _whiten } };
            // Centred, so the card is the letterboxed picture, not the window (as the bare Image was).
            _card = new Panel
            {
                Children = { _glow, _picture },
                HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Center,
            };
            _root = new Panel { Children = { _card } };
            Content = _root;
            Closed += (_, _) => { _lifetimeTimer?.Stop(); _lifetimeTimer = null; picture.Dispose(); };
        }

        /// <summary>
        /// Fade in to <paramref name="alpha"/> over <paramref name="fade"/>, hold until
        /// <paramref name="lifetime"/> after spawn, fade out over <paramref name="fade"/>, close.
        /// Same envelope as WPF's heartbeat: ExpiresAt = spawn + lifetime, ramps inside it.
        /// </summary>
        public void Run(double alpha, TimeSpan fade, TimeSpan lifetime)
        {
            _alpha = alpha;
            _fade = fade;
            _expiresAt = FlashOverlay.Clock.GetTimestamp() + (long)(lifetime.TotalSeconds * FlashOverlay.Clock.TimestampFrequency);
            Fade(0, alpha, fade);
            ScheduleExpiry();
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
                DispatcherTimer.RunOnce(() => { if (Exit != null || Shatter != null) return; X11Overlay.SetOpacity(this, v); if (last) done?.Invoke(); }, span * i / steps + TimeSpan.FromMilliseconds(1));
            }
        }
    }
}
