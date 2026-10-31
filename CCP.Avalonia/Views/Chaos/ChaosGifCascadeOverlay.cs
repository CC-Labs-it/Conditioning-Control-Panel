using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls.Fx;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// PORTED from ConditioningControlPanel/Chaos/ChaosGifCascadeOverlay.cs: the full-screen, click-through
    /// "gif rain". One override-redirect window over every screen, kept alive between cascades (hidden
    /// after an 8 s idle grace) and closed only by <see cref="CloseActive"/> (run teardown, panic). The
    /// maths (spawn timing, caps, speeds, growth, despawn) is Core <see cref="GifCascade"/>; this draws one
    /// Image per clip on the window's <see cref="FrameClock"/> (no timer of its own per frame).
    /// Refuses without a compositor (no per-pixel alpha: the full-screen window would be an opaque sheet).
    /// </summary>
    internal sealed class ChaosGifCascadeOverlay : Window
    {
        internal static TimeProvider Clock = TimeProvider.System;
        internal static bool SkipPlatformChecksForTests;
        /// <summary>Tests drive <see cref="Frame"/> themselves; the frame clock stays off (P08).</summary>
        internal static bool ManualFramesForTests;
        internal static bool Refused;
        private static ChaosGifCascadeOverlay? _active;
        private static long _pickGeneration;

        internal static ChaosGifCascadeOverlay? Active => _active;
        internal static Task? LastPick { get; private set; }
        /// <summary>The newest clip decode (tests await it, then run the posted attach).</summary>
        internal static Task? LastDecode { get; private set; }

        /// <summary>WPF IsRaining: a cascade is spawning or clips are still falling.</summary>
        public static bool IsRaining => _active?._cascade.IsRaining == true;

        /// <summary>WPF Show: draws a batch from the enabled flash pool off the UI thread, then rains it.</summary>
        public static void Show(double spawnRatePerSec, double durationSec, double gifSize, double fallSpeed, double opacity, double startScale = 1.0)
        {
            long generation = _pickGeneration;
            int batch = GifCascade.BatchSize(spawnRatePerSec, durationSec);
            var s = CoreSettings.Current;
            LastPick = Task.Run(() => PickFiles(batch, s)).ContinueWith(t => Dispatcher.UIThread.Post(() =>
            {
                if (generation != _pickGeneration) return; // panic or run teardown won
                try { ShowWithFiles(t.IsCompletedSuccessfully ? t.Result : new List<string>(), spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale); }
                catch (Exception ex) { Log.Debug("GifCascade.ShowWithFiles: {E}", ex.Message); }
            }), TaskScheduler.Default);
        }

        /// <summary>WPF GetChaosImagePaths: distinct picks from the enabled images (folder + content packs).
        /// An empty enabled pool rains nothing (#762/#798).</summary>
        internal static List<string> PickFiles(int batch, AppSettings s)
        {
            var root = CorePaths.EffectiveAssets;
            var files = AssetFolderExclusion.Enabled(ChaosImagePool.GetFiles(), root, s);
            var picked = new List<string>();
            for (int i = 0; i < batch; i++)
                if (FlashOverlay.NextFlashPath(files) is { } p && !picked.Contains(p)) picked.Add(p);
            return picked;
        }

        internal static void ShowWithFiles(List<string> files, double spawnRatePerSec, double durationSec,
                                           double gifSize, double fallSpeed, double opacity, double startScale)
        {
            if (files.Count == 0) { Log.Debug("GifCascade: no images in pool - nothing to rain"); return; }
            var w = Create();
            if (w == null) return;
            if (!w.IsVisible) w.Show();   // idles hidden between cascades
            if (!SkipPlatformChecksForTests) X11Overlay.Raise(w);   // WPF ChaosWindowZ.RaiseAboveVideo / ForceTopmost
            w.Restart(files, spawnRatePerSec, durationSec, gifSize, fallSpeed, opacity, startScale);
            Log.Information("GifCascade: raining (pool={N}, spread=[{L:F0}..{R:F0}])",
                files.Count, w._cascade.SpawnLeft, w._cascade.SpawnLeft + w._cascade.SpawnWidth);
        }

        /// <summary>Re-stack the live window above a mandatory video.</summary>
        public static void RaiseActive()
        {
            if (_active != null && !SkipPlatformChecksForTests) X11Overlay.Raise(_active);
        }

        /// <summary>Close any cascade immediately (run teardown, panic). Safe when idle.</summary>
        public static void CloseActive()
        {
            ++_pickGeneration; // a pending pool fetch must not reopen after run teardown or panic
            Refused = false;
            try { _active?.CloseNow(); } catch (Exception ex) { Log.Debug("GifCascade.CloseActive: {E}", ex.Message); }
        }

        private static ChaosGifCascadeOverlay? Create()
        {
            if (_active != null) return _active;
            if (Refused) return null;
            if (!SkipPlatformChecksForTests && (!X11Overlay.IsAvailable || !X11Overlay.IsCompositing))
            {
                Log.Warning("GifCascade: no composited click-through overlay on this platform, so no gif rain");
                Refused = true;
                return null;
            }
            var w = new ChaosGifCascadeOverlay();
            if ((!X11Overlay.SetClickThrough(w, true) | !X11Overlay.SetOverrideRedirect(w, w._bounds)) && !SkipPlatformChecksForTests)
            {
                Log.Warning("GifCascade: the platform refused a click-through topmost window, so no gif rain");
                Refused = true;
                w.Close();
                return null;
            }
            _active = w;
            return w;
        }

        private sealed class Clip
        {
            public Image Image = null!;
            public ScaleTransform Grow = null!;
            public TranslateTransform Move = null!;
            public List<Bitmap>? Frames;
            public TimeSpan Delay;
            public double FrameAcc;
            public int FrameIndex;
        }

        private readonly GifCascade _cascade = new();
        private readonly Dictionary<GifCascade.Faller, Clip> _clips = new();
        private readonly Canvas _canvas = new() { IsHitTestVisible = false };
        private readonly FrameClock _frames;
        private readonly DispatcherTimer _hideGrace = new() { Interval = TimeSpan.FromSeconds(8) };
        private readonly PixelRect _bounds;
        private readonly double _scale = 1;
        private IReadOnlyList<string> _files = Array.Empty<string>();
        private long _last;

        internal GifCascade Cascade => _cascade;
        internal Canvas Surface => _canvas;
        internal bool IsAnimating => _frames.IsEnabled;
        internal TimeSpan FrameInterval => _frames.Interval;
        internal double Scale => _scale;
        internal bool IsHideGraceArmed => _hideGrace.IsEnabled;

        public ChaosGifCascadeOverlay()
        {
            SystemDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Focusable = false;
            IsHitTestVisible = false;
            WindowStartupLocation = WindowStartupLocation.Manual;

            // WPF sizes to the whole virtual screen once and confines only where clips spawn.
            var all = Screens?.All ?? Array.Empty<Screen>();
            // The window's own DesktopScaling, the factor X11Overlay.SetOverrideRedirect sizes it with,
            // so the spawn band and the window agree on mixed-DPI desktops.
            _scale = DesktopScaling > 0 ? DesktopScaling : 1;
            _bounds = all.Count == 0 ? new PixelRect(0, 0, 1280, 720) : all.Select(x => x.Bounds).Aggregate((a, b) => a.Union(b));
            Width = _bounds.Width / _scale;
            Height = _bounds.Height / _scale;
            Content = _canvas;

            // Every composed frame, like WPF CompositionTarget.Rendering; dt is real elapsed time.
            _frames = new FrameClock(this) { Interval = TimeSpan.Zero };
            _frames.Tick += (_, _) => Frame();
            _hideGrace.Tick += (_, _) =>
            {
                _hideGrace.Stop();
                if (!_cascade.IsRaining) { try { Hide(); } catch (Exception ex) { Log.Debug("GifCascade hide: {E}", ex.Message); } }
            };
            _cascade.Spawned += OnSpawned;
            _cascade.Gone += OnGone;

            if (RenderProof.Rendering)   // --render-all: a cascade two seconds in, on the bundled spiral gif
            {
                Restart(new[] { Path.Combine(AppContext.BaseDirectory, "Resources", "spiral_corner.gif") },
                    GifCascade.PayloadSpawnRatePerSec, GifCascade.PayloadDurationSec, GifCascade.PayloadGifSize / 2,
                    GifCascade.PayloadFallSpeed, GifCascade.PayloadOpacity, GifCascade.PayloadStartScale);
                for (int i = 0; i < 120; i++) _cascade.Step(1.0 / 60);
                Paint(0);
                _frames.Stop();
            }
        }

        /// <summary>WPF Restart: replace any clips in flight and start a cascade over the resolved screens.</summary>
        private void Restart(IReadOnlyList<string> files, double rate, double dur, double size, double fall, double opacity, double startScale)
        {
            _hideGrace.Stop();
            _files = files;
            var (left, width) = SpawnBand();
            _cascade.Restart(files.Count, rate, dur, size, fall, opacity, startScale, Height, left, width);
            _last = Clock.GetTimestamp();
            Paint(0);
            if (!ManualFramesForTests) _frames.Start();
        }

        /// <summary>WPF SetSpawnSpread over App.ResolveScreens(MonitorTargetFollowGlobal): DualMonitorEnabled
        /// rains on every screen, otherwise on the global "Show content on" pick (GlobalTargetMonitor).</summary>
        private (double Left, double Width) SpawnBand()
        {
            var all = Screens?.All ?? Array.Empty<Screen>();
            if (all.Count == 0) return (0, Width);
            int primary = Math.Max(0, all.ToList().FindIndex(x => x.IsPrimary));
            var picked = PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, CoreSettings.Current.GlobalTargetMonitor, CoreSettings.Current.DualMonitorEnabled, all.Count, primary)
                .Select(i => ((all[i].Bounds.X - _bounds.X) / _scale, (all[i].Bounds.Right - _bounds.X) / _scale));
            return GifCascade.SpawnBand(picked, 0, Width);
        }

        /// <summary>One frame: real elapsed time since the last (P40), then the clips follow the maths.</summary>
        internal void Frame()
        {
            long now = Clock.GetTimestamp();
            double dt = Clock.GetElapsedTime(_last, now).TotalSeconds;
            _last = now;
            _cascade.Step(dt);
            Paint(dt);
            if (_cascade.IsRaining) return;
            // Drained: idle the frames; keep the empty window up for the grace so a follow-up cascade
            // does not pay a full-screen Show, then hide (WPF _hideGrace).
            _frames.Stop();
            _hideGrace.Stop();
            _hideGrace.Start();
        }

        private void Paint(double dt)
        {
            foreach (var (f, c) in _clips)
            {
                double s = _cascade.ScaleAt(f.Y);
                c.Grow.ScaleX = s;
                c.Grow.ScaleY = s;
                c.Move.Y = f.Y;
                if (c.Frames is { Count: > 1 } frames)
                {
                    c.FrameAcc += dt;
                    double delay = Math.Max(0.02, c.Delay.TotalSeconds);
                    if (c.FrameAcc < delay) continue;
                    c.FrameAcc %= delay;
                    c.FrameIndex = (c.FrameIndex + 1) % frames.Count;
                    c.Image.Source = frames[c.FrameIndex];
                }
            }
        }

        private void OnSpawned(GifCascade.Faller f)
        {
            string path = _files[f.FileIndex];
            bool animatedFormat = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                                  || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase);
            long len = 0;
            if (animatedFormat) { try { len = new FileInfo(path).Length; } catch { } }
            f.Animated = _cascade.CanAnimate(animatedFormat, len);

            double size = _cascade.Size;
            var grow = new ScaleTransform(_cascade.ScaleAt(f.Y), _cascade.ScaleAt(f.Y));
            var move = new TranslateTransform(0, f.Y);
            var img = new Image
            {
                Stretch = Stretch.Uniform,
                Opacity = _cascade.Opacity,
                Width = size,
                RenderTransformOrigin = new RelativePoint(0.5, 0.5, RelativeUnit.Relative),
                RenderTransform = new TransformGroup { Children = { grow, move } },
            };
            Canvas.SetLeft(img, f.CenterX - size / 2);
            Canvas.SetTop(img, 0);
            _canvas.Children.Add(img);
            var clip = new Clip { Image = img, Grow = grow, Move = move };
            _clips[f] = clip;

            // Decoded at display size off the UI thread; the clip falls empty for the few frames it takes.
            bool animate = f.Animated;
            int px = (int)Math.Round(size * _scale);
            if (RenderProof.Rendering) { Attach(f, clip, Decode(path, px, animate)); return; }
            LastDecode = Task.Run(() => Decode(path, px, animate)).ContinueWith(t =>
                Dispatcher.UIThread.Post(() => Attach(f, clip, t.IsCompletedSuccessfully ? t.Result : (new List<Bitmap>(), default))),
                TaskScheduler.Default);
        }

        private static (List<Bitmap> Frames, TimeSpan Delay) Decode(string path, int px, bool animate)
        {
            if (animate) return SpiralOverlay.Decode(path, px);
            try
            {
                using var stream = File.OpenRead(path);
                return (new List<Bitmap> { Bitmap.DecodeToWidth(stream, Math.Max(1, px)) }, default);
            }
            catch (Exception ex)
            {
                Log.Debug("GifCascade decode: {E}", ex.Message);
                return (new List<Bitmap>(), default);
            }
        }

        private void Attach(GifCascade.Faller f, Clip clip, (List<Bitmap> Frames, TimeSpan Delay) decoded)
        {
            if (!_clips.TryGetValue(f, out var live) || !ReferenceEquals(live, clip))
            {
                foreach (var b in decoded.Frames) b.Dispose();   // the clip already fell out
                return;
            }
            if (decoded.Frames.Count == 0) return;
            clip.Frames = decoded.Frames;
            clip.Delay = decoded.Delay;
            clip.Image.Source = decoded.Frames[0];
        }

        private void OnGone(GifCascade.Faller f)
        {
            if (!_clips.Remove(f, out var c)) return;
            c.Image.Source = null;
            _canvas.Children.Remove(c.Image);
            if (c.Frames != null) foreach (var b in c.Frames) b.Dispose();
        }

        private void CloseNow()
        {
            _hideGrace.Stop();
            _frames.Stop();
            _cascade.Clear();
            if (ReferenceEquals(_active, this)) _active = null;
            try { Close(); } catch (Exception ex) { Log.Debug("GifCascade close: {E}", ex.Message); }
        }
    }
}
