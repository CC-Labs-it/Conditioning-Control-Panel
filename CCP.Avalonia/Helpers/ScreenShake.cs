using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Threading;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Helpers
{
    /// <summary>
    /// Port of WPF <c>Services/UI/ScreenShakeService.cs</c> (App.ScreenShake): a brief jitter on
    /// every visible top-level window's content, the SAME random offset on all of them each 30 ms
    /// tick so the app wobbles as one, up to 28 px at intensity 1. Single-flight: a new shake
    /// restores the previous targets and re-snapshots the window list.
    ///
    /// <para><see cref="Step"/> is the stepped clock (tests call it; the timer only calls it) and
    /// ends the shake by <see cref="Clock"/>'s wall time, as WPF's <c>_endsAt</c> does.</para>
    ///
    /// <para>ponytail: WPF also injects a CSS shake into every visible WebView2; no head WebView is
    /// shaken here (chaos-payout-fallin-d-next).</para>
    /// </summary>
    internal static class ScreenShake
    {
        internal const int TICK_MS = 30;
        internal const double MAX_OFFSET_PX = 28.0;   // at intensity 1.0

        internal static TimeProvider Clock = TimeProvider.System;
        internal static Random Rng = new();
        /// <summary>The windows a shake snapshots; tests point it at their own.</summary>
        internal static Func<IEnumerable<Window>> Windows = () =>
            (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.Windows
            ?? Enumerable.Empty<Window>();

        private static readonly List<(Control Target, TranslateTransform Transform, ITransform? Prior)> _targets = new();
        private static DispatcherTimer? _timer;
        private static DateTimeOffset _endsAt;
        private static double _amplitude;

        internal static bool IsRunning => _timer?.IsEnabled == true;
        internal static int TargetCount => _targets.Count;

        /// <summary>Shake every visible window for <paramref name="durationMs"/> at
        /// <paramref name="intensity"/> (clamped 0..1; 0 does nothing). Safe from any thread.</summary>
        public static void Shake(double intensity, int durationMs)
        {
            intensity = Math.Clamp(intensity, 0, 1);
            if (intensity <= 0 || durationMs <= 0) return;
            if (Dispatcher.UIThread.CheckAccess()) Start(intensity, durationMs);
            else Dispatcher.UIThread.Post(() => Start(intensity, durationMs));
        }

        private static void Start(double intensity, int durationMs)
        {
            try
            {
                Reset();
                foreach (var win in Windows())
                {
                    if (!win.IsVisible || win.Content is not Control root) continue;
                    var t = new TranslateTransform();
                    _targets.Add((root, t, root.RenderTransform));
                    root.RenderTransform = t;
                }
                _amplitude = MAX_OFFSET_PX * intensity;
                _endsAt = Clock.GetUtcNow().AddMilliseconds(durationMs);
                if (_targets.Count == 0) return;
                if (_timer == null)
                {
                    _timer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(TICK_MS) };
                    _timer.Tick += (_, _) => Step();
                }
                _timer.Start();
            }
            catch (Exception ex) { Log.Debug("ScreenShake.Start: {E}", ex.Message); }
        }

        /// <summary>One tick: the same symmetric jitter on every target, or stop at the end time.</summary>
        internal static void Step()
        {
            if (_targets.Count == 0 || Clock.GetUtcNow() >= _endsAt) { Reset(); return; }   // holds no window past its shake
            double dx = (Rng.NextDouble() * 2 - 1) * _amplitude;
            double dy = (Rng.NextDouble() * 2 - 1) * _amplitude;
            foreach (var (_, t, _) in _targets) { t.X = dx; t.Y = dy; }
        }

        /// <summary>Stop and put back each target's own transform if ours is still the one installed.</summary>
        internal static void Reset()
        {
            _timer?.Stop();
            foreach (var (target, t, prior) in _targets)
                if (ReferenceEquals(target.RenderTransform, t)) target.RenderTransform = prior;
            _targets.Clear();
        }
    }
}
