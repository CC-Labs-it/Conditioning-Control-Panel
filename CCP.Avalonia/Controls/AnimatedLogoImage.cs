using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Helpers;
using ConditioningControlPanel.Controls;
using Serilog;
using SkiaSharp;

namespace ConditioningControlPanel.Avalonia.Controls
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Controls/AnimatedLogoImage.cs: the dashboard's animated CCP
    /// logo, drawn by the shared <see cref="DashboardLogoRenderer"/>. WPF ticks a 30 fps DispatcherTimer
    /// (:16); here the frames ride the window's shared 30 fps beat (<see cref="BeatLoop"/>, P82), which
    /// parks while the logo is hidden or the window minimised, and each frame is drawn off the UI thread
    /// (one in flight; a slow frame skips beats instead of holding the dispatcher). No Effect, no Animation.
    /// A mod pack's own logo (<see cref="SetArtwork"/> with a bitmap) is drawn still, as in WPF :59.
    /// </summary>
    internal sealed class AnimatedLogoImage : Control
    {
        internal const string ArtworkUri = "avares://CCP.Avalonia/Resources/branding/ccp-logo.jpg";

        /// <summary>WPF :29: the logo lives at rest on this drive; hover eases it to 1.</summary>
        internal const double IdleFloor = .35;

        /// <summary>WPF :32: renderer drive for a hover energy of 0 (rest) to 1 (full hover).</summary>
        internal static double Drive(double energy) => IdleFloor + (1 - IdleFloor) * Math.Clamp(energy, 0, 1);

        /// <summary>WPF :35: one turn per 12 s at rest, three times that on hover.</summary>
        internal static double PhaseRate(double energy) => Math.Tau / 12 * (1 + 2 * Math.Clamp(energy, 0, 1));

        private BeatLoop? _loop;
        private Task? _render;
        private SKBitmap? _back;
        private DashboardLogoRenderer? _renderer;
        private WriteableBitmap? _frame;
        private Bitmap? _source;
        private double _last, _phase, _energy;
        private bool _ambientAllowed, _failed, _attached;

        public AnimatedLogoImage() => SetArtwork(null);

        /// <summary>The built-in logo animates; a mod's own logo art does not (WPF IsAnimatedArtwork).</summary>
        public bool IsAnimatedArtwork { get; private set; }

        /// <summary>True while the beat actually ticks (not parked, not stopped). Test seam.</summary>
        internal bool IsAnimating => _loop?.IsTicking == true;

        /// <summary>WPF :45: the chrome gate (window focused, ambient motion allowed).</summary>
        public bool AmbientAllowed
        {
            get => _ambientAllowed;
            set { if (_ambientAllowed == value) return; _ambientAllowed = value; Refresh(); }
        }

        /// <summary>WPF :59 SetArtwork: null (no mod override) is the built-in animated logo.</summary>
        public void SetArtwork(Bitmap? modOverride)
        {
            Stop(); _failed = false; _phase = 0;
            IsAnimatedArtwork = modOverride == null;
            try { _source = modOverride ?? new Bitmap(AssetLoader.Open(new Uri(ArtworkUri))); }
            catch (Exception ex) { _source = null; Log.Debug("logo artwork: {E}", ex.Message); }
            Refresh();
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _attached = true;
            Refresh();
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnDetachedFromVisualTree(e);
            _attached = false;
            Stop();
            var (renderer, back) = (_renderer, _back); (_renderer, _back) = (null, null);
            void Free() { renderer?.Dispose(); back?.Dispose(); }
            // A frame still drawing off-thread owns the renderer and buffer until it lands.
            if (_render is { } pending) pending.ContinueWith(_ => Free(), TaskScheduler.Default);
            else Free();
        }

        private void Refresh()
        {
            if (!_attached || !AmbientAllowed || !IsAnimatedArtwork || _failed) { Stop(); return; }
            if (_loop?.IsRunning == true) return;
            _last = 0;
            _loop = new BeatLoop(this, Step);
            _loop.Start();
        }

        private void Stop()
        {
            _loop?.Stop(); _loop = null;
            _energy = 0; _frame?.Dispose(); _frame = null;
            InvalidateVisual();
        }

        /// <summary>WPF Tick (:94), on the beat: ease the hover energy, advance the phase, draw.</summary>
        private void Step(double now)
        {
            try
            {
                double dt = Math.Clamp(now - _last, 0, .1);
                _last = now;
                double target = IsPointerOver ? 1 : 0;
                _energy += (target - _energy) * (1 - Math.Exp(-dt / (target > _energy ? .18 : .48)));
                _phase = (_phase + dt * PhaseRate(_energy)) % Math.Tau;
                if (dt <= 0 && _frame != null) return;

                if (_render != null) return;   // one frame in flight: this beat only moves the clock

                if (_renderer == null)
                {
                    using var stream = AssetLoader.Open(new Uri(ArtworkUri));
                    _renderer = new DashboardLogoRenderer(stream);
                }
                double scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1;
                int size = Math.Clamp((int)Math.Ceiling(Math.Min(Bounds.Width, Bounds.Height) * scale), 128, 512);
                var (renderer, phase, drive) = (_renderer, _phase, Drive(_energy));
                // The blur-heavy draw runs off the UI thread (WPF drew it inline in Tick, :113): a slow
                // frame never holds the dispatcher, and only a finished bitmap crosses back (P82 rule 6).
                // One back buffer, reused while the size holds (WPF keeps one surface, :106); only the UI
                // thread swaps it, and never while a frame is in flight.
                if (_back?.Width != size) { _back?.Dispose(); _back = new SKBitmap(new SKImageInfo(size, size, SKColorType.Bgra8888, SKAlphaType.Premul)); }
                var back = _back;
                _render = Task.Run(() =>
                {
                    using (var canvas = new SKCanvas(back)) renderer.Draw(canvas, size, size, phase, drive);
                    return back;
                }).ContinueWith(t => Dispatcher.UIThread.Post(() => Publish(t)), TaskScheduler.Default);
            }
            catch (Exception ex) { _failed = true; Stop(); Log.Debug("logo animation fallback: {E}", ex.Message); }
        }

        /// <summary>UI thread: copy the finished frame into the bitmap this control draws.</summary>
        private void Publish(Task<SKBitmap> drawn)
        {
            _render = null;
            if (drawn.IsFaulted) { _failed = true; Stop(); Log.Debug("logo animation fallback: {E}", drawn.Exception?.GetBaseException().Message); return; }
            var bitmap = drawn.Result;   // the reused back buffer: copied, not disposed
            if (_loop == null || !ReferenceEquals(bitmap, _back)) return;   // stopped or detached while it drew
            int size = bitmap.Width;
            if (_frame == null || _frame.PixelSize.Width != size)
            {
                _frame?.Dispose();
                _frame = new WriteableBitmap(new PixelSize(size, size), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            }
            using (var fb = _frame.Lock())
            using (var pixels = bitmap.PeekPixels())
                pixels.ReadPixels(bitmap.Info, fb.Address, fb.RowBytes);
            InvalidateVisual();
        }

        /// <summary>The frame drawing off-thread, if any; it lands with a UI-thread post. Test seam.</summary>
        internal Task? PendingFrame => _render;

        /// <summary>Stretch="Uniform" (the WPF Image this replaces): the artwork's own aspect.</summary>
        private Size Natural => _source?.Size is { Width: > 0, Height: > 0 } s ? s : new Size(1024, 1024);

        protected override Size MeasureOverride(Size availableSize)
        {
            var n = Natural;
            double k = Math.Min(availableSize.Width / n.Width, availableSize.Height / n.Height);
            if (double.IsInfinity(k)) k = 128 / n.Width;
            return new Size(n.Width * k, n.Height * k);
        }

        public override void Render(DrawingContext context)
        {
            IImage? image = (IImage?)_frame ?? _source;
            if (image == null) return;
            var n = Natural;
            double k = Math.Min(Bounds.Width / n.Width, Bounds.Height / n.Height);
            var rect = new Rect((Bounds.Width - n.Width * k) / 2, (Bounds.Height - n.Height * k) / 2, n.Width * k, n.Height * k);
            if (!IsAnimatedArtwork) { context.DrawImage(image, rect); return; }
            // WPF OnRender (:126): the built-in logo is clipped to its 83/1024 rounded frame.
            using (context.PushClip(new RoundedRect(rect, rect.Width * 83 / 1024)))
                context.DrawImage(image, rect);
        }
    }
}
