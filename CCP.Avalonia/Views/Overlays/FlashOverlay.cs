using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
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
using ConditioningControlPanel.Services.Companion;
using Serilog;
using SkiaSharp;

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
    /// <para>ponytail: not here yet, each a later branch - the lucky glow pulse and toast, Natasha's favourite, the avatar's FlashClicked giggle (no phrase categories on this head),
    /// the Pendulum motion style (Drift and Bounce is below), remote pools, avatar pre-announce.</para>
    /// </summary>
    internal static class FlashOverlay
    {
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
        private static Action? _stopSound;
        private static TaskCompletionSource<TimeSpan?>? _voiceLength;
        private static TaskCompletionSource<bool>? _voiceStart;
        private static bool _soundPlayingForCurrentFlash;
        private static readonly ShuffleBag<string> VoiceBag = new(p => p, Rng);
        internal static Func<TimeSpan, Task> UnduckDelay = Task.Delay;
        internal static Action<string> ShowVoice = path => Views.AvatarTube.AvatarTubeWindow.Live?.ShowFlashVoice(path);
        internal static TimeProvider VoiceClock = TimeProvider.System;
        internal static TimeSpan VoiceMetadataTimeout = TimeSpan.FromMinutes(5);
        internal static long WhisperBusyUntil;
        private static int _voiceAwaitingLength;
        internal static bool IsWhisperPlaying => Volatile.Read(ref _voiceAwaitingLength) > 0 ||
            VoiceClock.GetUtcNow().UtcTicks < Interlocked.Read(ref WhisperBusyUntil);

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
            _soundPlayingForCurrentFlash = false;
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

                // WPF FlashService.cs:992-1031, 1535-1568: the voice starts after the
                // one-second announcement and the image lifetime follows its length.
                var announcementDelay = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                DispatcherTimer.RunOnce(() => announcementDelay.TrySetResult(), TimeSpan.FromSeconds(1));
                await announcementDelay.Task;
                if (_closed || generation != _generation) { foreach (var f in flashes) DisposeFrames(f.Frames); return; }
                scheduled = true; await ShowBurst(flashes, s, generation, durationMs, ambient, NextVoice(s));
            }
            catch (Exception ex) { Log.Error(ex, "Flash: burst failed"); }
            finally { if (!scheduled && generation == _generation) _busy = false; }
        }

        // WPF ShowImages starts the first image with the sound, before staggered images.
        // A bounded provisional lifetime is corrected for both shown and queued windows.
        internal static async Task ShowBurst(List<(List<Bitmap> Frames, TimeSpan Delay, PixelRect Rect, string Path, Screen Screen)> flashes,
            AppSettings s, int generation, int? durationMs, bool ambient, string? voicePath)
        {
            var windows = new List<FlashOverlayWindow>(); var life = Lifetime(s, ambient, durationMs); var scheduled = false;
            void Start() { Schedule(flashes, 0, StaggerMs, generation, Cap(s, ambient), Math.Clamp(s.FlashOpacity / 100.0, 0, 1),
                    TimeSpan.FromSeconds(s.FadeDuration * FlashPlacement.FadeSecondsPerPercent), life, s.FlashClickable,
                    ambient, 0, () => { if (generation == _generation) _busy = false; },
                    lifetimeNow: () => life, spawned: w => windows.Add(w)); scheduled = true; }
            try {
                var length = await StartVoice(s, voicePath, generation,
                    () => { life = Lifetime(s, ambient, 5000); Start(); },
                    duration => { life = Lifetime(s, ambient, (int)duration.TotalMilliseconds); foreach (var w in windows) w.UpdateLifetime(life); });
                if (_closed || generation != _generation) { if (!scheduled) foreach (var f in flashes) DisposeFrames(f.Frames); return; }
                if (!scheduled) { life = Lifetime(s, ambient, length.HasValue ? (int)length.Value.TotalMilliseconds : durationMs); Start(); }
            } finally { if (!scheduled && generation == _generation) _busy = false; }
        }

        // WPF BuildVoiceLinePool / GetNextSound (FlashService.cs:4410-4465). The shared
        // catalogue includes both content roots and phrase-library exclusions; add recursive
        // category folders from the active mod, also respecting the asset and phrase exclusions.
        internal static List<string> VoicePool(AppSettings s, IEnumerable<string> library, IEnumerable<string> extra)
        {
            var pool = library.ToList();
            var seen = new HashSet<string>(pool.Select(p => Path.GetFileName(p)!), StringComparer.OrdinalIgnoreCase);
            foreach (var file in AssetFolderExclusion.Enabled(extra.ToList(), CorePaths.EffectiveAssets, s))
            {
                var ext = Path.GetExtension(file);
                if (!ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".wav", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".ogg", StringComparison.OrdinalIgnoreCase)) continue;
                var id = "VoiceLine:" + Path.GetFileNameWithoutExtension(file);
                if (s.RemovedPhraseIds.Contains(id) || s.DisabledPhraseIds.Contains(id) || !seen.Add(Path.GetFileName(file))) continue;
                pool.Add(file);
            }
            return pool;
        }

        private static string? NextVoice(AppSettings s)
        {
            try
            {
                var library = CompanionVoiceLines.GetRows(s).Where(r => r.IsEnabled && r.AudioFilePath != null).Select(r => r.AudioFilePath!);
                var folder = CompanionVoiceLines.ResolveFolder().Absolute;
                var extra = Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories) : Enumerable.Empty<string>();
                var pool = VoicePool(s, library, extra);
                return VoiceBag.TryNext(pool, out var path) ? path : null;
            }
            catch (Exception ex) { Log.Debug("Flash: voice pool failed: {E}", ex.Message); return null; }
        }

        // WPF FlashService.cs:1535-1557, 4679-4717. Audio starts once per burst, never
        // for hydra children. Stop and panic stop playback, but must not cancel the unduck.
        internal static async Task<TimeSpan?> StartVoice(AppSettings s, string? path, int generation,
            Action? onReady = null, Action<TimeSpan>? onLength = null)
        {
            if (!s.FlashAudioEnabled || s.MasterVolume <= 0 || s.AvatarMuted || s.CompanionVoiceLinesMuted
                || string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
            _voiceStart?.TrySetResult(false); _voiceLength?.TrySetResult(null); _stopSound?.Invoke();
            var began = _voiceStart = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var lengthKnown = _voiceLength = new TaskCompletionSource<TimeSpan?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var master = s.MasterVolume / 100f;
            var gain = CompanionVoiceGain.ApplyLevel(Math.Max(0.05f, (float)Math.Pow(master, 1.5)) * CompanionVoiceGain.BarkTrim, s.CompanionVoiceLevel);
            // Zero voice level still plays silently and keeps its audio-linked length, as WPF.
            var stop = _stopSound = CoreAudio.PlayStoppable(path, Math.Max(gain, 0.000001f), "flash-voice",
                length => { began.TrySetResult(true); if (length > TimeSpan.Zero) lengthKnown.TrySetResult(length); },
                () => { began.TrySetResult(false); lengthKnown.TrySetResult(null); });
            bool playing;
            try { playing = await began.Task.WaitAsync(TimeSpan.FromSeconds(3)); }
            catch (TimeoutException) { _stopSound?.Invoke(); _stopSound = null; playing = false; }
            if (ReferenceEquals(_voiceStart, began)) _voiceStart = null;
            if (generation != _generation || _closed) { if (ReferenceEquals(_voiceLength, lengthKnown)) _voiceLength = null; return null; }

            // WPF failure still marks the whisper, shows the filename, ducks and pays XP (FlashService.cs:1537-1557, 4719-4726).
            var beganAt = VoiceClock.GetUtcNow();
            Interlocked.Increment(ref _voiceAwaitingLength);
            _soundPlayingForCurrentFlash = true;
            ShowVoice(path);
            long duckGeneration = 0;
            var ducked = s.AudioDuckingEnabled;
            if (ducked) { CoreAudio.Duck(s.DuckingLevel); duckGeneration = CoreAudio.DuckGeneration; }
            onReady?.Invoke(); TimeSpan? duration = null;
            try { if (playing) duration = await lengthKnown.Task.WaitAsync(VoiceMetadataTimeout); } catch (TimeoutException) { stop(); }
            if (ReferenceEquals(_voiceLength, lengthKnown)) _voiceLength = null;
            duration ??= playing && VoiceClock.GetUtcNow() - beganAt > TimeSpan.FromSeconds(5) ? VoiceClock.GetUtcNow() - beganAt : TimeSpan.FromSeconds(5); onLength?.Invoke(duration.Value);
            var remaining = duration.Value - (VoiceClock.GetUtcNow() - beganAt);
            if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;
            var until = (VoiceClock.GetUtcNow() + remaining + TimeSpan.FromMilliseconds(250)).UtcTicks;
            long previous;
            do { previous = Interlocked.Read(ref WhisperBusyUntil); }
            while (until > previous && Interlocked.CompareExchange(ref WhisperBusyUntil, until, previous) != previous);
            Interlocked.Decrement(ref _voiceAwaitingLength);
            if (ducked) _ = ReleaseVoiceDuck(remaining + TimeSpan.FromMilliseconds(1500), duckGeneration);
            // ShowBurst corrects each picture from the full length; return the remaining sound time.
            return generation == _generation && !_closed ? (playing ? remaining : duration.Value) : null;
        }

        internal static async Task ReleaseVoiceDuck(TimeSpan delay, long generation)
        {
            await UnduckDelay(delay);
            CoreAudio.Unduck(generation);
        }

        /// <summary>Spawn each placed picture on its own dispatcher timer: Task.Delay continuations
        /// were measured landing 1.7 s late here (thread-pool timer), dispatcher timers stayed on time.
        /// A spawn the platform refuses stops the rest; CloseAll (a newer generation) drops them.</summary>
        internal static void Schedule(List<(List<Bitmap> Frames, TimeSpan Delay, PixelRect Rect, string Path, Screen Screen)> flashes, int firstMs, int staggerMs,
            int generation, int cap, double alpha, TimeSpan fade, TimeSpan lifetime, bool clickable, bool ambient, int hydraGeneration, Action? done,
            FlashMotionStyle? inheritMotion = null, Func<TimeSpan>? lifetimeNow = null, Action<FlashOverlayWindow>? spawned = null)
        {
            var refused = false;
            for (var i = 0; i < flashes.Count; i++)
            {
                var (frames, delay, rect, path, screen) = flashes[i];
                var last = i == flashes.Count - 1;
                DispatcherTimer.RunOnce(() =>
                {
                    try
                    {
                        if (refused || _closed || generation != _generation || Active.Count >= cap) { DisposeFrames(frames); return; }
                        refused = !Show(frames[0], rect, path, screen, alpha, fade, lifetimeNow?.Invoke() ?? lifetime, clickable, ambient, hydraGeneration, inheritMotion, frames, delay); if (!refused) spawned?.Invoke(Active[^1].Window);
                    }
                    catch (Exception ex) { refused = true; Log.Error(ex, "Flash: spawn failed"); }
                    finally { if (last) done?.Invoke(); }
                }, TimeSpan.FromMilliseconds(firstMs + i * staggerMs));
            }
        }

        /// <summary>One flash on screen plus what WPF SpawnFlashWindow pays for it: the lucky roll
        /// before the show (FlashService.cs:1851) and its glow (1881-1967).</summary>
        internal static bool Show(Bitmap bmp, PixelRect rect, string path, Screen screen, double alpha, TimeSpan fade,
            TimeSpan lifetime, bool clickable, bool ambient, int hydraGeneration, FlashMotionStyle? inheritMotion = null,
            List<Bitmap>? frames = null, TimeSpan frameDelay = default)
        {
            var s = CoreSettings.Current;
            var multiplier = FlashFxRules.RollLucky(SkillTreeRules.HasSkill(s, FlashFxRules.LuckySkillId), hydraGeneration, remixMirror: false, Rng);
            var glow = FlashFxRules.ResolveGlow(s.FlashGlowEnabled, ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.CurrentTier,
                multiplier > 1, SkillTreeRules.GetSparkleBoostTier(s), natasha: false);
            var w = new FlashOverlayWindow(bmp, frames, frameDelay)
            {
                HydraGeneration = hydraGeneration, Ambient = ambient, Lifetime = lifetime,
                SpawnedAt = Clock.GetTimestamp(), SpawnScreen = screen, InheritMotion = inheritMotion,
            };
            try
            {
                if (!Spawn(w, rect, screen.Bounds, screen.Scaling, glow, alpha, fade, lifetime, clickable))
                { w.ReleasePictures(); return false; }
                Paid(path, s, hydraGeneration, multiplier);
                return true;
            }
            catch { w.ReleasePictures(); w.Close(); throw; }
        }

        /// <summary>What a shown flash pays (WPF FlashService.cs:1608, 1834-1877, 2165-2172): session
        /// log and history, the lucky chime, XP (8 with a voice, 4 without; a hydra child with
        /// independent timing pays less; x10 lucky), and the achievement + quest tick, which a linked
        /// hydra child skips only when timing is independent.</summary>
        internal static void Paid(string path, AppSettings s, int hydraGeneration, int multiplier)
        {
            App.Sessions?.SessionLog.RecordImages(new[] { path });
            App.MediaHistory?.RecordImages(new[] { path });   // WPF MediaHistoryService.OnFlashDisplayed
            if (multiplier > 1 && !s.SuppressPerkNotifications) LuckyChime(s);   // WPF :4441 PerkNotificationsSuppressed
            CoreProgression.AddXP(FlashFxRules.Xp(_soundPlayingForCurrentFlash, s.HydraLinkedTiming, hydraGeneration, multiplier), "Flash");
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
            // A named size wins; none, or 0 (an AI flash that left the field out), is the player's
            // Image Scale (FlashScaleRule, main 4dcdb625f).
            var (w, h) = FlashPlacement.FitSize(imgW, imgH, monW, monH, FlashScaleRule.Resolve(size, s.ImageScale));

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
            StartAnimation(w);
            var motion = BuildMotion(rect, screen, CoreSettings.Current, Rng, w.InheritMotion);
            w.MotionStyle = motion?.Style ?? FlashMotionStyle.Still;
            if (motion != null) StartDrift(w, motion);
            return true;
        }

        // WPF FlashService.cs:1771-1777, 3330-3338: one heartbeat for all live GIFs,
        // indexed from real elapsed time so a late tick does not slow the animation.
        internal static readonly List<FlashOverlayWindow> Animated = new();
        private static DispatcherTimer? _animationTimer;
        internal static bool AnimationRunning => _animationTimer != null;

        internal static void StartAnimation(FlashOverlayWindow w)
        {
            if (!w.HasAnimation) return;
            w.StartFrames(Clock.GetTimestamp());
            Animated.Add(w);
            w.Closed += (_, _) => StopAnimation(w);
            if (_animationTimer != null) return;
            _animationTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(16), DispatcherPriority.Render, (_, _) => AnimationTick());
            _animationTimer.Start();
        }

        internal static void StopAnimation(FlashOverlayWindow w)
        {
            Animated.Remove(w);
            if (Animated.Count == 0) { _animationTimer?.Stop(); _animationTimer = null; }
        }

        internal static void AnimationTick()
        {
            var now = Clock.GetTimestamp();
            foreach (var w in Animated) w.AdvanceFrames(now);
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
                if (w is not FlashOverlayWindow { Exit: not null } && w is not FlashOverlayWindow { Shatter: not null }
                    && FlashMotion.Step(m, dt))   // a leaving flash holds still
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
            _busy = false;
            _voiceStart?.TrySetResult(false); _stopSound?.Invoke();
            _stopSound = null;
            _voiceLength?.TrySetResult(null);
            _soundPlayingForCurrentFlash = false;
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
        internal static void Pop(FlashOverlayWindow w, bool fromGaze = false)
        {
            if (w.IsFadingOut || w.Exit != null || w.Shatter != null || !Active.Any(e => e.Window == w)) return;
            Active.RemoveAll(e => e.Window == w);
            StopAnimation(w);
            _ = CoreHaptics.Service?.FlashClickVibeAsync();   // WPF FlashService.cs:2945
            var s = CoreSettings.Current;
            // WPF OnFlashClicked hydra (FlashService.cs:2956-2970): Corruption mode, up to two more.
            var n = FlashFxRules.HydraSpawnCount(s.CorruptionMode, s.HydraLimit, isRemix: false, fromGaze: fromGaze, w.HydraGeneration, Active.Count);
            if (n > 0) Multiply(w, n, ChildLifetime(w, s), w.HydraGeneration + 1);
            if (FlashShatter.DecidesClick(s.FlashShatterEnabled, PrizeOwnership.OwnsFlashV2())
                && w.TryBeginShatter(s.MotionLevel, Rng))
            {
                Exiting.Add(w);
                w.Closed += (_, _) => Exiting.Remove(w);
                return;
            }
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
            var remaining = (parent.ExpiresAt > 0 ? Clock.GetElapsedTime(Clock.GetTimestamp(), parent.ExpiresAt)
                : parent.Lifetime - Clock.GetElapsedTime(parent.SpawnedAt)).TotalMilliseconds;
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
                if (generation != _generation) { foreach (var f in flashes) DisposeFrames(f.Frames); return; }
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

        internal static void DisposeFrames(IEnumerable<Bitmap> frames)
        {
            foreach (var frame in frames) frame.Dispose();
        }

        // WPF AnimatedWebp.cs:62-84 bounds concurrent native composition canvases.
        internal static readonly SemaphoreSlim DecodeGate = new(2, 2);

        /// <summary>WPF FlashService.cs:1419-1475 and AnimatedWebp.cs:167-244: GIF/WebP
        /// frames are composed in order, kept at display size with a 60-frame / 30 MB budget,
        /// then timed at the average kept delay times the stride. Static decode is the fallback.</summary>
        internal static (List<Bitmap> Frames, TimeSpan Delay) DecodePicture(string path, int maxDimension)
        {
            DecodeGate.Wait();
            try { return DecodePictureCore(path, maxDimension); }
            finally { DecodeGate.Release(); }
        }

        private static (List<Bitmap> Frames, TimeSpan Delay) DecodePictureCore(string path, int maxDimension)
        {
            var frames = new List<Bitmap>();
            int stillWidth = Math.Max(1, maxDimension);
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec != null && codec.Info.Width > 0 && codec.Info.Height > 0)
                {
                    double fit = Math.Min(1, stillWidth / (double)Math.Max(codec.Info.Width, codec.Info.Height));
                    stillWidth = Math.Max(1, (int)Math.Round(codec.Info.Width * fit));
                }
                if (codec != null && codec.FrameCount > 1 &&
                    (Path.GetExtension(path).Equals(".gif", StringComparison.OrdinalIgnoreCase) ||
                     Path.GetExtension(path).Equals(".webp", StringComparison.OrdinalIgnoreCase)))
                {
                    int count = codec.FrameCount, sw = codec.Info.Width, sh = codec.Info.Height;
                    if (sw > 0 && sh > 0 && (long)sw * sh <= 4096L * 4096L)
                    {
                        double scale = Math.Min(1, Math.Max(1, maxDimension) / (double)Math.Max(sw, sh));
                        int tw = Math.Max(1, (int)Math.Round(sw * scale)), th = Math.Max(1, (int)Math.Round(sh * scale));
                        long bytes = (long)tw * th * 4;
                        double estimatedMb = bytes * (double)count / (1024 * 1024);
                        int keep = estimatedMb > 30 ? Math.Max(10, (int)(count * 30 / estimatedMb)) : count;
                        keep = Math.Min(keep, 60);
                        int step = Math.Max(1, (int)Math.Ceiling(count / (double)keep));
                        int decodeCount = Math.Min(count, 600);
                        var info = codec.FrameInfo;
                        var full = new SKImageInfo(sw, sh, SKColorType.Bgra8888, SKAlphaType.Premul);
                        using var canvas = new SKBitmap(full);
                        using var scaled = new SKBitmap(new SKImageInfo(tw, th, SKColorType.Bgra8888, SKAlphaType.Premul));
                        var pixels = canvas.GetPixels();
                        byte[]? restore = null;
                        int restoreIndex = -1, durationSamples = 0;
                        long durationTotal = 0;
                        for (int i = 0; i < decodeCount && frames.Count < keep; i++)
                        {
                            int prior = -1;
                            if (i > 0 && info[i].RequiredFrame >= 0)
                            {
                                if (info[i - 1].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious) prior = i - 1;
                                else if (restore != null && restoreIndex >= info[i].RequiredFrame)
                                { Marshal.Copy(restore, 0, pixels, restore.Length); prior = restoreIndex; }
                            }
                            var result = codec.GetPixels(full, pixels, prior >= 0 ? new SKCodecOptions(i, prior) : new SKCodecOptions(i));
                            if (result != SKCodecResult.Success && result != SKCodecResult.IncompleteInput) break;
                            if (i + 1 < decodeCount && info[i].DisposalMethod != SKCodecAnimationDisposalMethod.RestorePrevious
                                && info[i + 1].DisposalMethod == SKCodecAnimationDisposalMethod.RestorePrevious)
                            {
                                restore ??= new byte[canvas.ByteCount];
                                canvas.GetPixelSpan().CopyTo(restore);
                                restoreIndex = i;
                            }
                            if (i % step != 0) continue;
                            canvas.ScalePixels(scaled, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
                            frames.Add(new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, scaled.GetPixels(),
                                new PixelSize(tw, th), new Vector(96, 96), scaled.RowBytes));
                            int duration = info[i].Duration;
                            if (duration > 0) { durationTotal += duration; durationSamples++; }
                        }
                        if (frames.Count >= 2)
                        {
                            double avg = durationSamples > 0 ? (double)durationTotal / durationSamples : 100;
                            if (avg < 20) avg = 100;
                            return (frames, TimeSpan.FromMilliseconds(Math.Clamp(avg * step, 20, 2000)));
                        }
                    }
                }
            }
            catch (Exception ex) { Log.Debug("Flash: animated decode failed for {Path}: {E}", path, ex.Message); }
            DisposeFrames(frames);
            frames.Clear();
            try
            {
                using var stream = File.OpenRead(path);
                frames.Add(Bitmap.DecodeToWidth(stream, stillWidth));
            }
            catch (Exception ex) { Log.Debug("Flash: still decode failed for {Path}: {E}", path, ex.Message); }
            return (frames, TimeSpan.FromMilliseconds(100));
        }

        private static string ImagesPath() => Path.Combine(CorePaths.EffectiveAssets, "images");

        /// <summary>
        /// Up to <paramref name="count"/> placed pictures, dealt from <see cref="DiskBag"/> over the enabled
        /// images (FlashService.GetNextImages / GetMediaFiles), re-drawing past unreadable files the
        /// way LoadImagesUntilAsync does (at most max(count*5, 20) tries). Placement happens first,
        /// from the header size alone, so each picture is decoded AT its display size like WPF's
        /// decode-at-display-size - never a full-resolution source held per window.
        /// </summary>
        internal static List<(List<Bitmap> Frames, TimeSpan Delay, PixelRect Rect, string Path, Screen Screen)> LoadPictures(int count, IReadOnlyList<Screen> screens,
            int[] targets, AppSettings s, List<PixelRect> occupied, int? size)
        {
            var root = CorePaths.EffectiveAssets;
            var dir = Path.Combine(root, "images");
            var result = new List<(List<Bitmap>, TimeSpan, PixelRect, string, Screen)>(count);
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
                    var (frames, delay) = DecodePicture(path, Math.Max(rect.Width, rect.Height));
                    if (frames.Count == 0) continue;
                    result.Add((frames, delay, rect, path, screen));
                    occupied.Add(rect);
                }
                catch (Exception ex) { Log.Debug("Flash: could not decode {Path}: {E}", path, ex.Message); }
            }
            return result;
        }
    }
}
