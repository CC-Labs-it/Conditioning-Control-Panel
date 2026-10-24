using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Flash;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// One burst of flash images, the head half of WPF's <c>FlashService.TriggerFlashOnce()</c>
    /// with default arguments (ConditioningControlPanel/Services/Flash/FlashService.cs:696):
    /// SimultaneousImages pictures from the enabled pool, each on a random targeted monitor,
    /// sized and placed by <see cref="FlashPlacement"/> (the math WPF itself calls), staggered
    /// 300 ms, faded by the Fade/Opacity sliders, living FlashDuration + 1 s.
    ///
    /// <para>Every window is override-redirect and click-through (X11Overlay; on Windows its
    /// Win32Overlay half). Where that is not available - a native Wayland backend, headless -
    /// nothing is shown and it is logged
    /// once: a topmost picture that swallows clicks is worse than no picture.</para>
    ///
    /// <para>The ambient rhythm is Core <c>CoreFlash</c>, which calls this.</para>
    ///
    /// <para>ponytail: not here yet, each a later branch - audio + ducking (lifetime then follows the sound's length), Shatter,
    /// the lucky glow pulse and toast, Natasha's favourite, the avatar's FlashClicked giggle (no phrase categories on this head),
    /// the Pendulum motion style (Drift and Bounce is below), GIF animation (first frame only), content-pack and remote pools, avatar pre-announce.</para>
    /// </summary>
    internal static class FlashOverlay
    {
        // WPF OverlayService:3053-3079 (#1041): the attention layer is re-raised while a topmost browser is fullscreen.
        internal static void RaiseForBrowserFullscreen()
        {
            foreach (var w in Active) X11Overlay.Raise(w.Window);
        }

        // FlashService.ResolveFlashCap on the default (compositor) path.
        private const int MaxConcurrent = 30;
        private const int StaggerMs = 300;

        internal static readonly List<(FlashOverlayWindow Window, PixelRect Rect)> Active = new();
        private static readonly Random Rng = new();
        // #627: one shuffled walk of the folder (WPF FlashService DiskBag), so every image comes up
        // before any repeats. Not thread-safe: LoadPictures draws under the bag's own lock.
        internal static readonly ShuffleBag<string> DiskBag = new(p => p, Rng);

        /// <summary>WPF FlashService _packImageList + PackBag + _tempPackFiles (:173-191).</summary>
        internal static readonly ConditioningControlPanel.Services.PackMediaPool PackImages =
            new(ConditioningControlPanel.Services.ContentPackStore.ImageType);
        private static bool _busy, _warnedUnavailable, _warnedEmpty, _closed;
        private static int _generation;

        /// <summary>Bumped by every CloseAll; a burst scheduled under an older one never spawns.</summary>
        internal static int Generation => _generation;

        /// <summary>A burst is still spawning (WPF <c>_isBusy</c>); the ambient tick skips.</summary>
        public static bool IsBusy => _busy;

        /// <summary>Fire one burst. Any attached visual works as <paramref name="host"/>; it only
        /// reaches <c>Screens</c>. Like WPF's _isBusy, a second press is ignored from the click
        /// until the burst's last flash has spawned. The overrides are WPF TriggerFlashOnce's
        /// (amount, duration ms, size %); null keeps the user's setting.</summary>
        public static async void TriggerOnce(Visual host, int? amount = null, int? durationMs = null, int? size = null, bool ambient = false)
        {
            if (_busy) return;
            if (!X11Overlay.IsAvailable)
            {
                if (!_warnedUnavailable) Log.Warning("Flash: this platform cannot show click-through overlays, so flashes are skipped");
                _warnedUnavailable = true;
                return;
            }

            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;
            _busy = true;
            var generation = _generation;
            var scheduled = false;
            try
            {
                var s = CoreSettings.Current;
                var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
                var targets = PinkFilterOverlay.ResolveScreenIndices(Services.UI.MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary);
                var occupied = Active.Select(a => a.Rect).ToList();
                var flashes = await Task.Run(() => LoadPictures(BurstCount(amount, s, Rng), screens, targets, s, occupied, size));
                if (flashes.Count == 0)
                {
                    if (!_warnedEmpty) Log.Warning("Flash: no images found in {Path}", ImagesPath());
                    _warnedEmpty = true;
                    return;
                }

                var clickable = s.FlashClickable;
                var lifetime = Lifetime(s, ambient, durationMs);
                var cap = Cap(s, ambient);
                var fade = TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent);
                var alpha = Math.Clamp(s.FlashOpacity / 100.0, 0, 1);

                // WPF waits 1 s after FlashAboutToDisplay so the avatar's line lands first, then
                // staggers 300 ms per image.
                Schedule(flashes, 1000, StaggerMs, generation, cap, alpha, fade, lifetime, clickable, ambient, hydraGeneration: 0,
                    done: () => _busy = false);
                scheduled = true;
            }
            catch (Exception ex) { Log.Error(ex, "Flash: burst failed"); }
            finally { if (!scheduled) _busy = false; }
        }

        /// <summary>Spawn each placed picture on its own dispatcher timer: Task.Delay continuations
        /// were measured landing 1.7 s late here (thread-pool timer), dispatcher timers stayed on time.
        /// A spawn the platform refuses stops the rest; CloseAll (a newer generation) drops them.</summary>
        private static void Schedule(List<(Bitmap Bitmap, PixelRect Rect, string Path, Screen Screen)> flashes, int firstMs, int staggerMs,
            int generation, int cap, double alpha, TimeSpan fade, TimeSpan lifetime, bool clickable, bool ambient, int hydraGeneration, Action? done,
            FlashMotionStyle? inheritMotion = null)
        {
            var refused = false;
            for (var i = 0; i < flashes.Count; i++)
            {
                var (bmp, rect, path, screen) = flashes[i];
                var last = i == flashes.Count - 1;
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        if (refused || _closed || generation != _generation || Active.Count >= cap) { bmp.Dispose(); return; }
                        refused = !Show(bmp, rect, path, screen, alpha, fade, lifetime, clickable, ambient, hydraGeneration, inheritMotion);
                    }
                    catch (Exception ex) { refused = true; Log.Error(ex, "Flash: spawn failed"); }
                    finally { if (last) done?.Invoke(); }
                }, TimeSpan.FromMilliseconds(firstMs + i * staggerMs));
            }
        }

        /// <summary>One flash on screen plus what WPF SpawnFlashWindow pays for it: the lucky roll
        /// before the show (FlashService.cs:1851) and its glow (1881-1967).</summary>
        internal static bool Show(Bitmap bmp, PixelRect rect, string path, Screen screen, double alpha, TimeSpan fade,
            TimeSpan lifetime, bool clickable, bool ambient, int hydraGeneration, FlashMotionStyle? inheritMotion = null)
        {
            var s = CoreSettings.Current;
            var multiplier = FlashFxRules.RollLucky(SkillTreeRules.HasSkill(s, FlashFxRules.LuckySkillId), hydraGeneration, remixMirror: false, Rng);
            var glow = FlashFxRules.ResolveGlow(s.FlashGlowEnabled, ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.CurrentTier,
                multiplier > 1, SkillTreeRules.GetSparkleBoostTier(s), natasha: false);
            var w = new FlashOverlayWindow(bmp)
            {
                HydraGeneration = hydraGeneration, Ambient = ambient, Lifetime = lifetime,
                SpawnedAt = Clock.GetTimestamp(), SpawnScreen = screen, InheritMotion = inheritMotion,
            };
            if (!Spawn(w, rect, screen.Bounds, screen.Scaling, glow, alpha, fade, lifetime, clickable)) return false;
            Paid(path, s, hydraGeneration, multiplier);
            return true;
        }

        /// <summary>What a shown flash pays (WPF FlashService.cs:1608, 1834-1877, 2165-2172): session
        /// log and history, the lucky chime, XP (4: this head plays no flash sound; a hydra child with
        /// independent timing pays less; x10 lucky), and the achievement + quest tick, which a linked
        /// hydra child skips only when timing is independent.</summary>
        internal static void Paid(string path, AppSettings s, int hydraGeneration, int multiplier)
        {
            App.Sessions?.SessionLog.RecordImages(new[] { path });
            App.MediaHistory?.RecordImages(new[] { path });   // WPF MediaHistoryService.OnFlashDisplayed
            if (multiplier > 1 && !s.SuppressPerkNotifications) LuckyChime(s);   // WPF :4441 PerkNotificationsSuppressed
            CoreProgression.AddXP(FlashFxRules.Xp(soundPlaying: false, s.HydraLinkedTiming, hydraGeneration, multiplier), "Flash");
            if (s.HydraLinkedTiming || hydraGeneration == 0)
            {
                App.Achievements?.TrackFlashImage();   // WPF AchievementService.cs:388
                App.Quests?.TrackFlashImage();         // ...which credits the quest (:399)
            }
        }

        /// <summary>The chime a lucky flash plays (tests swap it).</summary>
        internal static Action<AppSettings> LuckyChime = s =>
        {
            // WPF PlayLuckyFlashSound (FlashService.cs:4439): a random chime at master^1.5 x 0.35.
            try
            {
                var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "sounds", $"chime{Rng.Next(1, 4)}.mp3");
                if (File.Exists(file)) CoreAudio.PlayOneShot(file, (float)Math.Pow(s.MasterVolume / 100f, 1.5) * 0.35f, "lucky-flash");
                Log.Information("Lucky Flash! 10x XP!");
            }
            catch (Exception ex) { Log.Debug("Lucky flash chime failed: {E}", ex.Message); }
        };

        /// <summary>
        /// Where one flash goes on one screen, in physical pixels. WPF does the math in that
        /// monitor's DIPs (bounds / DPI scale, truncated) - the 40% fit, the 50-DIP edge padding,
        /// the #770 avoid-center box, 10 re-rolls away from &gt;30% overlap - and so does this, then
        /// scales the answer back onto the screen.
        /// </summary>
        internal static PixelRect Place(PixelRect screen, double scaling, int imgW, int imgH, AppSettings s, Random rng,
            IEnumerable<PixelRect> occupied, int? size = null)
        {
            var k = scaling > 0 ? scaling : 1.0;
            int monW = (int)(screen.Width / k), monH = (int)(screen.Height / k);
            var (w, h) = FlashPlacement.FitSize(imgW, imgH, monW, monH, (size ?? s.ImageScale) / 100.0);

            PixelRect ToPx(int x, int y) => new(screen.X + (int)(x * k), screen.Y + (int)(y * k), (int)(w * k), (int)(h * k));
            var others = occupied.Select(r => (r.X, r.Y, r.Width, r.Height)).ToList();

            PixelRect Pick()
            {
                var (x, y) = FlashPlacement.PickSpawnPoint(0, 0, monW, monH, w, h, s.FlashAvoidCenter,
                    s.FlashCenterExclusionPercent, rng, out var fellBack);
                if (fellBack) Log.Debug("Flash avoid-center: no legal band for {W}x{H} on {MW}x{MH}; unconstrained", w, h, monW, monH);
                return ToPx(x, y);
            }

            var rect = Pick();
            for (var attempt = 0; attempt < 10 && FlashPlacement.IsOverlapping(rect.X, rect.Y, rect.Width, rect.Height, others); attempt++)
                rect = Pick();
            return rect;
        }

        /// <summary>The platform half of <see cref="Spawn"/> (tests swap it; headless has no X11).</summary>
        internal static Func<FlashOverlayWindow, PixelRect, bool, bool> Present = (w, rect, clickable) =>
            X11Overlay.SetClickThrough(w, !clickable) && X11Overlay.SetOpacity(w, 0) && X11Overlay.SetOverrideRedirect(w, rect, passive: clickable);

        /// <summary>The pre-map recipe proved by --overlay-check, with every attribute requested
        /// BEFORE SetOverrideRedirect so its XSync covers them: the window maps already
        /// click-through and at alpha 0, never as one opaque or clickable frame. False (window
        /// closed) when the platform refuses.</summary>
        private static bool Spawn(FlashOverlayWindow w, PixelRect rect, PixelRect screen, double scaling, FlashGlowLook glow,
            double alpha, TimeSpan fade, TimeSpan lifetime, bool clickable)
        {
            var pad = w.ApplyGlow(glow, scaling);
            var winRect = new PixelRect(rect.X - pad, rect.Y - pad, rect.Width + 2 * pad, rect.Height + 2 * pad);
            // Click on (WPF FlashService.cs:1765): the window keeps its input and never takes focus
            // (passive); off: exactly the click-through recipe above.
            if (clickable) w.MakeClickable(() => Pop(w));
            if (!Present(w, winRect, clickable))
            {
                if (!_warnedUnavailable) Log.Warning("Flash: the platform refused a click-through topmost overlay window; flashes skipped");
                _warnedUnavailable = true;
                w.Close();
                return false;
            }
            var entry = (w, rect);
            w.Closed += (_, _) => Active.RemoveAll(e => e.Window == w);
            Active.Add(entry);
            w.Show();
            w.Run(alpha, fade, lifetime);
            var motion = BuildMotion(rect, screen, CoreSettings.Current, Rng, w.InheritMotion);
            w.MotionStyle = motion?.Style ?? FlashMotionStyle.Still;
            if (motion != null) StartDrift(w, motion);
            return true;
        }

        // ---- Flashes v2 motion: Drift and Bounce (WPF FlashService.SpawnLayerVisual + FlashLayer tick) ----

        /// <summary>Stepped clock for the drift tick (tests swap it).</summary>
        internal static TimeProvider Clock = TimeProvider.System;
        internal static readonly List<(Window Window, FlashMotionState Motion)> Drifting = new();
        private static DispatcherTimer? _driftTimer;
        private static long _lastDriftTick;

        /// <summary>
        /// The motion one flash plays, or null for Still. WPF ResolveMotionStyle + FlashMotion.Create
        /// + the #1265 speed (FlashDriftSpeed x the rolled velocity), in screen pixels over the spawn
        /// monitor, like WPF's world px. Ownership is <see cref="PrizeOwnership"/>; this head has no
        /// pendulum yet, so a Pendulum pick (synced profile) plays Still and Mix rolls Still or Drift.
        /// </summary>
        internal static FlashMotionState? BuildMotion(PixelRect rect, PixelRect screen, AppSettings s, Random rng, FlashMotionStyle? inherit = null)
        {
            // WPF ResolveMotionStyle: a hydra child takes its parent's motion kind (inheritMotion).
            var picked = inherit ?? s.FlashMotionStyle;
            if (picked == FlashMotionStyle.Still) return null;
            var style = FlashMotion.Resolve(picked, PrizeOwnership.IsGranted(PrizeOwnership.FlashDriftBounce),
                ownsPendulum: false, s.MotionLevel, rng);
            if (style != FlashMotionStyle.DriftBounce) return null;
            var m = FlashMotion.Create(style, rect.X, rect.Y, rect.Width, rect.Height,
                screen.X, screen.Y, screen.Width, screen.Height, s.MotionLevel, rng);
            if (m.Style != FlashMotionStyle.DriftBounce) return null;
            m.Vx *= s.FlashDriftSpeed;
            m.Vy *= s.FlashDriftSpeed;
            return m;
        }

        /// <summary>Moves <paramref name="w"/> with <paramref name="m"/> on the one shared tick, which
        /// runs only while some flash drifts: closing the last (expiry, Stop, panic via CloseAll)
        /// stops it.</summary>
        internal static void StartDrift(Window w, FlashMotionState m)
        {
            var entry = (w, m);
            Drifting.Add(entry);
            w.Closed += (_, _) =>
            {
                Drifting.Remove(entry);
                if (Drifting.Count == 0) { _driftTimer?.Stop(); _driftTimer = null; }
            };
            if (_driftTimer != null) return;
            _lastDriftTick = Clock.GetTimestamp();
            _driftTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => DriftTick());
            _driftTimer.Start();
        }

        /// <summary>True while the shared drift tick is running.</summary>
        internal static bool DriftRunning => _driftTimer != null;

        /// <summary>One frame: real elapsed time since the last (P40), FlashMotion.Step, and a window
        /// move only when the position changed - a move, never a re-render.</summary>
        internal static void DriftTick()
        {
            var now = Clock.GetTimestamp();
            var dt = Clock.GetElapsedTime(_lastDriftTick, now).TotalSeconds;
            _lastDriftTick = now;
            foreach (var (w, m) in Drifting)
                if (w is not FlashOverlayWindow { Exit: not null } && FlashMotion.Step(m, dt))   // a leaving flash holds still
                {
                    var p = new PixelPoint((int)Math.Round(m.X), (int)Math.Round(m.Y));
                    var pad = (w as FlashOverlayWindow)?.GlowPadPx ?? 0;   // the glow card sits round the picture
                    w.Position = new PixelPoint(p.X - pad, p.Y - pad);
                    // The overlap check of later bursts reads the LIVE rect, as WPF reads MotionState X/Y.
                    for (var i = 0; i < Active.Count; i++)   // a loop, not FindIndex: no closure per frame
                        if (Active[i].Window == w) Active[i] = (Active[i].Window, new PixelRect(p, Active[i].Rect.Size));
                }
        }

        /// <summary>Close every flash on screen and drop the spawns still queued. <paramref name="final"/>
        /// (shell closing) refuses every later burst; the tray's Stop everything passes false so a
        /// re-enabled flash can show again.</summary>
        public static void CloseAll(bool final = true)
        {
            _generation++;
            _closed |= final;
            foreach (var (w, _) in Active.ToList()) w.Close();
            foreach (var w in Exiting.ToList()) w.Close();
        }

        /// <summary>The burst a Core provider fires (tests swap it).</summary>
        internal static Action<Visual, bool> Fire = (host, ambient) => TriggerOnce(host, ambient: ambient);

        /// <summary>Seeds CoreFlash at startup. The scheduler tick is ambient; ShowProvider (remote
        /// trigger_flash, voice, autonomy) is point-fired like WPF TriggerFlashOnce, so it never stays
        /// until popped (FlashService.cs:1567). A full screen of staying flashes skips the tick.</summary>
        internal static void SeedCore(Func<Visual?> host, Action noteUsed)
        {
            void Show(bool ambient)
            {
                if (host() is not { } h) return;
                Fire(h, ambient);
                noteUsed();
            }
            CoreFlash.IsBusyProvider = () => IsBusy || StayScreenFull(CoreSettings.Current);
            CoreFlash.ShowProvider = () => Show(ambient: false);
            CoreFlash.AmbientShowProvider = () => Show(ambient: true);
        }

        // ---- Click to pop, stay until popped (WPF FlashService.OnFlashClicked / BuildExit / FlashStayRule) ----

        /// <summary>Popped flashes still playing their leave animation; off <see cref="Active"/> like WPF,
        /// but panic and Stop still close them.</summary>
        internal static readonly List<FlashOverlayWindow> Exiting = new();
        private static FlashExitStyle? _lastExitStyle;

        /// <summary>WPF FlashService.cs:84-92 StayUntilPopped: no solid host on this head, so Click alone
        /// decides MouseClickable; only the ambient tick is not point-fired.</summary>
        internal static bool Stays(AppSettings s, bool ambient) =>
            FlashStayRule.Applies(s.FlashStayUntilPopped, s.FlashClickable, pointFired: !ambient);

        /// <summary>WPF FlashService.cs:1557-1568: FlashDuration + 1 s (or the caller's override + 1 s),
        /// and the 10-minute safety lifetime for a flash that stays until popped.</summary>
        internal static TimeSpan Lifetime(AppSettings s, bool ambient, int? durationMs) => Stays(s, ambient)
            ? TimeSpan.FromMilliseconds(FlashStayRule.SafetyLifetimeMs)
            : TimeSpan.FromMilliseconds((durationMs ?? s.FlashDuration * 1000) + 1000);

        /// <summary>WPF FlashService.cs:1692: the shared-host cap (30), 40 while flashes stay.</summary>
        internal static int Cap(AppSettings s, bool ambient) => FlashStayRule.Cap(MaxConcurrent, Stays(s, ambient), sharedHost: true);

        /// <summary>WPF FlashService.cs:696-706: with stay on and the screen full, the ambient tick
        /// skips whole (picture and whisper) and waits for a pop.</summary>
        internal static bool StayScreenFull(AppSettings s) =>
            FlashStayRule.ScreenFull(Stays(s, ambient: true), Active.Count, Cap(s, ambient: true));

        /// <summary>WPF OnFlashClicked: off the active list (cap, overlap), then the leave animation
        /// (BuildExit, FlashService.cs:2334) or the plain cut for None.</summary>
        internal static void Pop(FlashOverlayWindow w)
        {
            if (w.Exit != null || !Active.Any(e => e.Window == w)) return;
            Active.RemoveAll(e => e.Window == w);
            _ = CoreHaptics.Service?.FlashClickVibeAsync();   // WPF FlashService.cs:2945
            var s = CoreSettings.Current;
            // WPF OnFlashClicked hydra (FlashService.cs:2956-2970): Corruption mode, up to two more.
            var n = FlashFxRules.HydraSpawnCount(s.CorruptionMode, s.HydraLimit, isRemix: false, fromGaze: false, w.HydraGeneration, Active.Count);
            if (n > 0) Multiply(w, n, ChildLifetime(w, s), w.HydraGeneration + 1);
            if (FlashExit.Pick(s.FlashExitStyle, _lastExitStyle, Rng) is not { } style) { w.Close(); return; }
            _lastExitStyle = style;
            Exiting.Add(w);
            w.Closed += (_, _) => Exiting.Remove(w);
            w.BeginExit(FlashExit.Begin(style, s.MotionLevel, Rng.Next()));
        }

        /// <summary>Spawns hydra children of a popped flash (tests swap it): parent, count, lifetime, generation.</summary>
        internal static Action<FlashOverlayWindow, int, TimeSpan, int> Multiply = SpawnChildren;

        /// <summary>WPF TriggerMultiplication + ShowImages: linked timing inherits what the parent had
        /// left (at least 1 s), independent a fresh full lifetime; children of an ambient flash still
        /// stay until popped.</summary>
        internal static TimeSpan ChildLifetime(FlashOverlayWindow parent, AppSettings s)
        {
            if (Stays(s, parent.Ambient)) return TimeSpan.FromMilliseconds(FlashStayRule.SafetyLifetimeMs);
            var remaining = (parent.Lifetime - Clock.GetElapsedTime(parent.SpawnedAt)).TotalMilliseconds;
            return TimeSpan.FromMilliseconds(FlashFxRules.HydraChildLifetimeMs(s.HydraLinkedTiming, (int)parent.Lifetime.TotalMilliseconds, remaining));
        }

        /// <summary>WPF TriggerMultiplication: the children stay on the parent's screen, play no
        /// sound and spawn 100 ms apart; a CloseAll in between drops them.</summary>
        private static async void SpawnChildren(FlashOverlayWindow parent, int count, TimeSpan lifetime, int hydraGeneration)
        {
            if (_closed || parent.SpawnScreen is not { } screen) return;
            var generation = _generation;
            try
            {
                var s = CoreSettings.Current;
                var occupied = Active.Select(a => a.Rect).ToList();
                var flashes = await Task.Run(() => LoadPictures(count, new[] { screen }, new[] { 0 }, s, occupied, null));
                if (generation != _generation) { foreach (var f in flashes) f.Bitmap.Dispose(); return; }
                Schedule(flashes, 0, 100, generation, Cap(s, parent.Ambient), Math.Clamp(s.FlashOpacity / 100.0, 0, 1),
                    TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent), lifetime, s.FlashClickable,
                    parent.Ambient, hydraGeneration, done: null, parent.MotionStyle);
            }
            catch (Exception ex) { Log.Error(ex, "Flash: hydra spawn failed"); }
        }

        /// <summary>Images in one burst (WPF TriggerFlashOnce, #658): the caller's override, else
        /// the flat count or a roll between Fewest and Images, rolled once per burst.</summary>
        internal static int BurstCount(int? amount, AppSettings s, Random rng) =>
            amount ?? AppSettings.RollFlashImageCount(s.SimultaneousImagesRandom, s.SimultaneousImagesMin, s.SimultaneousImages, rng);

        /// <summary>The next file of the shuffled walk; the caller re-lists the folder per burst.</summary>
        internal static string? NextPath(IReadOnlyList<string> files)
        {
            lock (DiskBag) return DiskBag.TryNext(files, out var p) ? p : null;
        }

        /// <summary>WPF GetNextImages (FlashService.cs:3553-3583): active content-pack images are the second
        /// local source, weighted by count; a pack pick decrypts to a temp file kept on PackImages'
        /// record, and a failed decrypt falls back to the folder.</summary>
        internal static string? NextFlashPath(IReadOnlyList<string> files) => PackImages.TryDraw(files.Count) ?? NextPath(files);

        private static string ImagesPath() => Path.Combine(CorePaths.EffectiveAssets, "images");

        /// <summary>
        /// Up to <paramref name="count"/> placed pictures, dealt from <see cref="DiskBag"/> over the enabled
        /// images (FlashService.GetNextImages / GetMediaFiles), re-drawing past unreadable files the
        /// way LoadImagesUntilAsync does (at most max(count*5, 20) tries). Placement happens first,
        /// from the header size alone, so each picture is decoded AT its display size like WPF's
        /// decode-at-display-size - never a full-resolution source held per window.
        /// </summary>
        private static List<(Bitmap Bitmap, PixelRect Rect, string Path, Screen Screen)> LoadPictures(int count, IReadOnlyList<Screen> screens,
            int[] targets, AppSettings s, List<PixelRect> occupied, int? size)
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            var result = new List<(Bitmap, PixelRect, string, Screen)>(count);
            if (targets.Length == 0) return result;

            var files = Directory.Exists(dir)
                ? AssetFolderExclusion.Enabled(Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                    .Where(f => FlashPlacement.ImageExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                    .ToList(), root, s)
                : new List<string>();

            for (var tries = Math.Max(count * 5, 20); result.Count < count && tries > 0; tries--)
            {
                if (NextFlashPath(files) is not { } path) break;
                try
                {
                    SkiaSharp.SKImageInfo info;
                    using (var codec = SkiaSharp.SKCodec.Create(path))
                    {
                        if (codec is null) continue;
                        info = codec.Info;
                    }
                    var screen = screens[targets[Rng.Next(targets.Length)]];
                    var rect = Place(screen.Bounds, screen.Scaling, info.Width, info.Height, s, Rng, occupied, size);
                    using var stream = File.OpenRead(path);
                    result.Add((Bitmap.DecodeToWidth(stream, rect.Width), rect, path, screen));
                    occupied.Add(rect);
                }
                catch (Exception ex) { Log.Debug("Flash: could not decode {Path}: {E}", path, ex.Message); }
            }
            return result;
        }
    }
}
