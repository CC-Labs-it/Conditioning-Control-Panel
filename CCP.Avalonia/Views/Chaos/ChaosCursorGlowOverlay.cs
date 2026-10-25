// PORTED from ConditioningControlPanel/Chaos/ChaosCursorGlowOverlay.cs (Rabbit Caller's "whistle held"
// halo). Size, gradient, breath and the 16 ms follow come from Core ChaosCursorGlowMath.

using System;
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
    /// A soft pink-gold halo that rides the cursor from the moment the whistle is pressed until the
    /// next click summons the rabbits there. Click-through, never activated, topmost: the click lands
    /// on whatever is underneath, exactly like WPF's WS_EX_TRANSPARENT | WS_EX_NOACTIVATE.
    ///
    /// <para>One static seam like WPF (Arm / Disarm / MoveToPx / CloseActive). The difference: WPF's
    /// cursor poll lives in ChaosModeService's aim timer, which the Avalonia ChaosRunHost does not
    /// have yet (no Rabbit Caller skill), so the overlay polls the pointer itself on the same 16 ms
    /// Render-priority timer, only while armed. Hidden = no timer, no work (P07).</para>
    ///
    /// <para>If the platform cannot make the window click-through, or the compositor refuses
    /// transparency, the halo is not shown: a box under the cursor that eats the summon click (or a
    /// black square) is worse than no halo.</para>
    /// </summary>
    internal sealed class ChaosCursorGlowOverlay : Window
    {
        private static ChaosCursorGlowOverlay? _active;
        private static bool _refused;   // platform said no once: don't churn a window per Arm until teardown
        internal static ChaosCursorGlowOverlay? Active => _active;

        /// <summary>Desktop-wide pointer in physical pixels. Test seam.</summary>
        internal static Func<(PixelPoint At, bool Pressed, bool Escape)?> ReadPointer = X11Pointer.Read;

        /// <summary>Makes a shown window click-through and confirms it is transparent. Test seam.</summary>
        internal static Func<Window, bool> MakeClickThrough = w =>
            X11Overlay.SetClickThrough(w, true) && w.ActualTransparencyLevel != WindowTransparencyLevel.None;

        /// <summary>Breath clock. Test seam (P08).</summary>
        internal static TimeProvider Clock = TimeProvider.System;

        private readonly Ellipse _halo;
        private readonly ScaleTransform _scale = new(1, 1);
        private DispatcherTimer? _timer;
        private long _breathStart;

        internal bool HaloVisible => _halo.IsVisible;
        internal bool Tracking => _timer?.IsEnabled == true;
        internal double HaloScale => _scale.ScaleX;

        /// <summary>Show the halo and start following the cursor. Safe from any thread.</summary>
        public static void Arm() => OnUi(() =>
        {
            if (_active == null)
            {
                if (_refused) return;
                var w = new ChaosCursorGlowOverlay();
                w.Park();
                w.Show();   // no window handle before Show, so click-through comes after
                if (!MakeClickThrough(w))
                {
                    Log.Warning("ChaosCursorGlow: platform cannot make the halo click-through/transparent, so it is not shown");
                    _refused = true;
                    w.Close();
                    return;
                }
                _active = w;
            }
            _active.StartTracking();
        });

        /// <summary>Hide the halo (the whistle was answered or cancelled) and stop tracking.</summary>
        public static void Disarm() => OnUi(() => _active?.StopTracking());

        /// <summary>Centre the halo on a cursor position in physical pixels. UI thread only.</summary>
        public static void MoveToPx(int pxX, int pxY) => _active?.MoveTo(pxX, pxY);

        /// <summary>Instant teardown (run end / panic / shutdown). Idempotent. Forgets a refusal.</summary>
        public static void CloseActive() => OnUi(() =>
        {
            _refused = false;
            var w = _active;
            _active = null;
            w?.Close();
        });

        // Parameterless ctor is also what --render-all constructs: the halo is visible there so the
        // frame shows the gradient. Arm() parks and hides it before the first Show.
        public ChaosCursorGlowOverlay()
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
            Width = Height = ChaosCursorGlowMath.Size;

            var brush = new RadialGradientBrush
            {
                GradientStops =
                {
                    new GradientStop(Color.FromArgb(0, 0xFF, 0xD7, 0x00), 0.18),
                    new GradientStop(Color.FromArgb(150, 0xFF, 0x8F, 0xC8), 0.55),
                    new GradientStop(Color.FromArgb(0, 0xFF, 0x4D, 0xC4), 1.0),
                },
            };
            _halo = new Ellipse
            {
                Width = ChaosCursorGlowMath.Size,
                Height = ChaosCursorGlowMath.Size,
                Fill = brush,
                IsHitTestVisible = false,
                RenderTransformOrigin = RelativePoint.Center,   // WPF sets (0.5, 0.5) explicitly
                RenderTransform = _scale,
            };
            Content = _halo;
            Closed += (_, _) =>
            {
                _timer?.Stop();
                if (ReferenceEquals(_active, this)) _active = null;
            };
        }

        private void Park()
        {
            _halo.IsVisible = false;
            var (x, y) = ChaosCursorGlowMath.ParkedPx(RenderScaling);
            Position = new PixelPoint(x, y);
        }

        private void StartTracking()
        {
            _halo.IsVisible = true;
            _breathStart = Clock.GetTimestamp();
            _timer ??= new DispatcherTimer(ChaosCursorGlowMath.FollowInterval, DispatcherPriority.Render, (_, _) => Tick());
            _timer.Start();
            Tick();
        }

        private void StopTracking()
        {
            _timer?.Stop();
            _halo.IsVisible = false;
        }

        /// <summary>One aim-timer frame: follow the pointer and breathe.</summary>
        internal void Tick()
        {
            if (!_halo.IsVisible) return;
            var s = ChaosCursorGlowMath.PulseScale(Clock.GetElapsedTime(_breathStart));
            _scale.ScaleX = _scale.ScaleY = s;
            try
            {
                if (ReadPointer() is { } p) MoveTo(p.At.X, p.At.Y);
            }
            catch (Exception ex) { Log.Debug("ChaosCursorGlow: pointer read failed: {E}", ex.Message); }
        }

        private void MoveTo(int pxX, int pxY)
        {
            if (!_halo.IsVisible) return;
            var (x, y) = ChaosCursorGlowMath.TopLeftPx(pxX, pxY, RenderScaling);
            Position = new PixelPoint(x, y);
        }

        private static void OnUi(Action a)
        {
            void Run()
            {
                try { a(); }
                catch (Exception ex) { Log.Debug("ChaosCursorGlow: {E}", ex.Message); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Run();
            else Dispatcher.UIThread.Post(Run);
        }
    }
}
