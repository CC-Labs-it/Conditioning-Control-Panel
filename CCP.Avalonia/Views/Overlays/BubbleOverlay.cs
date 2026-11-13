using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SkiaSharp;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.Immutable;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Chaos;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>BubbleService</c>'s ambient field (Services/BubbleService.cs):
    /// Core <see cref="AmbientBubbleField"/> moves and scores, this draws. WPF's default render is
    /// the shared host (one click-through surface, pops from a hook); here that is one override-
    /// redirect window per targeted screen whose X11 input region is exactly the live bubbles'
    /// hit discs, so the desktop stays clickable everywhere else. Reached through
    /// <see cref="CoreBubbles"/>, which App seeds.
    ///
    /// <para>Hot path is allocation-free: one cached frame delegate, one Render pass over the
    /// field with struct transforms, a reused rect buffer for the input region.</para>
    ///
    /// <para>Chaos runs share the field: ChaosRunHost spawns treat bubbles here (BeginChaos/
    /// SpawnChaos/EndChaos) and gets their pops; they draw tinted and pay no ambient XP.</para>
    ///
    /// <para>ponytail: Bubbles v2 motions (Rain/Spiral In),
    /// Brain Drain, Natasha's red bubble, the avatar egg, lucky gold glow/sparkles
    /// and the milestone popup remain unported.
    /// Gaze pops, pop haptics, mod pop audio and achievement credit are wired.
    /// See the feat-bubble-pop ledger row for the current parity evidence.</para>
    /// </summary>
    internal static class BubbleOverlay
    {
        internal static readonly AmbientBubbleField Field = new()
        {
            LuckyProc = multiplier => FlashOverlay.LuckyProc(App.Mods?.GetLuckyBubbleLabel()
                ?? ConditioningControlPanel.Localization.Loc.Get("skill_lucky_bubble"), multiplier),
        };
        internal static readonly AmbientPayloadRunner Payloads = new();
        private static readonly List<BubbleOverlayWindow> Windows = new();
        private static readonly List<Screen> Targets = new();
        private static DispatcherTimer? _spawnTimer;
        private static bool _running;
        private static TimeSpan? _lastStep;
        private static bool _pending;   // a frame is requested; the loop idles while the field is empty
        internal static Bitmap? Image;
        internal static Bitmap? TeaseImage;
        internal static readonly Dictionary<AmbientBubble, (List<Bitmap> Frames, double Delay, long Start, bool Animated)> TeaseFaces = new();
        private static int _teaseAnimated;

        // WPF BubbleService.cs:5397: a random local face from the effective assets pool.
        internal static string? PickTeaseFace(string root, Random random)
        {
            try
            {
                var dir = Path.Combine(root, "teasebubble");
                if (!Directory.Exists(dir)) return null;
                var files = Directory.GetFiles(dir).Where(f => new[] { ".gif", ".png", ".jpg", ".jpeg", ".webp", ".bmp" }
                    .Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)).ToArray();
                return files.Length == 0 ? null : files[random.Next(files.Length)];
            }
            catch (Exception ex) { Log.Debug("Tease face pool: {E}", ex.Message); return null; }
        }

        internal static (List<Bitmap> Frames, double Delay, bool Animated) DecodeTeaseFace(string path, int width, bool allowAnimation)
        {
            try
            {
                using var codec = SKCodec.Create(path);
                if (codec == null) return (new(), 0, false);
                // WPF AnimatedWebp.cs:173 decode limit (16 Mpx canvas) and the 48-frame AttachAnimation default.
                if (allowAnimation && codec.FrameCount > 1 && (long)codec.Info.Width * codec.Info.Height <= 4096L * 4096L
                    && new FileInfo(path).Length <= Services.Chaos.ChaosTuning.TEASE_ANIMATED_MAX_BYTES)
                {
                    var (frames, _) = SpiralOverlay.Decode(path, width, 48);
                    var plan = SpiralFrames.Plan(codec.Info.Width, codec.Info.Height, codec.FrameCount, codec.FrameInfo[0].Duration);
                    int step = plan.Frames > 48 ? (int)Math.Ceiling(codec.FrameCount / 48.0) : plan.Step;
                    double avg = codec.FrameInfo.Where((f, i) => i < 600 && i % step == 0 && i / step < frames.Count && f.Duration > 0)
                        .Select(f => (double)f.Duration).DefaultIfEmpty(100).Average();
                    if (avg < 20) avg = 100;
                    if (frames.Count > 0) return (frames, Math.Clamp(avg * step, 20, 2000), true);
                }
                using var stream = File.OpenRead(path);
                int w = codec.Info.Width, h = codec.Info.Height, target = Math.Max(1, width);
                if ((long)Math.Min(w, h) * target < Math.Max(w, h))
                {
                    using var thin = h > w ? Bitmap.DecodeToWidth(stream, 1) : Bitmap.DecodeToHeight(stream, 1);
                    var size = h > w ? new PixelSize(1, target) : new PixelSize(target, 1);
                    return (new() { thin.CreateScaledBitmap(size) }, 0, false);
                }
                var still = h > w ? Bitmap.DecodeToHeight(stream, target) : Bitmap.DecodeToWidth(stream, target);
                return (new() { still }, 0, false);
            }
            catch (Exception ex) { Log.Debug("Tease face decode: {E}", ex.Message); return (new(), 0, false); }
        }

        private static async void LoadTeaseFace(AmbientBubble bubble)
        {
            var path = PickTeaseFace(CorePaths.EffectiveAssets, Field.Random);
            if (path == null) return;
            bool slot = _teaseAnimated < MaxAnimatedTease(global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.CurrentTier) && (path.EndsWith(".gif", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".webp", StringComparison.OrdinalIgnoreCase));
            if (slot) _teaseAnimated++;
            var (frames, delay, animated) = await Task.Run(() => DecodeTeaseFace(path, Math.Clamp((int)(bubble.Size * .86), 64, 512), slot));
            if (slot && !animated) _teaseAnimated--;
            if (!Field.Bubbles.Contains(bubble) || bubble.Popping)
            {
                foreach (var frame in frames) frame.Dispose();
                if (animated) _teaseAnimated--;
                return;
            }
            if (frames.Count > 0) TeaseFaces[bubble] = (frames, delay, Environment.TickCount64, animated);
            RequestFrame();
        }

        // WPF PerformanceProfile.cs:69 MaxAnimatedTeaseBubbles.
        internal static int MaxAnimatedTease(PerformanceTier tier) =>
            tier switch { PerformanceTier.Performance => Services.Chaos.ChaosTuning.TEASE_MAX_ANIMATED, PerformanceTier.Balanced => 6, _ => 10 };

        // WPF BubbleService.cs:5127-5166: the pill shows until the verb is learned or the bubble dies.
        internal static bool ShowsTeaseHint(AmbientBubble b) =>
            b.Spec?.IsTease == true && !b.Popping && !Services.Chaos.ChaosMeta.State.BubbleHintsLearned.Contains("tease");

        private static void PruneTeaseFaces()
        {
            foreach (var entry in TeaseFaces)
                if (!Field.Bubbles.Contains(entry.Key))
                {
                    foreach (var frame in entry.Value.Frames) frame.Dispose();
                    if (entry.Value.Animated) _teaseAnimated--;
                    TeaseFaces.Remove(entry.Key);
                }
        }

        /// <summary>WPF BubbleService.LoadBubbleImage (BubbleService.cs:221, :918-936): every Start re-resolves
        /// the active mod's bubble.png, so a mod switch shows its sprite on the next start (a <c>??=</c>
        /// cache kept the first mod's art for the life of the process).</summary>
        internal static void LoadBubbleImage() => Image = Helpers.ModArt.TryLoad("bubble.png");

        /// <summary>The "N/300 today" line listens to this (WPF AmbientXpBudgetChanged).</summary>
        internal static event Action? XpBudgetChanged;

        /// <summary>WPF BubbleService.OnBubblePopped (the leash bubble task counts these).</summary>
        internal static event Action? Popped;

        // CCP_FRAME_STATS=1: one stdout line per 10 s - step+region cost per frame with N bubbles.
        private static readonly bool Stats = Environment.GetEnvironmentVariable("CCP_FRAME_STATS") == "1";
        private static readonly Stopwatch StatClock = new();
        private static double _statMax, _statSum;
        private static int _statN, _statBubbles, _statRectMax;
        private static long _statRects;

        /// <summary>The ambient game is running (windows opened only for a Chaos run do not count).</summary>
        internal static void RaiseAll(Action<Window> raise)
        {
            foreach (var window in Windows) if (window.IsVisible) raise(window);
        }

        public static bool IsRunning => _running && !_chaosOpenedWindows;
        internal static bool IsFrameOwner(BubbleOverlayWindow w) => Windows.Count > 0 && Windows[0] == w;

        /// <summary><paramref name="frequency"/> per minute overrides the setting (WPF Start's).</summary>
        public static void Start(Visual host, int? frequency = null) => Start(host, frequency, ambient: true);

        /// <param name="ambient">False opens the windows for a Chaos run only: no ambient spawn timer.</param>
        private static void Start(Visual host, int? frequency, bool ambient)
        {
            if (_running && ambient && _chaosOpenedWindows)
            {
                _chaosOpenedWindows = false;   // the ambient game starts mid-run: it owns the windows now
                StartSpawning(frequency);
                return;
            }
            if (_running) return;
            if (!X11Overlay.IsAvailable) { Log.Warning("Bubbles: this platform cannot show click-through overlays; skipped"); return; }
            var screens = ScreenList.Enumerate(host);
            if (screens.Count == 0) return;
            var s = CoreSettings.Current;
            var primary = Math.Max(0, screens.ToList().FindIndex(x => x.IsPrimary));
            LoadBubbleImage();
            TeaseImage = ChaosArt.Resolve("bubbles", "tease");
            foreach (var i in PinkFilterOverlay.ResolveScreenIndices(MonitorTarget.FollowGlobal, s.GlobalTargetMonitor, s.DualMonitorEnabled, screens.Count, primary))
            {
                var sc = screens[i];
                var w = new BubbleOverlayWindow(Targets.Count, sc.Bounds, sc.Scaling > 0 ? sc.Scaling : 1);
                if (!X11Overlay.SetInputRects(w, w.Rects, 0) || !X11Overlay.SetOverrideRedirect(w, sc.Bounds))
                {
                    Log.Warning("Bubbles: the platform refused a click-through topmost overlay window; skipped");
                    w.Close();
                    Stop();
                    return;
                }
                w.Show();
                Windows.Add(w);
                Targets.Add(sc);
            }
            _running = true;
            _lastStep = null;
            if (ambient) StartSpawning(frequency);
        }

        private static void StartSpawning(int? frequency)
        {
            var s = CoreSettings.Current;
            _spawnTimer = new DispatcherTimer { Interval = AmbientBubbleField.SpawnInterval(frequency ?? s.BubblesFrequency) };
            _spawnTimer.Tick += (_, _) => Spawn();
            _spawnTimer.Start();
            Spawn();   // WPF: first bubble immediately (and wakes the frame loop)
            if (Stats) StatClock.Restart();
            App.DiscordRpc?.SetBubbleActivity();   // WPF BubbleService.cs:244
            Log.Information("BubbleService started - {Freq} bubbles/min", frequency ?? s.BubblesFrequency);
        }

        public static void Stop()
        {
            BubblePayloadHost.Stop();
            // WPF Stop returns early when the ambient game is idle, so a chaos-only teardown keeps presence.
            var wasRunning = IsRunning;
            if (_chaosPopped != null && _running)
            {
                // The ambient game stops mid-run: its bubbles go, the run keeps the windows.
                _spawnTimer?.Stop();
                _spawnTimer = null;
                Field.Bubbles.RemoveAll(b => b.Spec == null || b.AmbientTrigger);
                _chaosOpenedWindows = true;
                foreach (var w in Windows) w.Sync();
                if (wasRunning) App.DiscordRpc?.SetIdleActivity();   // WPF BubbleService.cs:853
                return;
            }
            _chaosOpenedWindows = false;
            ChaosRipples.Clear();
            _running = false;
            _pending = false;
            _spawnTimer?.Stop();
            _spawnTimer = null;
            Field.Bubbles.Clear();
            PruneTeaseFaces();   // WPF PopAllBubbles: force-destroy, no animation
            foreach (var w in Windows) w.Close();
            Windows.Clear();
            Targets.Clear();
            if (wasRunning) App.DiscordRpc?.SetIdleActivity();   // WPF BubbleService.cs:827 (WPF Stop returns early when idle)
        }

        public static void RefreshFrequency()
        {
            if (_spawnTimer == null) return;
            _spawnTimer.Interval = AmbientBubbleField.SpawnInterval(CoreSettings.Current.BubblesFrequency);
        }

        /// <summary>WPF SpawnBubble: under the cap, not during a display change, on a random target.</summary>
        internal static void Spawn()
        {
            var s = CoreSettings.Current;
            if (!_running || Targets.Count == 0 || DisplayChangeCoordinator.SpawnsSuppressed || !Field.CanSpawn(s)) return;
            var i = Field.Random.Next(Targets.Count);
            var a = Targets[i].WorkingArea;
            // Outside sessions, bubbles are always clickable (no UI toggle exists for the setting).
            var clickable = !CoreSession.IsSessionRunning || s.BubblesClickable;
            var mod = (CoreMods.ActiveModTokenProvider?.Invoke() as ModManifest)?.BubbleScale;
            Field.Bubbles.Add(CreateAmbient(i, a, Windows[i].Scaling, s, mod, clickable));
            RequestFrame();
        }

        // WPF BubbleService.cs:1230-1265: ambient triggers share the field and its reward path.
        internal static AmbientBubble CreateAmbient(int screen, PixelRect a, double scaling,
            AppSettings settings, double? mod, bool clickable, Random? random = null)
        {
            var rng = random ?? Field.Random;
            var spec = AmbientBubblePayloads.Roll(settings, rng,
                Field.Bubbles.Count(b => b.AmbientTrigger || b.Spec is { IsLive: false }));
            AmbientBubble b;
            if (spec == null)
            {
                // WPF AmbientBubbleMotion.RollForSpawn: picker + grants + motion level -> one concrete style.
                var level = global::ConditioningControlPanel.Avalonia.Controls.AmbientFxCanvas.Env.Level; // WPF MotionFx.Level, capped by OS and Gentle Visuals
                var motion = BubbleMotionRoll.Resolve(settings.BubbleMotionStyle,
                    PrizeOwnership.IsGranted(PrizeOwnership.BubbleRain),
                    PrizeOwnership.IsGranted(PrizeOwnership.BubbleSpiralIn),
                    level, rng.NextDouble());
                b = AmbientBubble.Spawn(rng, screen, a.X, a.Y, a.Width, a.Height, scaling, settings, mod, clickable, level, motion);
            }
            else
                b = AmbientBubble.SpawnChaos(rng, screen, a.X, a.Y, a.Width, a.Height, scaling, spec, spec.Motion, settings.BubbleSpeedBoost);
            b.AmbientTrigger = spec != null;
            b.Clickable = clickable;
            return b;
        }

        /// <summary>WPF StartAnimationDriver/StopAnimationTimerIfIdle: one chain, parked while empty.</summary>
        private static void RequestFrame()
        {
            if (_pending || !_running || Windows.Count == 0) return;
            _pending = true;
            Windows[0].RequestAnimationFrame(Windows[0].FrameCallback);
        }

        // ---- Chaos field (WPF BubbleService.BeginChaosMode / SpawnChaosBubble / EndChaosMode) ----
        // WPF BubbleService.cs:1631-1632, 1669-1677, 1704-1719. Physical-pixel
        // distances, advanced once per logical field tick, never by the render clock.
        private static readonly List<(Point Center, double AgeMs)> ChaosRipples = new();

        internal static Point CenterPx(AmbientBubble b)
        {
            double scale = b.Screen >= 0 && b.Screen < Targets.Count ? Targets[b.Screen].Scaling : 1;
            return new Point(b.CenterX * scale, b.CenterY * scale);
        }

        internal static void TriggerChaosRipple(AmbientBubble b)
        {
            if (_chaosPopped == null || Field.HoldChaos) return;
            var center = CenterPx(b);
            ChaosRipples.Add((center, 0));
            if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.Ripple(center, 430, 550, strong: false);
            else ChaosFieldFxOverlay.Ripple(center, 430, 550);
            RequestFrame();
        }

        internal static void TickChaosRipples()
        {
            if (Field.HoldChaos || _chaosPopped == null) return;
            for (int i = ChaosRipples.Count - 1; i >= 0; i--)
            {
                var (center, age) = ChaosRipples[i];
                age += 32;
                double radius = 430 * Math.Min(1, age / 550);
                foreach (var b in Field.Bubbles.ToArray())
                {
                    if (b.Popping || b.Spec == null || b.Spec.IsLive || b.Spec.IsDarter || b.Spec.IsFreeze) continue;
                    if (b.Spec.IsTease || b.Spec.IsBrittle) continue; // WPF BubbleService.cs:4653-4659
                    var point = CenterPx(b);
                    double dx = point.X - center.X, dy = point.Y - center.Y;
                    if (dx * dx + dy * dy <= radius * radius) PopChaos(b);
                }
                if (age >= 550) ChaosRipples.RemoveAt(i);
                else ChaosRipples[i] = (center, age);
            }
        }

        private static Action<AmbientBubble>? _chaosPopped;
        private static int _hoverScreen = -1;
        private static double _hoverX, _hoverY;

        internal static void SetHover(int screen, double x, double y)
        {
            _hoverScreen = screen;
            _hoverX = x;
            _hoverY = y;
        }

        internal static void ClearHover(int screen)
        {
            if (_hoverScreen == screen) _hoverScreen = -1;
        }

        // WPF BubbleService.IsCursorOverLiveChaosBubble: scan all armed lives, even behind a treat.
        internal static bool CursorOnLive
        {
            get
            {
                if (_chaosPopped == null || _hoverScreen < 0) return false;
                foreach (var b in Field.Bubbles)
                    if (b.Screen == _hoverScreen && b.Spec is { IsLive: true } && !b.Popping
                        && b.Contains(_hoverX, _hoverY)) return true;
                return false;
            }
        }
        private static Func<AmbientBubble, bool>? _chaosCanChannel;
        private static Action<AmbientBubble>? _chaosTeaseTouched;

        /// <summary>WPF Detonate/CompleteDefuse anchor the pop point at the bubble's centre.</summary>
        private static void AnchorPop(AmbientBubble b) { ChaosLastPopX = b.CenterX; ChaosLastPopY = b.CenterY; }

        /// <summary>WPF OnPlayerPress: a live chaos bubble starts a hold-to-defuse channel (if the
        /// focus gate allows) at the press point (global DIPs); every other bubble pops.</summary>
        internal static bool IsShielded(AmbientBubble b) => b.Spec is { IsChaperoneLive: true }
            && b.Escort is { Popping: false } escort && Field.Bubbles.Contains(escort);

        internal static void BounceShield(AmbientBubble b)
        {
            if (b.BounceShield(Field.Clock()))
            {
                var cue = File.Exists(ChaosCuePath("shield_thunk")) ? "shield_thunk" : "toy_denied";
                if (File.Exists(ChaosCuePath(cue))) PlayChaosCue(cue, 0.45f);
            }
            RequestFrame();
        }

        internal static void Press(AmbientBubble b, double x, double y)
        {
            if (b.Spec?.IsTease == true)
            {
                if (Field.HoldChaos || b.Popping) return;
                b.Popping = true;
                AnchorPop(b);
                if (ChaosSkiaFxOverlay.Enabled)
                    ChaosSkiaFxOverlay.Burst(CenterPx(b), Color.FromRgb(0xFF, 0x3D, 0x5A), 1.4);
                _chaosTeaseTouched?.Invoke(b);
                RequestFrame();
                return;
            }
            if (!b.IsLiveThreat)
            {
                bool wasPopping = b.Popping;
                Pop(b);
                if (!wasPopping && b.Popping) EStimClick(b);   // WPF BubbleService.cs:352: a player pop conducts
                return;
            }
            if (Field.HoldChaos) return;
            if (IsShielded(b)) { BounceShield(b); return; }
            Field.PressLive(b, _chaosCanChannel?.Invoke(b) ?? true, x, y);
            RequestFrame();
        }

        /// <summary>WPF ChaosMouseHeld/CursorPx: the hold moved or let go.</summary>
        internal static void PointerHold(bool held, double x, double y)
        {
            Field.Held = held;
            Field.CursorX = x; Field.CursorY = y;
        }
        private static bool _chaosOpenedWindows;
        /// <summary>WPF BubbleService.ChaosLastPopXDip/YDip: the last chaos pop's centre, global DIPs.</summary>
        internal static double ChaosLastPopX, ChaosLastPopY;

        /// <summary>Live (not popping) chaos bubbles: WPF ActiveBubbles during a run.</summary>
        internal static int ChaosCount
        {
            get { var n = 0; foreach (var b in Field.Bubbles) if (b.Spec != null && !b.AmbientTrigger && !b.Popping) n++; return n; }
        }

        /// <summary>WPF BeginChaosMode: chaos pops go to <paramref name="onPop"/>; opens the windows
        /// if the ambient field is not already running.</summary>
        internal static void BeginChaos(Visual host, Action<AmbientBubble> onPop,
            Action<AmbientBubble>? onDefuse = null, Action<AmbientBubble>? onDetonate = null,
            Func<AmbientBubble, bool>? canChannel = null,
            Action<AmbientBubble>? onTeaseTouched = null, Action<AmbientBubble>? onTeaseDenied = null)
        {
            _chaosPopped = onPop;
            _chaosCanChannel = canChannel;
            _chaosTeaseTouched = onTeaseTouched;
            Field.ChaosTeaseDenied = onTeaseDenied == null ? null : b => { AnchorPop(b); onTeaseDenied(b); };
            Field.Held = false;
            Field.ChaosDefused = onDefuse == null ? null : b =>
            {
                AnchorPop(b);
                // WPF CreateChaosBubble onDefuse (:1571): a held snap deflates with a hiss, a sweep snaps.
                PlayChaosCue(b.DefusedViaChannel && File.Exists(ChaosCuePath("defuse_hiss")) ? "defuse_hiss" : "snap", 0.6f);
                onDefuse(b);
            };
            Field.ChaosDetonated = onDetonate == null ? null : b => { AnchorPop(b); onDetonate(b); };
            if (_running) return;
            Start(host, null, ambient: false);
            _chaosOpenedWindows = _running;
        }

        /// <summary>WPF SetChaosFrozen + SetChaosInputLocked (manual pause): chaos bubbles hold
        /// still and a click on one is swallowed, so a paused field cannot be farmed.</summary>
        internal static void HoldChaos(bool held)
        {
            Field.HoldChaos = held;
            RequestFrame();
        }

        /// <summary>WPF PopAllBubbles at a wave boundary, retaining the chaos callbacks.</summary>
        internal static void ClearChaos()
        {
            Field.Held = false;
            Field.Bubbles.Clear();
            ChaosRipples.Clear();   // hand-ported from #4776: no ripple or hover outlives the field
            _hoverScreen = -1;
            foreach (var w in Windows) w.Sync();
            RequestFrame();
        }

        /// <summary>WPF EndChaosMode: every chaos bubble goes at once (panic/stop clear the field).</summary>
        internal static void EndChaos()
        {
            Field.HoldChaos = false;
            ClearEStim();
            Field.FreezeChaos = false;
            Field.ClearFreezeVisuals();
            ChaosRipples.Clear();
            ChaosRunHost.ClearBodyBuzzHops();
            _hoverScreen = -1;
            _chaosPopped = null;
            _chaosCanChannel = null;
            _chaosTeaseTouched = null;
            Field.ChaosTeaseDenied = null;
            Field.ChaosDefused = Field.ChaosDetonated = null;
            Field.Held = false;
            Field.Bubbles.RemoveAll(b => b.Spec != null && !b.AmbientTrigger);
            PruneTeaseFaces();
            if (_chaosOpenedWindows) { Stop(); return; }
            foreach (var w in Windows) w.Sync();
        }

        // ---- E-Stim charged pops (hand-ported from #4356; WPF BubbleService.cs:352-440) ----
        // A charge is spent only when a player pop finds a conductor. Each hop is a one-shot
        // timer kept in EStimHops, so run end, panic and EndChaos cancel every pending hit.
        private static int _estimCharges;
        private static bool _estimFork;
        private static readonly List<IDisposable> EStimHops = new();
        internal static int EStimCharges => _estimCharges;
        /// <summary>Called after each conduction with the charges left (the run's event line and banner).</summary>
        internal static Action<int>? EStimArc;

        internal static void ArmEStim(int charges, bool fork)
        {
            if (_chaosPopped == null) return;
            _estimCharges = Math.Max(1, charges);
            _estimFork = fork;
            ChaosEStimOverlay.Arm();
        }

        private static void ClearEStim()
        {
            foreach (var hop in EStimHops) hop.Dispose();
            EStimHops.Clear();
            _estimCharges = 0;
            _estimFork = false;
            EStimArc = null;
            ChaosEStimOverlay.Disarm();
        }

        private static bool EscortShielded(AmbientBubble b) => IsShielded(b);

        private static void EStimClick(AmbientBubble source)
        {
            if (_estimCharges <= 0 || source.Spec is not { IsLive: false, IsDarter: false, IsFreeze: false }) return;
            static double Dist2(AmbientBubble a, AmbientBubble b) =>
                (a.CenterX - b.CenterX) * (a.CenterX - b.CenterX) + (a.CenterY - b.CenterY) * (a.CenterY - b.CenterY);
            const double Reach2 = 600 * 600;
            const int MaxChain = 40;
            var pool = Field.Bubbles.Where(b => !ReferenceEquals(b, source) && !b.Popping && !b.AmbientTrigger &&
                b.Spec is { IsDarter: false, IsFreeze: false, IsTease: false, IsBrittle: false } && !EscortShielded(b)).ToList();
            var hits = new List<(AmbientBubble From, AmbientBubble To, int DelayMs)>();
            if (!_estimFork)
            {
                foreach (var target in pool.Where(b => Dist2(source, b) <= Reach2).OrderBy(b => Dist2(source, b)).Take(3))
                    hits.Add((source, target, 70 * (hits.Count + 1)));
            }
            else
            {
                // The capstone fork chains breadth-first from each struck bubble.
                var queue = new Queue<(AmbientBubble Bubble, int Depth)>();
                var taken = new HashSet<AmbientBubble> { source };
                queue.Enqueue((source, 0));
                while (queue.Count > 0 && hits.Count < MaxChain)
                {
                    var (current, depth) = queue.Dequeue();
                    foreach (var target in pool)
                    {
                        if (taken.Contains(target) || Dist2(current, target) > Reach2) continue;
                        taken.Add(target);
                        hits.Add((current, target, 70 * (depth + 1)));
                        queue.Enqueue((target, depth + 1));
                        if (hits.Count == MaxChain) break;
                    }
                }
            }
            if (hits.Count == 0) return;
            _estimCharges--;
            if (_estimCharges == 0) { _estimFork = false; ChaosEStimOverlay.Disarm(); }
            foreach (var hit in hits)
            {
                var to = hit.To;
                IDisposable? hop = null;
                hop = DispatcherTimer.RunOnce(() =>
                {
                    EStimHops.Remove(hop!);
                    // WPF BubbleService.cs:4661: a held (paused) field swallows the hop.
                    if (Field.HoldChaos || _chaosPopped == null || !Field.Bubbles.Contains(to) || to.Popping || EscortShielded(to)) return;
                    if (to.IsLiveThreat) { if (to.Snap()) Field.ChaosDefused?.Invoke(to); }
                    else PopChaos(to);
                    RequestFrame();
                }, TimeSpan.FromMilliseconds(hit.DelayMs));
                EStimHops.Add(hop);
            }
            var bolts = hits.Select(h => (CenterPx(h.From), CenterPx(h.To))).ToList();
            // Gentle Visuals: the Skia bolt re-jitters every frame (flicker), the overlay path fades.
            if (ChaosSkiaFxOverlay.Enabled && ConditioningControlPanel.Services.Safety.GentleVisuals.AllowFlicker)
                ChaosSkiaFxOverlay.Strike(bolts);
            else
                ChaosEStimOverlay.Strike(bolts.Select(b => (new PixelPoint((int)b.Item1.X, (int)b.Item1.Y),
                    new PixelPoint((int)b.Item2.X, (int)b.Item2.Y))).ToList());
            EStimArc?.Invoke(_estimCharges);
        }

        /// <summary>WPF SpawnChaosBubble (FloatUp, RainDown, Chaperone roaming). Without an overlay window (headless, or a
        /// platform that refuses click-through) the bubble lives on a 1920x1080 model screen.</summary>
        internal static AmbientBubble SpawnChaos(Services.Chaos.EffectBubbleSpec spec, Random? random = null,
            Services.Chaos.ChaosMotion motion = Services.Chaos.ChaosMotion.FloatUp)
        {
            AmbientBubble b;
            if (Targets.Count == 0)
                b = AmbientBubble.SpawnChaos((random ?? Field.Random), 0, 0, 0, 1920, 1080, 1, spec, motion);
            else
            {
                var i = (random ?? Field.Random).Next(Targets.Count);
                if (spec.SpawnAtPxX is double x && spec.SpawnAtPxY is double y)
                {
                    for (var n = 0; n < Targets.Count; n++)
                    {
                        var bounds = Targets[n].Bounds;
                        if (x >= bounds.X && x < bounds.Right && y >= bounds.Y && y < bounds.Bottom)
                        { i = n; break; }
                    }
                }
                var a = Targets[i].WorkingArea;
                b = AmbientBubble.SpawnChaos((random ?? Field.Random), i, a.X, a.Y, a.Width, a.Height, Windows[i].Scaling, spec, motion);
            }
            Field.Bubbles.Add(b);
            if (spec.IsTease) LoadTeaseFace(b);
            RequestFrame();
            return b;
        }

        /// <summary>WPF Bubble.Pop with a spec: anchor the pop point, start the burst, hand it to the run.</summary>
        internal static void PopChaos(AmbientBubble b)
        {
            if (b.Popping || b.Spec == null || Field.HoldChaos) return;   // held: the click is swallowed
            // WPF BubbleService.cs:4645-4669: instant pops preserve touch-only and escort immunity.
            if (b.Spec.IsTease || b.Spec.IsBrittle) return;
            if (IsShielded(b)) { BounceShield(b); return; }
            if (b.IsLiveThreat)
            {
                if (!b.Snap()) return;
                AnchorPop(b);
                // WPF BubbleService.cs:4686-4688: a live snap is a bigger release, in SnapColor at 1.3,
                // burst before the defuse callback can clear the field.
                var snapScale = b.Screen >= 0 && b.Screen < Windows.Count ? Windows[b.Screen].Scaling : 1;
                ChaosSkiaFxOverlay.Burst(new Point(b.CenterX * snapScale, b.CenterY * snapScale),
                    Color.FromRgb(0x7A, 0xE0, 0xFF), 1.3);
                Field.ChaosDefused?.Invoke(b);
                RequestFrame();
                return;
            }
            b.Popping = true;
            ChaosLastPopX = b.CenterX;
            ChaosLastPopY = b.CenterY;
            // WPF BubbleService.cs:4682-4688: burst before the callback can clear the field.
            var scaling = b.Screen >= 0 && b.Screen < Windows.Count ? Windows[b.Screen].Scaling : 1;
            ChaosSkiaFxOverlay.Burst(new Point(b.CenterX * scaling, b.CenterY * scaling),
                b.Spec.IsLive ? Color.FromRgb(0x7A, 0xE0, 0xFF) : Color.FromUInt32(b.Spec.Tint),
                b.Spec.IsLive ? 1.3 : 1.0);
            PlayChaosCue(b.Spec.IsFreeze ? "freeze_catch" : null, b.Spec.IsFreeze ? 0.6f : 1f);
            _chaosPopped?.Invoke(b);
            RequestFrame();
        }

        /// <summary>WPF BubbleService.cs:325-340: paid DVD sweep, darters/freeze exempt.
        /// Use the existing pop/snap callbacks so payloads, score and lessons still run.</summary>
        internal static void PopChaosInRect(Rect rect)
        {
            if (Field.HoldChaos) return;
            for (int i = Field.Bubbles.Count - 1; i >= 0; i--)
            {
                if (i >= Field.Bubbles.Count) continue;
                var b = Field.Bubbles[i];
                // WPF BubbleService.cs:4652-4660: instant sweeps cannot end Tease or Brittle.
                if (b.Popping || b.Spec == null || b.Spec.IsDarter || b.Spec.IsFreeze || b.Spec.IsTease || b.Spec.IsBrittle) continue;
                if (!rect.Intersects(new Rect(b.X, b.Y, b.Size, b.Size))) continue;
                if (IsShielded(b)) { BounceShield(b); continue; }
                if (b.IsLiveThreat)
                {
                    if (b.Snap()) { Field.ChaosDefused?.Invoke(b); RequestFrame(); }
                }
                else PopChaos(b);
            }
        }

        private static string ChaosCuePath(string name) =>
            ContentLocator.Resolve(Path.Combine("Resources", "sounds", "chaos", name + ".mp3"));   // as ChaosSfx

        /// <summary>WPF PlayChaosCue/PlayCue (BubbleService.cs:2049, :2058): the chaos cue at
        /// (master x bubbles)^1.5 x scale, or the ambient pop when the cue is absent (or null).</summary>
        internal static void PlayChaosCue(string? name, float scale)
        {
            var s = CoreSettings.Current;
            var vol = (float)Math.Pow(s.MasterVolume / 100f * (s.BubblesVolume / 100f), 1.5);
            var cue = name == null ? "" : ChaosCuePath(name);
            if (cue.Length > 0 && File.Exists(cue)) { CoreAudio.PlayOneShot(cue, Math.Min(vol * scale, 1f), "chaos-cue"); return; }
            var pop = new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }[Field.Random.Next(3)];
            CoreAudio.PlayOneShot(Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", "bubbles", pop), Math.Min(vol, 1f), "bubble-pop");
        }

        /// <summary>WPF AwardAmbientPop, head half: sound, XP, quest credit.</summary>
        internal static void Pop(AmbientBubble b)
        {
            if (b.Popping) return;   // WPF BubbleService.cs:4647 rejects an already popping bubble.
            if (b.Spec != null && !b.AmbientTrigger) { PopChaos(b); return; }
            var s = CoreSettings.Current;
            var paid = Field.Pop(b, s);
            if (paid > 0) CoreProgression.AddXP(paid, "Bubble");
            if (b.AmbientTrigger && b.Spec?.Payload is { } payload) BubblePayloadHost.Run(payload);
            XpBudgetChanged?.Invoke();
            App.NoteFeatureUsed(ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureBubbles);   // WPF OnBubblePopped
            var master = s.MasterVolume / 100f;
            var vol = (float)Math.Pow(master * (s.BubblesVolume / 100f), 1.5);
            var (path, chime) = ResolvePopSound(b.Lucky, s.SuppressPerkNotifications, Field.Random);
            if (chime) vol *= 0.35f;
            if (File.Exists(path)) CoreAudio.PlayOneShot(path, Math.Min(vol, 1f), "bubble-pop");
            App.Achievements?.TrackBubblePopped();   // WPF BubbleService.cs:1117
            try { App.Quests?.TrackBubblePopped(); } catch (Exception ex) { Log.Debug("bubble quest credit: {E}", ex.Message); }
            _ = CoreHaptics.Service?.BubblePopAsync();   // WPF BubbleService.cs:1089
            try { Popped?.Invoke(); } catch (Exception ex) { Log.Debug("bubble popped handler: {E}", ex.Message); }   // WPF OnBubblePopped
        }

        // WPF BubbleService.cs:2191-2236: mod audio, perk opt-out and missing-chime fallback.
        internal static (string Path, bool Chime) ResolvePopSound(bool lucky, bool suppressed, Random random)
        {
            string Resolve(string cue) => CoreModArt.AudioOverridePath(cue)
                ?? Path.Combine(AppContext.BaseDirectory, "Resources", "sounds", cue);
            if (lucky && !suppressed)
            {
                var chime = Resolve($"chime{random.Next(1, 4)}.mp3");
                if (File.Exists(chime)) return (chime, true);
            }
            return (Resolve("bubbles/" + new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }[random.Next(3)]), false);
        }

        internal static void OnFrame(TimeSpan now)
        {
            _pending = false;
            if (!_running || Windows.Count == 0) return;
            // WPF OnAnimationRenderTick: one logical step per >= 30 ms, re-based to the real frame
            // time so a late frame drops rather than bursting catch-up steps.
            if (_lastStep is not { } last || (now - last).TotalMilliseconds >= AmbientBubbleField.StepMs)
            {
                _lastStep = now;
                var t0 = Stats ? Stopwatch.GetTimestamp() : 0;
                StepChaosField();
                PruneTeaseFaces();
                TickChaosRipples();
                foreach (var w in Windows) w.Sync();
                if (Stats) Sample(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                if (Field.Bubbles.Count == 0 && ChaosRipples.Count == 0) { _lastStep = null; return; }   // idle until the next spawn
            }
            RequestFrame();
        }

        internal static void StepChaosField()
        {
            Field.Step();
            if (Field.HoldChaos) return;
            foreach (var b in Field.Bubbles)
            {
                if (b.Spec is not { IsEscort: true } || b.Popping || b.OrbitFrames == 0 || b.OrbitFrames % 7 != 0 || b.OrbitTarget is not { Popping: false } live) continue;
                double t = Field.Random.NextDouble();
                var a = CenterPx(b);
                var z = CenterPx(live);
                var p = new Point(a.X + (z.X - a.X) * t, a.Y + (z.Y - a.Y) * t);
                if (ChaosSkiaFxOverlay.Enabled) ChaosSkiaFxOverlay.TrailDot(p, 0.35);
                else ChaosFieldFxOverlay.TrailDot(p, 0.35);
            }
        }

        private static void Sample(double ms)
        {
            _statSum += ms; _statN++; _statBubbles += Field.Bubbles.Count;
            // Disc input regions send about one native rect per pixel row per bubble.
            var rects = 0;
            foreach (var w in Windows) rects += w.InputRectCount;
            _statRects += rects;
            if (rects > _statRectMax) _statRectMax = rects;
            if (ms > _statMax) _statMax = ms;
            if (StatClock.Elapsed.TotalSeconds < 10) return;
            Console.WriteLine(FormattableString.Invariant(
                $"[Bubbles] {_statN} steps, avg {(double)_statBubbles / _statN:F1} bubbles, step+sync mean {_statSum / _statN:F3} ms, max {_statMax:F3} ms, input rects mean {(double)_statRects / _statN:F0} max {_statRectMax}, gen0 GCs {GC.CollectionCount(0)}"));
            _statSum = _statMax = 0; _statN = _statBubbles = _statRectMax = 0; _statRects = 0;
            StatClock.Restart();
        }
    }

    /// <summary>One screen of the field: draws its bubbles (WPF Bubble visuals: bubble.png, or the
    /// gradient ellipse fallback, scaled and rotated about the centre at the pop fade) and takes
    /// presses only inside their hit discs.</summary>
    internal sealed class BubbleOverlayWindow : Window
    {
        private static readonly IBrush FallbackFill = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(180, 200, 220, 255), 0), new GradientStop(Color.FromArgb(80, 255, 255, 255), 1) },
        };
        private static readonly IPen FallbackPen = new Pen(Brushes.White, 2);
        private static readonly IBrush TeaseBack = new SolidColorBrush(Color.FromRgb(0x14, 0x07, 0x0C));
        private static readonly IBrush TeaseEdge = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Colors.White, .8), new GradientStop(Colors.Transparent, 1) }
        };
        private static readonly IBrush TeaseShine = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative), EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
            GradientStops = { new GradientStop(Color.FromArgb(150, 255, 255, 255), 0), new GradientStop(Colors.Transparent, .7) }
        };
        private static readonly IPen TeasePen = new Pen(Brushes.White, 5);
        private static readonly IBrush HintBack = new SolidColorBrush(Color.FromArgb(0xA0, 0x12, 0x0A, 0x18)), HintFore = new SolidColorBrush(Color.FromRgb(0xFF, 0xE2, 0xF2));
        internal static readonly IPen ShieldPen = new Pen(new SolidColorBrush(Color.FromRgb(0x9C, 0xE8, 0xFF)), 3.5,
            dashStyle: new DashStyle(new[] { 3.0, 2.0 }, 0));
        private static readonly Dictionary<uint, IPen> FuseCache = new();
        private static readonly Dictionary<uint, (IBrush, IPen)> TintCache = new();

        /// <summary>One pen per ring colour+opacity (a few hundred at most), so the render pass stays allocation-free.</summary>
        internal static IPen FusePen(uint argb)
        {
            if (FuseCache.TryGetValue(argb, out var p)) return p;
            return FuseCache[argb] = new ImmutablePen(new ImmutableSolidColorBrush(Color.FromUInt32(argb)), 4);
        }

        /// <summary>One fill + pen per variant tint, built once (the render pass stays allocation-free).</summary>
        private static (IBrush, IPen) TintPaint(uint argb)
        {
            if (TintCache.TryGetValue(argb, out var p)) return p;
            var c = Color.FromUInt32(argb);
            return TintCache[argb] = (new SolidColorBrush(c, 0.45), new Pen(new SolidColorBrush(c), 3));
        }

        internal readonly int Index;
        internal readonly PixelRect Bounds;
        internal readonly double Scaling;
        internal PixelRect[] Rects = new PixelRect[64];
        internal int InputRectCount { get; private set; }
        private int _lastCount = -1;
        /// <summary>Cached (no per-frame closure); a closed window's late frame is ignored.</summary>
        internal readonly Action<TimeSpan> FrameCallback;

        /// <summary>--render-all only: two bubbles on a grey backdrop (the proof PNG has no desktop).</summary>
        internal BubbleOverlayWindow() : this(0, new PixelRect(0, 0, 1280, 720), 1)
        {
            Width = 1280;
            Height = 720;
            Background = Brushes.DimGray;
            BubbleOverlay.Image ??= Helpers.ModArt.TryLoad("bubble.png");
            if (!BubbleOverlay.IsRunning && BubbleOverlay.Field.Bubbles.Count == 0)
            {
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { X = 300, Y = 250, Size = 200, Angle = 20 });
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { X = 750, Y = 300, Size = 240, Popping = true, Scale = 1.4, Fade = 0.5 });
            }
        }

        public BubbleOverlayWindow(int index, PixelRect bounds, double scaling)
        {
            Index = index;
            Bounds = bounds;
            Scaling = scaling;
            WindowDecorations = WindowDecorations.None;
            TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            ShowActivated = false;
            CanResize = false;
            Content = new Layer(this);
            FrameCallback = now => { if (BubbleOverlay.IsFrameOwner(this)) BubbleOverlay.OnFrame(now); };
            PointerPressed += OnPressed;
            PointerEntered += (_, e) => UpdateHover(e);
            PointerMoved += (_, e) => { UpdateHover(e); OnHold(e, true); };
            PointerExited += (_, _) => BubbleOverlay.ClearHover(Index);
            PointerReleased += (_, e) => OnHold(e, false);
            // WPF polls the physical button (BubbleService.cs:545): a release the window never saw
            // (alt-tab, compositor grab) still breaks the hold.
            PointerCaptureLost += (_, _) =>
            {
                if (BubbleOverlay.Field.Held) BubbleOverlay.PointerHold(false, BubbleOverlay.Field.CursorX, BubbleOverlay.Field.CursorY);
            };
        }

        /// <summary>Global bubble DIPs (screen px / scaling) to this window's DIPs.</summary>
        private double K => Scaling / RenderScaling;

        /// <summary>WPF: left or right press on a bubble pops it (topmost first); a miss passes through.</summary>
        private void OnPressed(object? sender, PointerPressedEventArgs e)
        {
            var p = e.GetPosition(this);
            var b = BubbleOverlay.Field.HitTest(Index, (Bounds.X + p.X * RenderScaling) / Scaling, (Bounds.Y + p.Y * RenderScaling) / Scaling);
            if (b == null) return;
            BubbleOverlay.Press(b, (Bounds.X + p.X * RenderScaling) / Scaling, (Bounds.Y + p.Y * RenderScaling) / Scaling);
            e.Handled = true;
        }

        private void UpdateHover(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            BubbleOverlay.SetHover(Index, (Bounds.X + p.X * RenderScaling) / Scaling, (Bounds.Y + p.Y * RenderScaling) / Scaling);
        }

        /// <summary>The press is captured, so the hold's moves and release land here (WPF TickChannel reads them).</summary>
        private void OnHold(PointerEventArgs e, bool held)
        {
            if (!BubbleOverlay.Field.Held) return;
            var p = e.GetPosition(this);
            BubbleOverlay.PointerHold(held, (Bounds.X + p.X * RenderScaling) / Scaling, (Bounds.Y + p.Y * RenderScaling) / Scaling);
        }

        /// <summary>After a step: input region = live clickable bubbles here; repaint if any were drawn.</summary>
        internal void Sync()
        {
            var n = 0;
            var bubbles = BubbleOverlay.Field.Bubbles;
            for (var i = 0; i < bubbles.Count; i++)
            {
                var b = bubbles[i];
                if (b.Screen != Index || !b.Clickable || b.Popping || b.Size <= 0) continue;
                // WPF BubbleService.cs:661/:3023-3027: the shared-host hook tests a physical-pixel disc;
                // :581 and :3013 exclude non-clickable and popping bubbles.
                // Both native backends already accept unions of rectangles: rasterise each
                // physical pixel row, clipped to this screen. Keep the buffer across frames.
                var cx = b.CenterX * Scaling - Bounds.X;
                var cy = b.CenterY * Scaling - Bounds.Y;
                var r = b.Size * Scaling / 2;
                var first = Math.Max(0, (int)Math.Ceiling(cy - r));
                var last = Math.Min(Bounds.Height - 1, (int)Math.Floor(cy + r));
                for (var y = first; y <= last; y++)
                {
                    var dy = y - cy;
                    var half = Math.Sqrt(Math.Max(0, r * r - dy * dy));
                    var left = Math.Max(0, (int)Math.Ceiling(cx - half));
                    var right = Math.Min(Bounds.Width - 1, (int)Math.Floor(cx + half));
                    // Round trips through mixed-DPI global DIPs can differ by an ULP at the
                    // radius. Use the same predicate as the press handler at the endpoints.
                    bool Hit(int x) => b.Contains((Bounds.X + x) / Scaling, (Bounds.Y + y) / Scaling);
                    if (left > 0 && Hit(left - 1)) left--;
                    if (right < Bounds.Width - 1 && Hit(right + 1)) right++;
                    while (left <= right && !Hit(left)) left++;
                    while (right >= left && !Hit(right)) right--;
                    if (left > right) continue;
                    var rect = new PixelRect(left, y, right - left + 1, 1);
                    // Coalesce identical adjacent rows (especially useful for small bubbles).
                    if (n > 0 && Rects[n - 1] is var prev && prev.X == rect.X && prev.Width == rect.Width && prev.Bottom == y)
                        Rects[n - 1] = new PixelRect(prev.X, prev.Y, prev.Width, prev.Height + 1);
                    else
                    {
                        if (n == Rects.Length) Array.Resize(ref Rects, Rects.Length * 2);
                        Rects[n++] = rect;
                    }
                }
            }
            InputRectCount = n;
            var drawn = HasBubbles();
            if (!drawn && _lastCount == 0) return;   // stayed empty: region already empty, nothing to repaint
            X11Overlay.SetInputRects(this, Rects, InputRectCount);
            (Content as Control)?.InvalidateVisual();
            _lastCount = drawn ? 1 : 0;
        }

        private bool HasBubbles()
        {
            foreach (var b in BubbleOverlay.Field.Bubbles) if (b.Screen == Index) return true;
            return false;
        }

        private sealed class Layer : Control
        {
            private readonly BubbleOverlayWindow _w;
            public Layer(BubbleOverlayWindow w) { _w = w; IsHitTestVisible = false; }

            private static readonly IBrush FreezeAura = new RadialGradientBrush
            {
                Center = RelativePoint.Center, GradientOrigin = RelativePoint.Center,
                GradientStops = { new GradientStop(Color.FromArgb(0, 150, 210, 255), 0.30),
                    new GradientStop(Color.FromArgb(190, 150, 210, 255), 0.66),
                    new GradientStop(Color.FromArgb(0, 150, 210, 255), 1.0) }
            };

            public override void Render(DrawingContext dc)
            {
                var k = _w.K;
                double ox = _w.Bounds.X / _w.Scaling, oy = _w.Bounds.Y / _w.Scaling;
                var bubbles = BubbleOverlay.Field.Bubbles;
                for (var i = 0; i < bubbles.Count; i++)
                {
                    var b = bubbles[i];
                    if (b.Screen != _w.Index) continue;
                    var teaseImage = b.Spec?.IsTease == true ? BubbleOverlay.TeaseImage : null;
                    var img = teaseImage ?? BubbleOverlay.Image;
                    double cx = (b.CenterX - ox) * k, cy = (b.CenterY - oy) * k, size = b.Size * k, s = b.DrawScale * b.ChannelScale;
                    var m = Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(b.Angle * Math.PI / 180)
                          * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(cx, cy);
                    using (dc.PushTransform(m))
                    using (dc.PushOpacity(Math.Clamp(b.Fade * b.MotionFade, 0, 1)))
                    {
                        if (b.Spec != null && BubbleOverlay.Field.FreezeChaos)
                        {
                            using (dc.PushOpacity(BubbleOverlay.Field.FreezeAuraOpacity))
                                dc.DrawEllipse(FreezeAura, null, new Point(cx, cy), (size + 6 * k) / 2, (size + 6 * k) / 2);
                        }
                        if (b.Spec != null && teaseImage == null)   // WPF skips tint for a variant sprite
                        {
                            var (fill, pen) = TintPaint(b.Spec.Tint);
                            dc.DrawEllipse(fill, pen, new Point(cx, cy), size / 2 - 5, size / 2 - 5);
                        }
                        if (img != null) dc.DrawImage(img, new Rect(cx - size / 2, cy - size / 2, size, size));
                        else dc.DrawEllipse(FallbackFill, FallbackPen, new Point(cx, cy), size / 2 - 5, size / 2 - 5);
                        if (b.Spec?.IsTease == true && BubbleOverlay.TeaseFaces.TryGetValue(b, out var face)) using (dc.PushTransform(m.TryInvert(out var inverse) ? inverse : Matrix.Identity))
                        {
                            var frame = face.Frames[face.Animated
                                ? (int)((Environment.TickCount64 - face.Start) / face.Delay % face.Frames.Count) : 0];
                            double edge = size * .86, x = cx - edge / 2, y = cy - edge / 2;
                            var rect = new Rect(x, y, edge, edge);
                            double side = Math.Min(frame.Size.Width, frame.Size.Height);
                            var src = new Rect((frame.Size.Width - side) / 2, (frame.Size.Height - side) / 2, side, side);
                            using (dc.PushGeometryClip(new EllipseGeometry(rect)))
                            {
                                using (dc.PushOpacityMask(TeaseEdge, rect))
                                {
                                    dc.DrawEllipse(TeaseBack, null, new Point(cx, cy), edge / 2, edge / 2);
                                    using (dc.PushOpacity(.92)) dc.DrawImage(frame, src, rect);
                                    using (dc.PushOpacity(.16 + .14 * (.5 + .5 * Math.Sin(Environment.TickCount64 / 1000.0 * 5.2))))
                                        dc.DrawEllipse(TeaseShine, null, new Point(cx, cy), edge / 2, edge / 2);
                                }
                            }
                        }
                        if (b.Spec?.IsTease == true && teaseImage == null && !b.Popping)
                        {
                            double r = size * 0.12;
                            dc.DrawLine(TeasePen, new Point(cx - r, cy - r), new Point(cx + r, cy + r));
                            dc.DrawLine(TeasePen, new Point(cx + r, cy - r), new Point(cx - r, cy + r));
                        }
                        if (!b.Popping && BubbleOverlay.IsShielded(b))
                        {
                            using (dc.PushOpacity(b.ShieldOpacity))
                                dc.DrawEllipse(null, ShieldPen, new Point(cx, cy), size / 2 + 6 * k, size / 2 + 6 * k);
                        }
                        if (b.FuseTotalMs > 0 && !b.Popping)   // WPF fuse ring: shrinks 1 -> 0.45; yellow, flash, red brink
                        {
                            double rr = (size / 2 - 2) * (0.45 + 0.55 * b.FuseFraction);
                            dc.DrawEllipse(null, FusePen(b.FuseRingArgb), new Point(cx, cy), rr, rr);
                        }
                    }
                    if (BubbleOverlay.ShowsTeaseHint(b))   // WPF BubbleService.cs:5137-5160: pulsing 0.55-1 pill under the bubble
                    {
                        var hint = new FormattedText(global::ConditioningControlPanel.Localization.Loc.Get("chaos_hint_tease"), System.Globalization.CultureInfo.CurrentUICulture,
                            FlowDirection.LeftToRight, new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.SemiBold), 12.5 * k, HintFore);
                        var box = new Rect(cx - hint.Width / 2 - 8 * k, cy + size / 2 + 8 * k, hint.Width + 16 * k, hint.Height + 5 * k);
                        using (dc.PushOpacity(Math.Clamp(b.Fade * b.MotionFade, 0, 1) * (.775 + .225 * Math.Cos(Environment.TickCount64 * Math.PI / 800))))
                        {
                            dc.DrawRectangle(HintBack, null, box, 9 * k, 9 * k);
                            dc.DrawText(hint, new Point(box.X + 8 * k, box.Y + 2 * k));
                        }
                    }
                }
            }
        }
    }
}
