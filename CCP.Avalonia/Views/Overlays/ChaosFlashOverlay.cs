using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Services;                   // AssetFolderExclusion
using ConditioningControlPanel.Services.UI;                // MonitorTarget
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The Chaos "braindrain" wash (WPF ConditioningControlPanel/Chaos/ChaosFlashOverlay.cs): one
    /// random image from the flash pool held over the desktop at a low opacity, fading in, holding and
    /// fading out on the <see cref="ChaosFlashWash"/> curve. GIFs and animated webp loop; stills hold.
    ///
    /// <para>One click-through, override-redirect <see cref="SpiralOverlayWindow"/> per screen the
    /// stage covers (WPF StageBounds: every monitor with DualMonitorEnabled, else the primary), the way
    /// the spiral and pink tint do. The fade is the compositor's (<see cref="X11Overlay.SetOpacity"/>)
    /// on one-shot timers, like FlashOverlayWindow. Refused without X11 click-through and a compositor:
    /// an opaque full-screen picture would be the opposite of a faint wash.</para>
    ///
    /// <para>Not ported: WPF keeps its window alive between washes (a Win32 layered-window render-thread
    /// hang); here the windows close when the fade ends. The Back Room hero (ShowHero/StopHero) and remote
    /// stills have no caller on this head.</para>
    /// </summary>
    internal static class ChaosFlashOverlay
    {
        internal static bool SkipPlatformChecksForTests;
        /// <summary>The one-shot scheduler for the fade steps; tests swap in a stepped clock (P08).</summary>
        internal static Action<Action, TimeSpan> Schedule = (a, t) => DispatcherTimer.RunOnce(a, t);

        private static bool _refused;
        /// <summary>Tests only: forget a platform refusal so it does not leak into later tests (P02).</summary>
        internal static void ResetRefusalForTests() => _refused = false;
        private static int _gen;
        private static readonly List<SpiralOverlayWindow> Windows = new();
        private static List<Bitmap> _frames = new();
        private static int _index;
        private static DispatcherTimer? _gifTimer;

        internal static IReadOnlyList<SpiralOverlayWindow> Shown => Windows;
        internal static bool IsShowing => Windows.Count > 0;
        internal static bool IsAnimating => _gifTimer is not null;
        /// <summary>The alpha last written to the windows.</summary>
        internal static double Alpha { get; private set; }
        internal static Task? LastShow { get; private set; }

        private static bool CanShow =>
            SkipPlatformChecksForTests || (!_refused && X11Overlay.IsAvailable && X11Overlay.IsCompositing);

        /// <summary>WPF Show: wash a random flash-pool image for <paramref name="durationMs"/> at
        /// <paramref name="opacity"/>. Silent no-op with an empty pool or no composited overlay.
        /// <paramref name="host"/> is any attached visual, used to reach the screens.</summary>
        public static void Show(Visual host, int durationMs = ChaosFlashWash.DefaultDurationMs,
            double opacity = ChaosFlashWash.DefaultOpacity)
        {
            if (!CanShow)
            {
                Log.Debug("ChaosFlash: no composited click-through overlay on this platform, so no wash");
                return;
            }
            var s = CoreSettings.Current;
            var files = ChaosImagePool.GetFiles();   // UI thread only
            var root = CorePaths.EffectiveAssets;
            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }
            var want = PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
            if (want.Length == 0) return;
            var targets = want.Select(i => screens[i].Bounds).ToArray();
            var fit = Math.Min(ChaosFlashWash.MaxDecodeDim, targets.Max(b => Math.Max(b.Width, b.Height)));
            var gen = ++_gen;
            // The pick and the decode run off the UI thread: a pack pick decrypts (WPF :45-58).
            LastShow = ShowAsync(gen, targets, durationMs, opacity, () =>
            {
                var enabled = AssetFolderExclusion.Enabled(files, root, s);
                return FlashOverlay.NextFlashPath(enabled);
            }, fit);
        }

        /// <summary>A wash of one given file (tests and a future caller that picked its own).</summary>
        internal static Task ShowPath(IReadOnlyList<PixelRect> targets, string path, int durationMs, double opacity)
            => LastShow = ShowAsync(++_gen, targets, durationMs, opacity, () => path, ChaosFlashWash.MaxDecodeDim);

        private static async Task ShowAsync(int gen, IReadOnlyList<PixelRect> targets, int durationMs, double opacity,
            Func<string?> pick, int fit)
        {
            var (frames, delay) = await Task.Run(() =>
            {
                try { return pick() is { } p ? SpiralOverlay.Decode(p, fit, ChaosFlashWash.MaxFrames) : (new List<Bitmap>(), TimeSpan.Zero); }
                catch (Exception ex) { Log.Debug("ChaosFlash: pick/decode failed: {E}", ex.Message); return (new List<Bitmap>(), TimeSpan.Zero); }
            });
            if (gen != _gen || frames.Count == 0)
            {
                if (frames.Count == 0) Log.Debug("ChaosFlash: no images in pool - nothing to wash");
                foreach (var f in frames) f.Dispose();
                return;
            }

            CloseWindows();   // a new wash swaps the picture (WPF reuses its window for the same)
            (_frames, _index) = (frames, 0);
            foreach (var bounds in targets)
            {
                var w = new SpiralOverlayWindow();
                w.Spiral.Source = frames[0];
                // Before Show, the order FlashOverlay uses: a window that ate clicks would lock the desktop.
                if (!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOpacity(w, 0) | !X11Overlay.SetOverrideRedirect(w, bounds)
                    && !SkipPlatformChecksForTests)
                {
                    Log.Warning("ChaosFlash: the platform refused a click-through topmost overlay window, so no wash");
                    _refused = true;
                    w.Close();
                    CloseActive();
                    return;
                }
                w.Show();
                Windows.Add(w);
            }
            Alpha = 0;
            if (frames.Count > 1)
            {
                _gifTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = delay };
                _gifTimer.Tick += (_, _) => NextFrame();
                _gifTimer.Start();
            }

            var plan = ChaosFlashWash.For(durationMs, opacity);
            foreach (var (at, alpha) in ChaosFlashWash.Keyframes(plan))
            {
                var last = at == plan.EndMs;
                Schedule(() =>
                {
                    if (gen != _gen) return;   // superseded by a newer wash, or closed
                    if (last) { CloseActive(); return; }
                    SetAlpha(alpha);
                }, TimeSpan.FromMilliseconds(at));
            }
        }

        /// <summary>One GIF frame on every window. Allocation-free.</summary>
        internal static void NextFrame()
        {
            if (_frames.Count == 0 || Windows.Count == 0) return;
            _index = (_index + 1) % _frames.Count;
            foreach (var w in Windows) w.Spiral.Source = _frames[_index];
        }

        private static void SetAlpha(double alpha)
        {
            Alpha = alpha;
            foreach (var w in Windows) X11Overlay.SetOpacity(w, alpha);
        }

        /// <summary>WPF CloseActive: take the wash down now (run teardown, panic, exit). Drops any
        /// pending fade step and any decode still in flight.</summary>
        public static void CloseActive()
        {
            _gen++;
            CloseWindows();
        }

        private static void CloseWindows()
        {
            _gifTimer?.Stop();
            _gifTimer = null;
            foreach (var w in Windows)
            {
                try { w.Close(); }
                catch (Exception ex) { Log.Debug("ChaosFlash: close failed: {E}", ex.Message); }
            }
            Windows.Clear();
            foreach (var f in _frames) f.Dispose();
            _frames = new List<Bitmap>();
            Alpha = 0;
        }
    }
}
