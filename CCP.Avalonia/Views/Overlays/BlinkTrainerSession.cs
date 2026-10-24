using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Localization;
using ConditioningControlPanel.Models;                 // SeasonFeatureKeys
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;               // MonitorTarget
using ConditioningControlPanel.Services.Webcam;
using LibVLCSharp.Shared;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// A Blink Trainer session: WPF <c>BlinkTrainerService</c> (Services/BlinkTrainerService.cs). One
    /// transparent, topmost, click-through overlay per screen shows a random pool asset and swaps it on
    /// every <see cref="WebcamTracker.OnBlink"/>; auto-stops after the configured duration.
    /// Same refusals as <see cref="PinkFilterOverlay"/>: no click-through or no transparency = no overlay.
    /// GIF and animated webp tiles loop (WPF AnimatedWebp.AttachAnimation) on SpiralOverlay's SkiaSharp
    /// decoder, off the UI thread; the first frame shows until the frames are ready.
    /// ponytail: mix mode prefers same-aspect images only among those already shown (WPF warms the
    /// aspect cache in the background); the explicit "Tracking monitor" pick is not offered on this
    /// head, so placement is always the app-wide convention (wire-shell-blink-trainer-b-next).
    /// </summary>
    internal static class BlinkTrainerSession
    {
        internal sealed class OverlayWindow : Window
        {
            public readonly Panel Host = new() { ClipToBounds = true };

            public OverlayWindow()
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
                Title = "BlinkTrainerOverlay";
                Width = 640;
                Height = 360;
                Content = Host;
            }

            public void PlaceOn(Screen screen)
            {
                var b = screen.Bounds;
                var scale = screen.Scaling > 0 ? screen.Scaling : 1.0;
                Position = new PixelPoint(b.X, b.Y);
                Width = b.Width / scale;
                Height = b.Height / scale;
            }

            public double Aspect => Width > 0 && Height > 0 ? Width / Height : 16.0 / 9.0;
        }

        private static readonly List<OverlayWindow> Overlays = new();
        private static readonly Dictionary<string, double> Aspects = new(StringComparer.OrdinalIgnoreCase);
        private static readonly List<Bitmap> Shown = new();
        private static BlinkTrainerAssetPool? _pool;
        private static string? _last;
        private static DispatcherTimer? _duration;
        private static MediaPlayer? _player;
        private static Media? _media;
        private static VlcFrameSink? _sink;
        private static Window? _owner;

        /// <summary>Builds and shows the overlays; tests replace it (headless has no X11 to accept them).</summary>
        internal static Func<Visual, List<OverlayWindow>> CreateOverlays = DefaultCreateOverlays;

        public static bool IsRunning { get; private set; }
        public static string LastError { get; private set; } = "";
        public static DateTime? StartedAt { get; private set; }
        public static TimeSpan? Duration { get; private set; }
        internal static IReadOnlyList<OverlayWindow> Windows => Overlays;
        /// <summary>Tests shorten it to prove the auto-stop.</summary>
        internal static DispatcherTimer? DurationTimer => _duration;

        public static TimeSpan Remaining
        {
            get
            {
                if (!IsRunning || StartedAt == null || Duration == null) return TimeSpan.Zero;
                var rem = Duration.Value - (DateTime.UtcNow - StartedAt.Value);
                return rem < TimeSpan.Zero ? TimeSpan.Zero : rem;
            }
        }

        /// <summary>Fires when IsRunning flips (and after a refused start), on the UI thread.</summary>
        public static event Action? StateChanged;

        /// <summary>WPF Start: the same checks in the same order, the same error keys.</summary>
        public static bool Start(Visual host)
        {
            if (IsRunning) return true;
            try
            {
                var s = CoreSettings.Current;
                var pool = BlinkTrainerAssetPool.Build(s.BlinkTrainerFolders, s.BlinkTrainerIncludeVideos);
                if (pool.IsEmpty)
                    return Fail(s.BlinkTrainerFolders.Count == 0 ? Loc.Get("blink_trainer_error_no_folders") : Loc.Get("blink_trainer_error_no_assets"));
                if (!WebcamConsent.IsCurrent(s)) return Fail(Loc.Get("blink_trainer_error_no_consent"));
                // WPF #743: never start the engine from here; the caller brings the tracker up off the UI thread.
                if (!WebcamTracker.Instance.IsRunning) return Fail(Loc.Get("blink_trainer_error_webcam_not_running"));

                Overlays.AddRange(CreateOverlays(host));
                if (Overlays.Count == 0) { Cleanup(); return Fail(Loc.Get("blink_trainer_error_overlay_create")); }
                var opacity = Math.Clamp(s.BlinkTrainerOpacity, 1, 100) / 100.0;
                foreach (var ov in Overlays) ov.Host.Opacity = opacity;

                _pool = pool;
                _last = null;
                // WPF LabTab.cs:1000: Closing += Stop - Closing, not Closed, so a close cancelled to the tray still stops it.
                _owner = TopLevel.GetTopLevel(host) as Window;
                if (_owner != null) _owner.Closing += OnOwnerClosing;
                WebcamTracker.Instance.OnBlink += HandleBlink;
                Duration = TimeSpan.FromMinutes(Math.Clamp(s.BlinkTrainerDurationMinutes, 1, 180));
                StartedAt = DateTime.UtcNow;
                _duration = new DispatcherTimer { Interval = Duration.Value };
                _duration.Tick += (_, _) => { Log.Information("BlinkTrainer: duration elapsed — auto-stopping"); Stop(); };
                _duration.Start();
                IsRunning = true;
                // WPF SeasonRecapService.TrackFeature(BlinkTrainer): the e_bl day-log event.
                try { SeasonFeatureCredit.Track(CoreSettings.Current, SeasonFeatureKeys.BlinkTrainer, App.FeatureDayLog, DateTime.UtcNow); }
                catch (Exception ex) { Log.Debug("blink season credit: {E}", ex.Message); }
                LastError = "";
                Log.Information("BlinkTrainer: started — pool={Count} assets, duration={Mins}m, opacity={Opacity}%, screens={Screens}",
                    pool.Paths.Count, s.BlinkTrainerDurationMinutes, s.BlinkTrainerOpacity, Overlays.Count);
                ShowRandom();
                Raise();
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "BlinkTrainer: start failed");
                Cleanup();
                return Fail(Loc.Get("blink_trainer_error_overlay_create"));
            }
        }

        private static bool Fail(string error)
        {
            LastError = error;
            Raise();
            return false;
        }

        /// <summary>Bumped by every Stop, so a start queued behind the panic-shortcut bind can tell it was cancelled.</summary>
        internal static int Generation { get; private set; }

        public static void Stop()
        {
            Generation++;
            if (!IsRunning && Overlays.Count == 0) return;
            Cleanup();
            IsRunning = false;
            StartedAt = null;
            Duration = null;
            Log.Debug("BlinkTrainer: stopped");
            Raise();
        }

        private static void Raise()
        {
            try { StateChanged?.Invoke(); } catch (Exception ex) { Log.Warning(ex, "BlinkTrainer StateChanged handler threw"); }
        }

        private static void OnOwnerClosing(object? sender, WindowClosingEventArgs e) => Stop();

        private static void Cleanup()
        {
            if (_owner != null) { _owner.Closing -= OnOwnerClosing; _owner = null; }
            WebcamTracker.Instance.OnBlink -= HandleBlink;
            _duration?.Stop();
            _duration = null;
            StopVideo();
            foreach (var ov in Overlays)
            {
                ov.Host.Children.Clear();
                try { ov.Close(); } catch (Exception ex) { Log.Debug("BlinkTrainer: overlay close failed: {E}", ex.Message); }
            }
            Overlays.Clear();
            DisposeShown();
            Aspects.Clear();
            _pool = null;
            _last = null;
        }

        /// <summary>WPF HandleBlink: swap, haptic pulse, quest credit.</summary>
        internal static void HandleBlink()
        {
            if (!IsRunning) return;
            ShowRandom();
            // WPF TriggerBlinkHaptic: off the UI thread, never blocking blink handling; gates itself.
            if (CoreHaptics.Service is { } haptics)
                _ = System.Threading.Tasks.Task.Run(async () =>
                {
                    try { await haptics.BlinkPulseAsync(); }
                    catch (Exception ex) { Log.Debug(ex, "BlinkTrainer: blink haptic failed"); }
                });
            try { App.Quests?.TrackBlinkTrainerBlink(); } catch (Exception ex) { Log.Debug("blink quest credit: {E}", ex.Message); }
        }

        private static void ShowRandom()
        {
            if (_pool == null || Overlays.Count == 0) return;
            var path = _pool.PickRandom(_last);
            if (path == null) return;
            _last = path;
            try
            {
                if (BlinkTrainerAssetPool.IsVideo(path)) { ShowVideo(path); return; }
                StopVideo();
                var mix = CoreSettings.Current.BlinkTrainerMixImages;
                var lead = Load(path);
                if (lead == null) return;
                foreach (var ov in Overlays) ov.Host.Children.Clear();
                DisposeShown();
                Shown.Add(lead);
                foreach (var ov in Overlays) Fill(ov, path, lead, mix);
            }
            catch (Exception ex) { Log.Warning(ex, "BlinkTrainer: failed to apply asset {Path}", path); }
        }

        /// <summary>WPF ApplyAsset / ApplyAssetMixed: one Uniform tile when aspects match, else a
        /// UniformToFill grid (Core <see cref="BlinkTrainerState.TileGrid"/>) of the lead, or in mix
        /// mode of other images of the same aspect bucket.</summary>
        private static void Fill(OverlayWindow ov, string leadPath, Bitmap lead, bool mix)
        {
            var aspect = lead.Size.Height > 0 ? lead.Size.Width / lead.Size.Height : 1.0;
            var (cols, rows) = BlinkTrainerState.TileGrid(aspect, ov.Aspect);
            var tileDim = TileDecodeDim(ov, cols, rows);
            if (cols == 1 && rows == 1) { ov.Host.Children.Add(Tile(leadPath, lead, Stretch.Uniform, tileDim)); return; }

            var grid = new UniformGrid { Columns = cols, Rows = rows };
            var extras = mix ? MixCandidates(leadPath, aspect) : new List<string>();
            for (int i = 0; i < cols * rows; i++)
            {
                var bmp = lead;
                var path = leadPath;
                if (i > 0 && extras.Count > 0 && Load(extras[(i - 1) % extras.Count]) is { } extra)
                {
                    path = extras[(i - 1) % extras.Count];
                    bmp = extra;
                    Shown.Add(extra);
                }
                grid.Children.Add(Tile(path, bmp, Stretch.UniformToFill, tileDim));
            }
            ov.Host.Children.Add(grid);
        }

        private static List<string> MixCandidates(string leadPath, double leadAspect)
        {
            var images = _pool!.Paths.Where(p => !BlinkTrainerAssetPool.IsVideo(p) && !string.Equals(p, leadPath, StringComparison.OrdinalIgnoreCase)).ToList();
            var bucket = images.Where(p => Aspects.TryGetValue(p, out var a) && Bucket(a) == Bucket(leadAspect)).ToList();
            var list = bucket.Count >= 2 ? bucket : images;   // WPF GetCompatibleImages fallback
            return list.OrderBy(_ => Random.Shared.Next()).ToList();
        }

        private static int Bucket(double aspect) => aspect < 0.85 ? 0 : aspect > 1.15 ? 2 : 1;

        private static Bitmap? Load(string path)
        {
            try
            {
                var bmp = new Bitmap(path);
                if (bmp.Size.Height > 0) Aspects[path] = bmp.Size.Width / bmp.Size.Height;
                return bmp;
            }
            catch (Exception ex) { Log.Warning(ex, "BlinkTrainer: bitmap load failed for {Path}", path); return null; }
        }

        private static void DisposeShown()
        {
            StopAnimations();
            foreach (var b in Shown.Distinct()) b.Dispose();
            Shown.Clear();
        }

        // ---- GIF / animated webp (WPF BuildTile -> AnimatedWebp.AttachAnimation) ----------------

        private sealed class Animation
        {
            public readonly List<Image> Tiles = new();
            public List<Bitmap>? Frames;
            public DispatcherTimer? Timer;
            public int Index;
        }

        private static readonly Dictionary<string, Animation> Animations = new(StringComparer.OrdinalIgnoreCase);
        private static int _swap;   // bumped by every swap and stop, so a late decode lands nowhere

        /// <summary>The last decode started; tests await it instead of sleeping.</summary>
        internal static System.Threading.Tasks.Task AnimationDecode { get; private set; } = System.Threading.Tasks.Task.CompletedTask;

        /// <summary>Tests: how many tiles are looping.</summary>
        internal static int AnimatedTiles => Animations.Values.Where(a => a.Timer != null).Sum(a => a.Tiles.Count);

        private static bool MayAnimate(string path)
        {
            var ext = System.IO.Path.GetExtension(path);
            return ext.Equals(".gif", StringComparison.OrdinalIgnoreCase) || ext.Equals(".webp", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>One tile; a .gif / .webp tile starts on its still and joins its path's loop.</summary>
        private static Image Tile(string path, Bitmap still, Stretch stretch, int tileDim)
        {
            var img = new Image { Source = still, Stretch = stretch };
            if (!MayAnimate(path)) return img;
            if (!Animations.TryGetValue(path, out var anim))
            {
                Animations[path] = anim = new Animation();
                // WPF GifDecodeDim: a GIF decodes at no more than 400 px; a webp at its cell size.
                var dim = path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? Math.Min(tileDim, 400) : tileDim;
                AnimationDecode = DecodeThenLoop(path, anim, dim, _swap);
            }
            anim.Tiles.Add(img);
            return img;
        }

        private static async System.Threading.Tasks.Task DecodeThenLoop(string path, Animation anim, int dim, int swap)
        {
            var (frames, delay) = await System.Threading.Tasks.Task.Run(() => SpiralOverlay.Decode(path, dim));
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // A still webp decodes to one frame and keeps its still; a swap since the start drops it.
                if (swap != _swap || frames.Count < 2) { foreach (var f in frames) f.Dispose(); return; }
                anim.Frames = frames;
                foreach (var t in anim.Tiles) t.Source = frames[0];
                anim.Timer = new DispatcherTimer { Interval = delay };
                anim.Timer.Tick += (_, _) => Step(anim);
                anim.Timer.Start();
            });
        }

        private static void Step(Animation anim)
        {
            if (anim.Frames is not { Count: > 1 } frames) return;
            anim.Index = (anim.Index + 1) % frames.Count;
            foreach (var t in anim.Tiles) t.Source = frames[anim.Index];
        }

        /// <summary>Tests: one frame of every loop, as the timer would.</summary>
        internal static void StepAnimations() { foreach (var a in Animations.Values) Step(a); }

        private static void StopAnimations()
        {
            _swap++;
            foreach (var a in Animations.Values)
            {
                a.Timer?.Stop();
                foreach (var t in a.Tiles) t.Source = null;
                foreach (var f in a.Frames ?? new List<Bitmap>()) f.Dispose();
            }
            Animations.Clear();
        }

        /// <summary>WPF TileDecodeDim: the overlay's cell size, clamped to [64, 1280].</summary>
        private static int TileDecodeDim(OverlayWindow ov, int cols, int rows)
        {
            var w = ov.Width > 0 ? ov.Width : 1920;
            var h = ov.Height > 0 ? ov.Height : 1080;
            return (int)Math.Clamp(Math.Max(w / Math.Max(1, cols), h / Math.Max(1, rows)), 64, 1280);
        }

        /// <summary>WPF ApplyVideoToAllOverlays: one muted decoder, looped, every screen shows its frames.</summary>
        private static void ShowVideo(string path)
        {
            var vlc = LibVlcAudio.Shared;
            if (vlc == null) { Log.Warning("BlinkTrainer: LibVLC unavailable - cannot play {Path}", path); return; }
            StopVideo();
            foreach (var ov in Overlays) ov.Host.Children.Clear();
            DisposeShown();
            var images = Overlays.Select(ov => { var i = new Image { Stretch = Stretch.UniformToFill }; ov.Host.Children.Add(i); return i; }).ToList();
            _player = new MediaPlayer(vlc) { EnableHardwareDecoding = true, Mute = true };
            _sink = new VlcFrameSink(_player, () => _media, bmp => images.ForEach(i => i.Source = bmp), () => images.ForEach(i => i.InvalidateVisual()));
            _media = new Media(vlc, path, FromType.FromPath);
            _media.AddOption(":no-audio");
            _media.AddOption(":input-repeat=65535");   // WPF MediaEnded -> Position 0 -> Play
            _player.Play(_media);
        }

        private static void StopVideo()
        {
            if (_player != null)
            {
                try { _player.Stop(); } catch (Exception ex) { Diag.Swallowed(ex); }   // joins the decoder thread
                try { _player.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            }
            _player = null;
            try { _media?.Dispose(); } catch (Exception ex) { Diag.Swallowed(ex); }
            _media = null;
            _sink?.Free();
            _sink = null;
        }

        /// <summary>WPF GazeContentScreenPolicy: explicit display wins, Primary follows global placement.
        /// Each window must accept click-through and transparency or none show.</summary>
        internal static System.Collections.Generic.IEnumerable<int> ResolveTrackingScreens(
            System.Collections.Generic.IReadOnlyList<string?> names, string? picked, bool dual, int primary)
        {
            if (names.Count == 0) return Array.Empty<int>();
            if (!string.IsNullOrEmpty(picked) && !string.Equals(picked, "Primary", StringComparison.OrdinalIgnoreCase))
            {
                for (int i = 0; i < names.Count; i++)
                    if (string.Equals(names[i], picked, StringComparison.OrdinalIgnoreCase)) return new[] { i };
                // WPF App.GetWebcamCalibrationScreen falls back to Primary when a named display disappears.
                return new[] { primary >= 0 ? primary : 0 };
            }
            return PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, MonitorTarget.FollowGlobal, dual, names.Count, primary);
        }
        private static List<OverlayWindow> DefaultCreateOverlays(Visual host)
        {
            var made = new List<OverlayWindow>();
            if (!X11Overlay.IsAvailable) { Log.Warning("BlinkTrainer: no X11 display, so no click-through overlay on this platform"); return made; }
            var screens = ScreenList.Enumerate(host);
            var primary = -1;
            for (var i = 0; i < screens.Count; i++) if (screens[i].IsPrimary) { primary = i; break; }
            foreach (var i in ResolveTrackingScreens(screens.Select(s => s.DisplayName).ToArray(), CoreSettings.Current.WebcamCalibrationScreen, CoreSettings.Current.DualMonitorEnabled, primary))
            {
                var w = new OverlayWindow();
                w.PlaceOn(screens[i]);
                w.Show();
                made.Add(w);
                if (!X11Overlay.SetClickThrough(w, true) || w.ActualTransparencyLevel == WindowTransparencyLevel.None)
                {
                    Log.Warning("BlinkTrainer: overlay refused (no click-through or no transparency) - a topmost full-screen window that swallows clicks or paints opaque would lock the desktop");
                    foreach (var m in made) m.Close();
                    made.Clear();
                    break;
                }
            }
            return made;
        }
    }
}
