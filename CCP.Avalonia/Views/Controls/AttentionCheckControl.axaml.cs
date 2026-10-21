using System;
using System.Linq;
using Avalonia;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;
using ConditioningControlPanel.Avalonia.Helpers;

namespace ConditioningControlPanel.Avalonia.Views.Controls
{
    /// <summary>
    /// Reusable visual for the Attention-Check mechanic: hot-pink progress
    /// ring around a glowing dot, lifted from WebcamCalibrationWindow's
    /// calibration target so the visual is instantly recognizable to users
    /// who've completed calibration. The control is intrinsically 84x84 DIPs;
    /// host it in a window at the desired screen position.
    ///
    /// Public surface:
    ///   SetProgress(0..1)  — fills the foreground ring clockwise.
    ///   StartPulse() / StopPulse() — gentle scale-pulse animation, useful
    ///                                 to signal "look here" on first appear.
    ///
    /// <para><b>Nothing on this head constructs this control, matching WPF.</b> Its only caller is
    /// <c>AttentionCheckService.EnsureWindow()</c> (ConditioningControlPanel/Services/AttentionCheckService.cs:339),
    /// and WPF scrapped that mechanic pre-ship: App.xaml.cs constructs the service for BarkService's
    /// OnPass/OnFail wiring and never starts it, so WPF never shows the ring. If WPF revives it,
    /// <c>AttentionCheckParityScanTests</c> fails; then port the service (gaze feed from the head's
    /// WebcamTracker, topmost click-through host, panic stop, Lockdown) and wire this from it.</para>
    /// </summary>
    public partial class AttentionCheckControl : UserControl
    {
        private readonly Ellipse _dotRingFg;
        private readonly ScaleTransform _dotRingScale;
        private readonly Image _dotRingGlow;
        private Bitmap? _glowBitmap;
        private double _progress;
        private BeatLoop? _pulse;
        private TopLevel? _topLevel;

        public AttentionCheckControl()
        {
            AvaloniaXamlLoader.Load(this);

            _dotRingFg = this.FindControl<Ellipse>("DotRingFg")!;
            // The transform is pulled out of the group rather than looked up by name: Avalonia
            // rejects x:Name on a ScaleTransform outright (AVLN2000, only a StyledElement can be
            // named), so the WPF markup's x:Name="DotRingScale" could not be kept.
            _dotRingScale = ((TransformGroup)_dotRingFg.RenderTransform!).Children.OfType<ScaleTransform>().First();
            _dotRingGlow = this.FindControl<Image>("DotRingGlow")!;
            _dotRingGlow.RenderTransform = _dotRingFg.RenderTransform;
        }

        /// <summary>
        /// Sets the foreground-ring fill amount. progress is clamped to
        /// [0, 1]; 0 = empty, 1 = full ring. Implementation mirrors
        /// WebcamCalibrationWindow.UpdateProgressRing — same StrokeDashArray
        /// math so the visual matches calibration exactly.
        /// </summary>
        public void SetProgress(double progress)
        {
            progress = Math.Clamp(progress, 0.0, 1.0);
            double radius = (_dotRingFg.Width - _dotRingFg.StrokeThickness) / 2.0;
            double perimeter = 2.0 * Math.PI * radius;
            double units = perimeter / _dotRingFg.StrokeThickness;
            double visible = progress * units;
            double gap = Math.Max(0.001, units - visible);
            _dotRingFg.StrokeDashArray = new AvaloniaList<double> { visible, gap };
            _progress = progress;
            RefreshGlow();
        }

        // Rasterize only the filled stroke, not a full circular halo. The surface pixels are
        // copied straight into an immutable Avalonia Bitmap before publication; the pulse reuses it.
        private void RefreshGlow()
        {
            // Density is unknown until attach; OnAttachedToVisualTree draws the current progress.
            if (_topLevel == null)
                return;
            const float padding = 14;
            float width = (float)_dotRingFg.Width;
            float extent = width + 2 * padding;
            float density = (float)_topLevel.RenderScaling;
            int pixels = (int)Math.Ceiling(extent * density);
            using var surface = SKSurface.Create(new SKImageInfo(pixels, pixels, SKColorType.Rgba8888, SKAlphaType.Premul));
            surface.Canvas.Clear(SKColors.Transparent);
            surface.Canvas.Scale(density);
            {
                // ponytail: Gaussian three-sigma extent maps WPF BlurRadius=14 to sigma=14/3;
                // exact Windows blur-kernel pixels still need comparison on a Windows renderer.
                using var blur = SKImageFilter.CreateBlur(padding / 3, padding / 3);
                using var paint = new SKPaint
                {
                    IsAntialias = true, Style = SKPaintStyle.Stroke,
                    StrokeWidth = (float)_dotRingFg.StrokeThickness, StrokeCap = SKStrokeCap.Round,
                    Color = new SKColor(255, 105, 180, 179), ImageFilter = blur,
                };
                float inset = padding + paint.StrokeWidth / 2;
                if (_progress == 0)
                    surface.Canvas.DrawPoint(extent - inset, extent / 2, paint);
                else
                    surface.Canvas.DrawArc(new SKRect(inset, inset, extent - inset, extent - inset),
                        0, (float)(_progress * 360), false, paint);
            }
            surface.Canvas.Flush();
            using var pixmap = surface.PeekPixels();
            var next = new Bitmap(PixelFormat.Rgba8888, AlphaFormat.Premul, pixmap.GetPixels(),
                new PixelSize(pixels, pixels), new Vector(96, 96), pixmap.RowBytes);
            var previous = _glowBitmap;
            _glowBitmap = next;
            _dotRingGlow.Source = next;
            previous?.Dispose();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _topLevel = TopLevel.GetTopLevel(this);
            if (_topLevel != null)
                _topLevel.ScalingChanged += OnScalingChanged;
            RefreshGlow();
        }

        // Moving to a monitor with another scale redraws the glow at the new density.
        private void OnScalingChanged(object? sender, EventArgs e) => RefreshGlow();

        public void StartPulse()
        {
            StopPulse();
            // WPF's 420 ms sine AutoReverse pulse, driven by the existing shared 30 Hz clock.
            // ponytail: keep this dormant control dormant; WPF removed its service's UI/start path.
            _pulse ??= new BeatLoop(this, seconds =>
            {
                double scale = 1.0 + 0.18 * BeatLoop.Breath(seconds, 0.42);
                _dotRingScale.ScaleX = scale;
                _dotRingScale.ScaleY = scale;
            });
            _pulse.Start();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            StopPulse();
            if (_topLevel != null)
                _topLevel.ScalingChanged -= OnScalingChanged;
            _topLevel = null;
            _dotRingGlow.Source = null;
            _glowBitmap?.Dispose();
            _glowBitmap = null;
            base.OnDetachedFromVisualTree(e);
        }

        public void StopPulse()
        {
            _pulse?.Stop();
            _dotRingScale.ScaleX = 1.0;
            _dotRingScale.ScaleY = 1.0;
        }
    }
}
