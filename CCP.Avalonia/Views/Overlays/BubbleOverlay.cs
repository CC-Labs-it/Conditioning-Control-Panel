using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Controls;   // AmbientFxCanvas.Env
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Views.Features;   // ScreenList
using ConditioningControlPanel.Models;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.UI;
using Serilog;
using NatashasFavourite = ConditioningControlPanel.Services.Chaster.NatashasFavourite;

namespace ConditioningControlPanel.Avalonia.Views.Overlays
{
    /// <summary>
    /// The head half of WPF <c>BubbleService</c>'s ambient field (Services/BubbleService.cs):
    /// Core <see cref="AmbientBubbleField"/> moves and scores, this draws. WPF's default render is
    /// the shared host (one click-through surface, pops from a hook); here that is one override-
    /// redirect window per targeted screen whose X11 input region is exactly the live bubbles'
    /// hit squares, so the desktop stays clickable everywhere else. Reached through
    /// <see cref="CoreBubbles"/>, which App seeds.
    ///
    /// <para>Hot path is allocation-free: one cached frame delegate, one Render pass over the
    /// field with struct transforms, a reused rect buffer for the input region.</para>
    ///
    /// <para>Chaos runs share the field: ChaosRunHost spawns treat bubbles here (BeginChaos/
    /// SpawnChaos/EndChaos) and gets their pops; they draw tinted and pay no ambient XP.</para>
    ///
    /// <para>ponytail: plain bubbles FloatUp only (trigger bubbles follow the Chaos Motion setting). Bubbles v2 motions (Rain/Spiral In), Brain Drain,
    /// the avatar egg, gaze pops, lucky glow/sparkles, mod pop-sound overrides and bubble
    /// achievements remain follow-up work. Ambient payloads and Natasha share this host.</para>
    /// </summary>
    internal static class BubbleOverlay
    {
        internal static readonly AmbientBubbleField Field = new();
        internal static readonly AmbientPayloadRunner Payloads = new();
        static BubbleOverlay()
        {
            Field.NatashaPopped = b => { Pop(b); BookNatasha(b, "natasha"); };
            Field.NatashaResisted = b => BookNatasha(b, "natasha_held");
        }
        private static void BookNatasha(AmbientBubble b, string row) =>
            ChasterHead.Service?.NoteAt(row, new ConditioningControlPanel.Services.Chaster.ScreenPoint(b.CenterX * (Windows.ElementAtOrDefault(b.Screen)?.Scaling ?? 1),
                b.CenterY * (Windows.ElementAtOrDefault(b.Screen)?.Scaling ?? 1)));
        private static readonly List<BubbleOverlayWindow> Windows = new();
        private static readonly List<Screen> Targets = new();
        private static DispatcherTimer? _spawnTimer;
        private static bool _running;
        private static TimeSpan? _lastStep;
        private static bool _pending;   // a frame is requested; the loop idles while the field is empty
        internal static Bitmap? Image;

        /// <summary>WPF BubbleService.LoadBubbleImage (BubbleService.cs:221, :918-936): every Start re-resolves
        /// the active mod's bubble.png, so a mod switch shows its sprite on the next start (a <c>??=</c>
        /// cache kept the first mod's art for the life of the process).</summary>
        internal static void LoadBubbleImage() => Image = Helpers.ModArt.TryLoad("bubble.png");

        private static readonly Dictionary<string, Bitmap?> Sprites = new();
        /// <summary>WPF Bubble ctor (:3420): flash wears bubble.png, a sweeper its own art, every other
        /// variant assets/Chaos/bubbles/{id}.png; null = no sprite (tinted bubble.png + label).
        /// shortcut: bundled art only (WPF also reads UserAssetsPath first); add that when a user-art port lands.</summary>
        internal static Bitmap? SpriteFor(ConditioningControlPanel.Services.Chaos.EffectBubbleSpec spec) =>
            spec.VariantId == "flash" ? Image : (spec.IsSweeper ? SpriteOf("sweeper") : null) ?? SpriteOf(spec.VariantId);

        private static Bitmap? SpriteOf(string id)
        {
            if (!Sprites.TryGetValue(id, out var bmp))
                Sprites[id] = bmp = Helpers.ModArt.TryLoadBuiltIn($"chaos/bubbles/{id}.png", 384);
            return bmp;
        }

