using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Media.Imaging;
using SkiaSharp;
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
    /// <para>One click-through virtual-stage window for each independent wash/hero surface. Windows
    /// remain alive between pictures, clear after fading, and hide after eight quiet seconds.
    /// Refused without the Win32/X11 composited overlay shim. Remote stills require the unported
    /// remote media pool; local animation decoding reuses the spiral's bounded worker path.</para>
    /// </summary>
    internal static class ChaosFlashOverlay
    {
        internal static bool SkipPlatformChecksForTests;
        /// <summary>The one-shot scheduler for the fade steps; tests swap in a stepped clock (P08).</summary>
        internal static Action<Action, TimeSpan> Schedule = (a, t) => DispatcherTimer.RunOnce(a, t);

        private static bool _refused;
        /// <summary>Tests only: forget a platform refusal so it does not leak into later tests (P02).</summary>
        internal static void ResetRefusalForTests() => _refused = false;
        private static readonly Surface Wash = new();
        private static readonly Surface Hero = new();
        private static int _heroPostGen;
        internal static IReadOnlyList<SpiralOverlayWindow> Shown => Wash.Windows;
        internal static IReadOnlyList<SpiralOverlayWindow> HeroShown => Hero.Windows;
        internal static bool IsShowing => Wash.IsShowing;
        internal static bool IsAnimating => Wash.IsAnimating;
        internal static bool IsHeroAnimating => Hero.IsAnimating;
        internal static double Alpha => Wash.Alpha;
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
            var want = PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, MonitorTarget.FollowGlobal, s.DualMonitorEnabled, screens.Count, primary);
            if (want.Length == 0) return;
            var targets = want.Select(i => screens[i].Bounds).ToArray();
            // The pick and the decode run off the UI thread: a pack pick decrypts (WPF :45-58).
            LastShow = Wash.ShowAsync(targets, durationMs, opacity, () =>
            {
                var enabled = AssetFolderExclusion.Enabled(files, root, s);
                return FlashOverlay.NextFlashPath(enabled);
            });
        }

        /// <summary>A wash of one given file (tests and a future caller that picked its own).</summary>
        internal static Task ShowPath(IReadOnlyList<PixelRect> targets, string path, int durationMs, double opacity)
            => LastShow = Wash.ShowAsync(targets, durationMs, opacity, () => path);

        /// <summary>Back Room gif-full: a separate local-only surface, spanning every screen.</summary>
        public static void ShowHero(Visual host, string path, int durationMs, double opacity, bool still, Action? shown = null)
        {
            if (string.IsNullOrEmpty(path) || !System.IO.Path.IsPathFullyQualified(path)) return;
            var gen = _heroPostGen;
            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _heroPostGen || !CanShow) return;
                var targets = ScreenList.Enumerate(host).Select(s => s.Bounds).ToArray();
                LastShow = Hero.ShowAsync(targets, durationMs, opacity, () => path, still, shown);
            });
        }

        internal static Task ShowHeroPath(IReadOnlyList<PixelRect> targets, string path, bool still, Action? shown = null)
            => Hero.ShowAsync(targets, 10000, .1, () =>
                System.IO.Path.IsPathFullyQualified(path) ? path : null, still, shown);

        public static void StopHero()
        {
            ++_heroPostGen;
            if (Dispatcher.UIThread.CheckAccess()) Hero.Idle();
            else Dispatcher.UIThread.Post(Hero.Idle);
        }
        public static void RaiseActive()
        {
            foreach (var window in Wash.Windows) X11Overlay.SetOverrideRedirect(window, Wash.Bounds);
        }
        internal static void NextFrame() => Wash.NextFrame();

        /// <summary>Run teardown, panic and exit also orphan decodes for both surfaces.</summary>
        public static void CloseActive() { ++_heroPostGen; Wash.Close(); Hero.Close(); }

        /// <summary>WPF uses one UniformToFill image over the virtual desktop, including negative origins.</summary>
        internal static PixelRect StageBounds(IReadOnlyList<PixelRect> targets)
        {
            if (targets.Count == 0) return default;
            var left = targets.Min(b => b.X);
            var top = targets.Min(b => b.Y);
            return new PixelRect(left, top, targets.Max(b => b.Right) - left, targets.Max(b => b.Bottom) - top);
        }

        // ponytail: reuse the existing animation decoder; only stills need WPF's larger width budget.
        internal static (List<Bitmap> Frames, TimeSpan Delay) Decode(string path, int stageWidth, bool still)
        {
            using var codec = SKCodec.Create(path);
            if (codec == null) return (new(), TimeSpan.Zero);
            if (!still && codec.FrameCount > 1)
                return SpiralOverlay.Decode(path, ChaosFlashWash.MaxDecodeDim, ChaosFlashWash.MaxFrames);
            var width = Math.Min(codec.Info.Width, Math.Clamp(stageWidth, 640, 2560));
            var height = Math.Max(1, (int)Math.Round(codec.Info.Height * (double)width / codec.Info.Width));
            using var full = SKBitmap.Decode(codec);
            if (full == null) return (new(), TimeSpan.Zero);
            using var scaled = new SKBitmap(new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul));
            full.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear));
            // Bitmap copies the pixels before the mutable worker-owned SKBitmap is disposed.
            return (new() { new Bitmap(global::Avalonia.Platform.PixelFormat.Bgra8888,
                global::Avalonia.Platform.AlphaFormat.Premul, scaled.GetPixels(),
                new PixelSize(width, height), new Vector(96, 96), scaled.RowBytes) }, TimeSpan.Zero);
        }

        private sealed class Surface
        {
            private int _gen, _request;
            internal readonly List<SpiralOverlayWindow> Windows = new();
            private List<Bitmap> _frames = new();
            private int _index;
            private DispatcherTimer? _gifTimer;
            internal PixelRect Bounds { get; private set; }
            internal bool IsShowing => Windows.Count > 0 && _frames.Count > 0;
            internal bool IsAnimating => _gifTimer is not null;
            internal double Alpha { get; private set; }

            internal async Task ShowAsync(IReadOnlyList<PixelRect> targets, int durationMs, double opacity,
                Func<string?> pick, bool still = false, Action? shown = null)
            {
                if (targets.Count == 0) return;
                var request = ++_request;
                var bounds = StageBounds(targets);
                string? path;
                try { path = await Task.Run(pick); }
                catch (Exception ex) { Log.Debug("ChaosFlash: pick failed: {E}", ex.Message); return; }
                if (request != _request || path == null) return;

                var gen = ++_gen;
                ClearImage();
                if (Windows.Count > 0 && Bounds != bounds) CloseWindows();
                Bounds = bounds;
                if (Windows.Count == 0)
                {
                    var w = new SpiralOverlayWindow();
                    // Apply before Show: a full-stage surface must never intercept desktop clicks.
                    var safe = X11Overlay.SetClickThrough(w, true) & X11Overlay.SetOpacity(w, 0)
                        & X11Overlay.SetOverrideRedirect(w, bounds);
                    if (!safe && !SkipPlatformChecksForTests)
                    {
                        _refused = true;
                        w.Close();
                        Close();
                        return;
                    }
                    if (SkipPlatformChecksForTests)
                    {
                        w.Position = bounds.Position;
                        w.Width = bounds.Width;
                        w.Height = bounds.Height;
                    }
                    Windows.Add(w);
                }
                foreach (var w in Windows)
                {
                    X11Overlay.SetOpacity(w, 0);
                    w.Show();
                    X11Overlay.SetOverrideRedirect(w, bounds); // re-stack above video after unhiding
                }
                Alpha = 0;
                var plan = ChaosFlashWash.For(durationMs, opacity);
                foreach (var (at, alpha) in ChaosFlashWash.Keyframes(plan))
                {
                    var last = at == plan.EndMs;
                    Schedule(() =>
                    {
                        if (gen != _gen) return;
                        if (last) { Idle(cancelPending: false); return; }
                        SetAlpha(alpha);
                    }, TimeSpan.FromMilliseconds(at));
                }
                // WPF starts the envelope and acknowledges the window before the still decode.
                try { shown?.Invoke(); }
                catch (Exception ex) { Log.Debug("ChaosFlash: shown callback failed: {E}", ex.Message); }
                var (frames, delay) = await Task.Run(() =>
                {
                    try { return Decode(path, bounds.Width, still); }
                    catch (Exception ex) { Log.Debug("ChaosFlash: decode failed: {E}", ex.Message); return (new List<Bitmap>(), TimeSpan.Zero); }
                });
                if (gen != _gen)
                {
                    foreach (var frame in frames) frame.Dispose();
                    return;
                }
                (_frames, _index) = (frames, 0);
                if (frames.Count == 0) return;
                foreach (var w in Windows) w.Spiral.Source = frames[0];
                if (frames.Count > 1)
                {
                    _gifTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = delay };
                    _gifTimer.Tick += (_, _) => NextFrame();
                    _gifTimer.Start();
                }
            }

            internal void NextFrame()
            {
                if (_frames.Count == 0) return;
                _index = (_index + 1) % _frames.Count;
                foreach (var w in Windows) w.Spiral.Source = _frames[_index];
            }

            private void SetAlpha(double alpha)
            {
                Alpha = alpha;
                foreach (var w in Windows) X11Overlay.SetOpacity(w, alpha);
            }

            internal void Idle() => Idle(cancelPending: true);

            private void Idle(bool cancelPending)
            {
                var gen = ++_gen;
                if (cancelPending) ++_request;
                SetAlpha(0);
                ClearImage();
                // WPF: retain the cleared click-through window, hide only after 8 quiet seconds.
                Schedule(() =>
                {
                    if (gen != _gen) return;
                    foreach (var w in Windows) w.Hide();
                }, TimeSpan.FromSeconds(8));
            }

            private void ClearImage()
            {
                _gifTimer?.Stop();
                _gifTimer = null;
                foreach (var w in Windows) w.Spiral.Source = null;
                foreach (var frame in _frames) frame.Dispose();
                _frames.Clear();
            }

            internal void Close()
            {
                ++_gen;
                ++_request;
                ClearImage();
                SetAlpha(0);
                CloseWindows();
            }

            private void CloseWindows()
            {
                foreach (var w in Windows) w.Close();
                Windows.Clear();
            }
        }
    }
}