        /// <summary>The "N/300 today" line listens to this (WPF AmbientXpBudgetChanged).</summary>
        internal static event Action? XpBudgetChanged;

        /// <summary>WPF BubbleService.OnBubblePopped (the leash bubble task counts these).</summary>
        internal static event Action? Popped;

        // CCP_FRAME_STATS=1: one stdout line per 10 s - step+region cost per frame with N bubbles.
        private static readonly bool Stats = Environment.GetEnvironmentVariable("CCP_FRAME_STATS") == "1";
        private static readonly Stopwatch StatClock = new();
        private static double _statMax, _statSum;
        private static int _statN, _statBubbles;

        /// <summary>The ambient game is running (windows opened only for a Chaos run do not count).</summary>
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
            Field.Held = false;
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
            _running = false;
            _pending = false;
            _spawnTimer?.Stop();
            _spawnTimer = null;
            Field.Bubbles.Clear();   // WPF PopAllBubbles: force-destroy, no animation
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
            var spec = AmbientBubblePayloads.Roll(s, Field.Random, Field.Bubbles.Count(b => b.AmbientTrigger || b.Spec is { IsLive: false }));
            var bubble = spec == null
                ? AmbientBubble.Spawn(Field.Random, i, a.X, a.Y, a.Width, a.Height, Windows[i].Scaling, s, mod, clickable)
                : AmbientBubble.SpawnChaos(Field.Random, i, a.X, a.Y, a.Width, a.Height, Windows[i].Scaling, spec, spec.Motion);
            bubble.AmbientTrigger = spec != null;
            bubble.Clickable = clickable;
            bubble.Natasha = ChasterHead.Service?.CanBook("natasha") == true && ConditioningControlPanel.Services.Chaster.NatashasFavourite.Roll(Field.Random);
            Field.Bubbles.Add(bubble);
            RequestFrame();
        }

        /// <summary>WPF StartAnimationDriver/StopAnimationTimerIfIdle: one chain, parked while empty.</summary>
        private static void RequestFrame()
        {
            if (_pending || !_running || Windows.Count == 0) return;
            _pending = true;
            Windows[0].RequestAnimationFrame(Windows[0].FrameCallback);
        }

        // ---- Chaos field (WPF BubbleService.BeginChaosMode / SpawnChaosBubble / EndChaosMode) ----
        private static Action<AmbientBubble>? _chaosPopped;
        private static Func<AmbientBubble, bool>? _chaosCanChannel;

        /// <summary>WPF Detonate/CompleteDefuse anchor the pop point at the bubble's centre.</summary>
        private static void AnchorPop(AmbientBubble b) { ChaosLastPopX = b.CenterX; ChaosLastPopY = b.CenterY; }

        /// <summary>WPF OnPlayerPress: a live chaos bubble starts a hold-to-defuse channel (if the
        /// focus gate allows) at the press point (global DIPs); every other bubble pops.</summary>
        internal static void Press(AmbientBubble b, double x, double y)
        {
            if (b.Natasha && !b.Popping)
            {
                Field.Held = true; Field.CursorX = x; Field.CursorY = y;
                b.BeginResist(Field.Clock()); RequestFrame(); return;
            }
            if (!b.IsLiveThreat) { Pop(b); return; }
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
            Func<AmbientBubble, bool>? canChannel = null)
        {
            _chaosPopped = onPop;
            _chaosCanChannel = canChannel;
            Field.Held = false;
            Field.ChaosDefused = onDefuse == null ? null : b => { AnchorPop(b); onDefuse(b); };
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

        /// <summary>WPF EndChaosMode: every chaos bubble goes at once (panic/stop clear the field).</summary>
        internal static void EndChaos()
        {
            Field.HoldChaos = false;
            _chaosPopped = null;
            _chaosCanChannel = null;
            Field.ChaosDefused = Field.ChaosDetonated = null;
            Field.Held = false;
            Field.Bubbles.RemoveAll(b => b.Spec != null && !b.AmbientTrigger);
            if (_chaosOpenedWindows) { Stop(); return; }
            foreach (var w in Windows) w.Sync();
        }

        /// <summary>WPF SpawnChaosBubble, FloatUp only. Without an overlay window (headless, or a
        /// platform that refuses click-through) the bubble lives on a 1920x1080 model screen.</summary>
        internal static AmbientBubble SpawnChaos(Services.Chaos.EffectBubbleSpec spec)
        {
            AmbientBubble b;
            if (Targets.Count == 0)
                b = AmbientBubble.SpawnChaos(Field.Random, 0, 0, 0, 1920, 1080, 1, spec);
            else
            {
                var i = Field.Random.Next(Targets.Count);
                var a = Targets[i].WorkingArea;
                b = AmbientBubble.SpawnChaos(Field.Random, i, a.X, a.Y, a.Width, a.Height, Windows[i].Scaling, spec);
            }
            Field.Bubbles.Add(b);
            RequestFrame();
            return b;
        }

        /// <summary>WPF Bubble.Pop with a spec: anchor the pop point, start the burst, hand it to the run.</summary>
        internal static void PopChaos(AmbientBubble b)
        {
            if (b.Popping || b.Spec == null || Field.HoldChaos) return;   // held: the click is swallowed
            b.Popping = true;
            ChaosLastPopX = b.CenterX;
            ChaosLastPopY = b.CenterY;
            _chaosPopped?.Invoke(b);
            RequestFrame();
        }

        /// <summary>WPF AwardAmbientPop, head half: sound, XP, quest credit.</summary>
        internal static void Pop(AmbientBubble b)
        {
            if (b.Popping) return;
            if (b.Spec != null && !b.AmbientTrigger) { PopChaos(b); return; }
            var s = CoreSettings.Current;
            var paid = Field.Pop(b, s);
            if (paid > 0) CoreProgression.AddXP(paid, "Bubble");
            if (b.AmbientTrigger && b.Spec?.Payload is { } payload)
            {
                // WPF ShowChaosEffectLabel: the effect's word flashes at the bubble.
                Chaos.ChaosPopText.Show(b.CenterX, b.CenterY,
                    ConditioningControlPanel.Services.Chaos.ChaosBubbleVariants.PopWordFor(b.Spec.VariantId), Color.FromUInt32(b.Spec.Tint));
                BubblePayloadHost.Run(payload);
            }
            XpBudgetChanged?.Invoke();
            App.NoteFeatureUsed(ConditioningControlPanel.Services.Companion.Brain.MemorySignalWriter.FeatureBubbles);   // WPF OnBubblePopped
            var master = s.MasterVolume / 100f;
            var vol = (float)Math.Pow(master * (s.BubblesVolume / 100f), 1.5);
            var dir = Path.Combine(AppContext.BaseDirectory, "Resources", "sounds");
            var path = b.Lucky
                ? Path.Combine(dir, $"chime{Field.Random.Next(1, 4)}.mp3")   // lucky: a chime replaces the pop, at 0.35x
                : Path.Combine(dir, "bubbles", new[] { "Pop.mp3", "Pop2.mp3", "Pop3.mp3" }[Field.Random.Next(3)]);
            if (b.Lucky) vol *= 0.35f;
            CoreAudio.PlayOneShot(path, Math.Min(vol, 1f), "bubble-pop");
            try { App.Quests?.TrackBubblePopped(); } catch (Exception ex) { Log.Debug("bubble quest credit: {E}", ex.Message); }
            _ = CoreHaptics.Service?.BubblePopAsync();   // WPF BubbleService.cs:1089
            try { Popped?.Invoke(); } catch (Exception ex) { Log.Debug("bubble popped handler: {E}", ex.Message); }   // WPF OnBubblePopped
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
                Field.Step();
                foreach (var w in Windows) w.Sync();
                if (Stats) Sample(Stopwatch.GetElapsedTime(t0).TotalMilliseconds);
                if (Field.Bubbles.Count == 0) { _lastStep = null; return; }   // idle until the next spawn
            }
            RequestFrame();
        }

        private static void Sample(double ms)
        {
            _statSum += ms; _statN++; _statBubbles += Field.Bubbles.Count;
            if (ms > _statMax) _statMax = ms;
            if (StatClock.Elapsed.TotalSeconds < 10) return;
            Console.WriteLine(FormattableString.Invariant(
                $"[Bubbles] {_statN} steps, avg {(double)_statBubbles / _statN:F1} bubbles, step+sync mean {_statSum / _statN:F3} ms, max {_statMax:F3} ms, gen0 GCs {GC.CollectionCount(0)}"));
            _statSum = _statMax = 0; _statN = _statBubbles = 0;
            StatClock.Restart();
        }
    }

    /// <summary>One screen of the field: draws its bubbles (WPF Bubble visuals: bubble.png, or the
    /// gradient ellipse fallback, scaled and rotated about the centre at the pop fade) and takes
    /// presses only inside their hit squares.</summary>
    internal sealed class BubbleOverlayWindow : Window
    {
        private static readonly IBrush FallbackFill = new RadialGradientBrush
        {
            GradientStops = { new GradientStop(Color.FromArgb(180, 200, 220, 255), 0), new GradientStop(Color.FromArgb(80, 255, 255, 255), 1) },
        };
        private static readonly IPen FallbackPen = new Pen(Brushes.White, 2);
        private static readonly IBrush NatashaWash = new SolidColorBrush(Color.FromRgb(0xE0, 0x2A, 0x4A));
        private static readonly IPen ResistMint = new Pen(new SolidColorBrush(Color.FromRgb(0x5F, 0xFF, 0xD0)), 5, lineCap: PenLineCap.Round);
        private static readonly IBrush LabelShadow = new SolidColorBrush(Color.FromArgb(0xB0, 0, 0, 0));

        /// <summary>WPF Bubble.RingArc: clockwise from twelve o'clock, <paramref name="progress"/> of the way round.</summary>
        internal static Geometry RingArc(Point c, double r, double progress)
        {
            progress = Math.Clamp(progress, 0, 1);
            if (progress >= 0.999) return new EllipseGeometry(new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
            var g = new StreamGeometry();
            using (var ctx = g.Open())
            {
                var a = progress * 2 * Math.PI;
                ctx.BeginFigure(new Point(c.X, c.Y - r), false);
                ctx.ArcTo(new Point(c.X + r * Math.Sin(a), c.Y - r * Math.Cos(a)), new Size(r, r), 0, progress > 0.5, SweepDirection.Clockwise);
                ctx.EndFigure(false);
            }
            return g;
        }
        private static readonly IPen FuseYellow = new Pen(new SolidColorBrush(Color.FromRgb(255, 210, 40)), 4);
        private static readonly IPen FuseRed = new Pen(new SolidColorBrush(Color.FromRgb(255, 45, 45)), 4);
        private static readonly Dictionary<uint, (IBrush, IPen)> TintCache = new();

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
        internal readonly PixelRect[] Rects = new PixelRect[64];
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
                BubbleOverlay.Field.Bubbles.Add(new AmbientBubble { X = 1000, Y = 150, Size = 180, AmbientTrigger = true, Natasha = true,
                    Spec = AmbientBubblePayloads.Build("pink") });
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
            PointerMoved += (_, e) => OnHold(e, true);
            PointerReleased += (_, e) => { OnHold(e, false); e.Pointer.Capture(null); };
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
            // The region already catches this press; capture also catches release outside it.
            // No low-level hook is used here, so a separate native press-catcher window is unnecessary.
            if (b.Natasha || b.IsLiveThreat) e.Pointer.Capture(this);
            e.Handled = true;
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
            for (var i = 0; i < bubbles.Count && n < Rects.Length; i++)
            {
                var b = bubbles[i];
                if (b.Screen != Index || !b.Clickable || b.Popping) continue;
                Rects[n++] = new PixelRect((int)(b.X * Scaling) - Bounds.X, (int)(b.Y * Scaling) - Bounds.Y,
                    (int)(b.Size * Scaling), (int)(b.Size * Scaling));
            }
            var drawn = HasBubbles();
            if (!drawn && _lastCount == 0) return;   // stayed empty: region already empty, nothing to repaint
            X11Overlay.SetInputRects(this, Rects, n);
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

            public override void Render(DrawingContext dc)
            {
                var k = _w.K;
                double ox = _w.Bounds.X / _w.Scaling, oy = _w.Bounds.Y / _w.Scaling;
                var img = BubbleOverlay.Image;
                var bubbles = BubbleOverlay.Field.Bubbles;
                var tier = AmbientFxCanvas.Env.CurrentTier;   // once per frame, not per bubble
                bool glow = AmbientFxCanvas.Env.AllowGlow(tier), loops = AmbientFxCanvas.Env.AllowAmbientLoops;
                var haloBlur = glow ? Math.Min(NatashasFavourite.HaloBlurDip, AmbientFxCanvas.Env.MaxGlowBlurRadius(tier)) : 0;
                for (var i = 0; i < bubbles.Count; i++)
                {
                    var b = bubbles[i];
                    if (b.Screen != _w.Index) continue;
                    double cx = (b.CenterX - ox) * k, cy = (b.CenterY - oy) * k, size = b.Size * k, s = b.DrawScale * b.ChannelScale;
                    var m = Matrix.CreateTranslation(-cx, -cy) * Matrix.CreateRotation(b.Angle * Math.PI / 180)
                          * Matrix.CreateScale(s, s) * Matrix.CreateTranslation(cx, cy);
                    var sprite = b.Spec != null ? BubbleOverlay.SpriteFor(b.Spec) : null;
                    using (dc.PushTransform(m))
                    using (dc.PushOpacity(Math.Clamp(b.Fade, 0, 1)))
                    {
                        if (b.Natasha && !b.Popping && glow)
                        {
                            // WPF MarkNatasha halo (a DropShadowEffect), flaring on the blink like :4371.
                            var op = NatashasFavourite.HaloOpacity + 2.0 * (loops ? NatashasFavourite.WashAlphaAt(b.AliveSeconds) : 0);
                            var halo = new BoxShadow { Blur = haloBlur * k, Color = Color.FromArgb((byte)(255 * Math.Clamp(op, 0, 1)), NatashasFavourite.R, NatashasFavourite.G, NatashasFavourite.B) };
                            dc.DrawRectangle(null, null, new RoundedRect(new Rect(cx - size / 2, cy - size / 2, size, size), size / 2), new BoxShadows(halo));
                        }
                        if (b.Spec != null && sprite == null)   // no variant sprite: its tint under bubble.png
                        {
                            var (fill, pen) = TintPaint(b.Spec.Tint);
                            dc.DrawEllipse(fill, pen, new Point(cx, cy), size / 2 - 5, size / 2 - 5);
                        }
                        if ((sprite ?? img) is { } art) dc.DrawImage(art, new Rect(cx - size / 2, cy - size / 2, size, size));
                        else dc.DrawEllipse(FallbackFill, FallbackPen, new Point(cx, cy), size / 2 - 5, size / 2 - 5);
                        if (b.Natasha && !b.Popping)
                        {
                            var n = NatashasFavourite.BubbleWashAt(b.AliveSeconds, loops);
                            using (dc.PushOpacity(n)) dc.DrawEllipse(NatashaWash, null, new Point(cx, cy), size / 2, size / 2);
                        }
                        if (b.FuseTotalMs > 0 && !b.Popping)   // WPF fuse ring: shrinks 1 -> 0.45, red at the brink
                        {
                            double rr = (size / 2 - 2) * (0.45 + 0.55 * b.FuseFraction);
                            double brinkMs = Math.Min(b.FuseTotalMs * 0.30, ConditioningControlPanel.Services.Chaos.ChaosTuning.RING_BRINK_MS);
                            dc.DrawEllipse(null, b.FuseRemainingMs <= brinkMs ? FuseRed : FuseYellow, new Point(cx, cy), rr, rr);
                        }
                    }
                    // WPF: label and mint ring sit in the bubble's grid, unrotated.
                    if (sprite == null && !b.Popping && b.Spec?.Label is { Length: > 0 } label)
                    {
                        var em = Math.Max(14, b.Size * 0.30) * k;
                        FormattedText Glyph(IBrush fg) => new(label, System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                            new Typeface(FontFamily.Default, FontStyle.Normal, FontWeight.Bold), em, fg);
                        var t = Glyph(Brushes.White);
                        var at = new Point(cx - t.Width / 2, cy - t.Height / 2);
                        dc.DrawText(Glyph(LabelShadow), at + new Point(1.5, 1.5));   // WPF _labelShadow
                        dc.DrawText(t, at);
                    }
                    if (b.Resisting)
                        dc.DrawGeometry(null, ResistMint, RingArc(new Point(cx, cy), Math.Max(1, size / 2 - 3), b.ResistProgress));
                }
            }
        }
    }
}
