using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Media;
using ConditioningControlPanel.Localization;
using Avalonia.Controls;
using ConditioningControlPanel.Avalonia.Platform;
using ConditioningControlPanel.Avalonia.Controls;
using Avalonia.Threading;
using ConditioningControlPanel.Avalonia.Views.Overlays;
using ConditioningControlPanel.Services;
using ConditioningControlPanel.Services.Chaos;
using Serilog;

namespace ConditioningControlPanel.Avalonia.Views.Chaos
{
    /// <summary>
    /// The run-lifecycle slice of WPF <c>ChaosModeService</c> (App.Chaos): StartRun (:297) opens
    /// the HUD and the overlay and runs the countdown; BeginRun starts the 250 ms RunTick (:929),
    /// which steps <see cref="ChaosRunEngine"/> exactly as WPF does; EndRun (:3200) closes the HUD
    /// and shows the recap; RequestStop (:3148), ForceShutdown (:3161), RunAgain and
    /// OnOverlayClosed tear down the same way.
    ///
    /// <para>The bubble field (WPF SpawnTick :1101, OnBenignPopped :1767) runs on
    /// <see cref="BubbleOverlay"/>: <see cref="SpawnTick"/> on its own timer at WPF's cadence
    /// (ChaosFieldMath), ordinary treats picked by ChaosBubbleVariants, Heavy Drop and the mimic
    /// prism; a pop fires the payload and scores through ChaosFieldMath.ApplyTreatPop/ApplyPrismPop.
    /// Panic, stop and the recap clear the field.</para>
    ///
    /// <para>The hub's FALL IN starts a run here (WPF ChaosHubWindow BtnBegin_Click :1718); EndRun
    /// banks the payout like WPF (:3243-3268): AddXP(base), AwardRunRewards (Sparks + lifetime
    /// stats), RevealService.Sync, the rank card; the loop tip banks gold at every loop boundary
    /// and at a full-course end. Panic (ForceShutdown) pays nothing, as in WPF.</para>
    ///
    /// <para>Live bubbles (pink, spiral, braindrain, video, gif rain) burn a fuse; a press starts
    /// WPF's hold-to-defuse channel (Core AmbientBubble), a completed hold snaps (OnDefused), and a
    /// fuse-out, an escape or a broken hold detonates (OnDetonated): shields, Snap Chain and the
    /// collar pay first, and a detonation makes the loop unclean (no doubled tip, no silk_touch).
    /// The run-end lessons go through Core ChaosRunLessons.</para>
    ///
    /// <para>Darters spawn on WPF's own roll (:1204) and a catch slows time (OnDarterCaught :2351).
    /// ponytail: their flare, bolt, bounce-out and 8 s life come with #3986 (Core StepDarter); until then
    /// a darter roams like any RoamBounce bubble until caught or the field clears.</para>
    ///
    /// <para>ponytail: still NOT here: the Brittle,
    /// the lucky golden (and its gold/Gold Digger droplets), the heart and the behavioral bubbles
    /// (Echo/Bound) - SpawnTick spawns treats, plain live variants, the Tease and the Chaperone; every
    /// motion other than FloatUp, RainDown and Chaperone roaming, chain reaction, cam-girl tips,
    /// treat lessons, the announcer and the achievement credit; slow-mo,
    /// the perf backoff on the cadence; Gif Rain and bouncing text payloads fire; the
    /// first-time bonus pop text.
    /// Defuse and detonation barks and the last breath announcer caller remain missing.
    /// The FX window receives pop, defuse, combo and detonation pulses plus heat tint (also
    /// mirrored to <see cref="PulseFx"/>); screen shake keeps WPF's values through the unseeded
    /// <see cref="ShakeFx"/> hook (chaos-payout-fallin-d). Periodic
    /// KeepChromeTopmost reassertion and mandatory-video re-raise await their run features.
    /// The hold and the Snap Chain window run on wall time (<c>BubbleOverlay.Field.Clock</c>),
    /// as WPF does (:4531, ChaosModeService :2000).</para>
    ///
    /// <para><see cref="Tick"/> is the stepped clock: the DispatcherTimer only calls it, tests call
    /// it directly. It does nothing while the overlay is hidden (P01).</para>
    /// </summary>
    internal static partial class ChaosRunHost
    {
        internal static ChaosPlayMode ActiveMode { get; private set; } = ChaosPlayMode.Story;
        private static ChaosRunState? _state;
        private static ChaosHudWindow? _hud;
        private static ChaosOverlayWindow? _overlay;
        private static ChaosBoonBarOverlay? _boonBar;
        private static DispatcherTimer? _runTimer, _spawnTimer;
        private static int _spawnSerial, _ordinarySpawns, _waveDetonatedAt, _chromeTicks;
        private static int _regenPopCount;
        internal static Action<Window> RaiseChromeWindow = w => X11Overlay.RaiseManaged(w);
        internal static Action<Window> RaiseOverlayWindow = w => X11Overlay.Raise(w);
        internal static Action RaiseBubbles = () => BubbleOverlay.RaiseAll(RaiseOverlayWindow);
        private static DateTime _invulnUntilUtc = DateTime.MinValue;   // Snap Chain, wall time (WPF :1916)
        /// <summary>The host's dice (loop-tip roll, chance flip, prism). Tests seed it and put it back.</summary>
        internal static Random Rng = new();

        /// <summary>WPF BubbleService :4910-4918 and ChaosModeService :1685-1689: bubble
        /// labels and payouts use the pooled overlay. Tests may replace the delivery seam.</summary>
        internal static Action<double, double, string, Color>? PopText = ChaosPopText.Show;
        internal static Action<string, float> RippleCue = (key, volume) =>
        {
            if (key == "ripple_cast") ChaosOverlayWindow.ChaosSfx.PlayRippleCast();
            else ChaosOverlayWindow.ChaosSfx.Play(key, volume);
        };
        private sealed record RippleTarget(ChaosRunState Run, Point[] Centers, double Reach);
        private static RippleTarget? _rippleTarget;
        private static RipplePointer? _ripplePointer;

        // Published on the UI thread; the hook only reads this immutable snapshot.
        internal static void RefreshRippleInput()
        {
            var run = _state;
            Volatile.Write(ref _rippleTarget, _spawning && run != null && !_manualPaused && !_lessonCardPaused && !_draftPaused
                ? new RippleTarget(run, BubbleOverlay.ChaosCenters(), run.RippleRadiusPx + ChaosTuning.RIPPLE_TRIGGER_GRACE_PX)
                : null);
        }

        // WPF ChaosModeService.cs:2569-2585: only a near right-click is consumed.
        internal static bool OnRippleRightDown(PixelPoint px)
        {
            var target = Volatile.Read(ref _rippleTarget);
            if (target == null) return false;
            bool near = false;
            foreach (var c in target.Centers)
            {
                double dx = c.X - px.X, dy = c.Y - px.Y;
                if (dx * dx + dy * dy <= target.Reach * target.Reach) { near = true; break; }
            }
            if (!near || !ReferenceEquals(target, Volatile.Read(ref _rippleTarget))) return false;
            Dispatcher.UIThread.Post(() =>
            {
                if (!ReferenceEquals(target.Run, _state)) return;
                OnBubbleRightDown(new Point(px.X, px.Y), _hud?.Screens.Primary?.Scaling ?? 1);
            });
            return true;
        }

        // WPF ChaosModeService.cs:2590-2624: cast or answer at the physical click.
        internal static bool OnBubbleRightDown(Point px, double primaryScale)
        {
            var run = _state;
            if (!_spawning || run == null || _manualPaused || _lessonCardPaused || _draftPaused) return false;
            if (!run.RippleReady)
            {
                RippleCue("toy_denied", 0.45f);
                run.PushEvent(IconText.BareLoc("Chaos_Ripple_Denied", run.RippleText));
                double scale = primaryScale > 0 ? primaryScale : 1;
                PopText?.Invoke(px.X / scale, px.Y / scale - 30, Loc.GetF("Chaos_Ripple_Gathering", run.RippleText),
                    Color.FromRgb(0x7A, 0xE0, 0xFF));
                return true;
            }
            run.RippleCooldown = run.RippleRechargeSec;
            if (!ChaosMeta.State.SeenRippleTeach)
            {
                ChaosMeta.State.SeenRippleTeach = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce(IconText.BareLoc("Chaos_Ripple_Teach"), ChaosAnnounceKind.PowerUp);
            }
            bool skips = run.MaxedBoons.Contains("skipping_stone");
            void Wave()
            {
                if (!_spawning || !ReferenceEquals(run, _state)) return;
                RippleCue("ripple_cast", 0.6f);
                BubbleOverlay.TriggerPlayerRipple(px, run.RippleRadiusPx, run.RippleLifeMs);
            }
            Wave();
            if (skips)
                for (int i = 1; i <= 2; i++)
                    DispatcherTimer.RunOnce(Wave, TimeSpan.FromMilliseconds(i * ChaosTuning.RIPPLE_WAVE_GAP_MS));
            run.PushEvent(Loc.Get(skips ? "Chaos_Ripple_Skips" : "Chaos_Ripple_Cast"));
            return true;
        }
        /// <summary>Observer of every gated <c>_fx.Pulse(color, strength)</c> (ChaosFxWindow); tests only.</summary>
        internal static Action<Color, double>? PulseFx;
        /// <summary>WPF <c>App.ScreenShake.Shake(intensity, ms)</c>; unseeded until it lands.</summary>
        internal static Action<double, int>? ShakeFx;
        private static ChaosFxWindow? _fx;
        private static double _lastHeatTint = -1;
        private static bool _dvdBannerOn;
        private static int _lastComboBigFired;
        private static DateTime _lastNoFocusAnnounceUtc = DateTime.MinValue;
        private static double _freezeRemainingSec;
        // WPF ChaosModeService.cs:2406-2475, :2813-2817, :2958-2979: the Pendulum's slow-mo window
        // (hand-ported from #4303). Run clock, so it holds through pauses like the freeze.
        internal const double SlowMoFactor = 0.12, SlowMoDurationSec = 6.0;   // WPF :2399-2400
        private static double _slowMoRemainingSec, _pendulumFireAt;
        private static int _pendulumWave;
        private static bool _pendulumFired, _pendulumSlowActive, _slowMoCueOn;
        internal static double SlowMoRemainingSec => _slowMoRemainingSec;
        /// <summary>WPF :1703: x3 pay rides only the pendulum's own swing, and only with Focus Here.</summary>
        internal static double PendulumMultiplier =>
            _pendulumSlowActive && _state?.PendulumPayMult > 1 ? _state.PendulumPayMult : 1.0;
        private static double SpawnSlowFactor => _slowMoRemainingSec > 0 ? SlowMoFactor : 1.0;
        private static bool _active, _spawning, _manualPaused, _endingSoonFired, _lessonCardPaused, _draftPaused;
        private static int _pendingWave, _runsAtStart, _draftsThisRun;
        private static bool _scriptThreat, _scriptSnapped, _scriptDraft, _sinRigged, _sinPending;
        private static bool _lessonCardsAfterDraft, _finalLoopAnnounced;
        private static readonly List<ChaosUnlockCardInfo> _pendingLessonCards = new();

        static ChaosRunHost()
        {
            ChaosLessons.LessonCompleted += OnLessonCompleted;
            // WPF ChaosModeService.cs:126, :210-224: one subscription for every first-time award
            // (pop, defuse, draft pick, sin, toy), so each one floats and feeds once (#3505).
            ChaosFirstTimes.Awarded += OnFirstTimeAwarded;
        }
        private static readonly List<ChaosToyButtonWindow> _toyButtons = new();
        internal static IReadOnlyList<ChaosToyButtonWindow> ToyButtons => _toyButtons;

        internal static ChaosRunState? State => _state;
        internal static ChaosOverlayWindow? Overlay => _overlay;
        internal static ChaosHudWindow? Hud => _hud;
        internal static ChaosBoonBarOverlay? BoonBar => _boonBar;
        internal static ChaosFxWindow? Fx => _fx;
        internal static bool IsActive => _active;
        /// <summary>WPF IsDescending: the clock is running (countdown over, recap not up).</summary>
        internal static bool IsDescending => _spawning;
        internal static bool IsManuallyPaused => _manualPaused;
        /// <summary>Tests: stop the real 4 Hz timer so only stepped <see cref="Tick"/> calls move the clock (P08).</summary>
        internal static void StopTimerForTests() => _runTimer?.Stop();

        public static void StartRun(ChaosRunConfig? config = null, bool isRestart = false)
        {
            if (_active) return;
            CloseLoadoutSidebar();
            if (!isRestart) SeasonFeatureTracking.Track(CoreSettings.Current, ConditioningControlPanel.Models.SeasonFeatureKeys.ChaosMode, ev => App.FeatureDayLog?.Note(ev), DateTime.UtcNow);
            try
            {
                var cfg = config ?? ChaosRunConfig.FromSettings();
                ActiveMode = cfg.PlayMode;
                ChaosRunState.IconResolver = ChaosBoonArt.Resolve;
                _state = new ChaosRunState(cfg);
                _active = true;
                ChaosHubWindow.SetCompanionActive(true);
                _lastHeatTint = -1;
                _hud = new ChaosHudWindow(_state);
                _hud.Show();
                RefreshSidebarLoadout();   // WPF ChaosModeService.cs:342, countdown loadout
                _overlay = ChaosOverlayWindow.ForRun();
                _overlay.OnRunAgain = RunAgain;
                _overlay.OnDismissed = OnOverlayClosed;
                _overlay.Show();
                ChaosEffectBannerOverlay.EnsureCreated();   // WPF :410 birth the banner window NOW, not mid-chaos
                Overlays.ChaosFieldFxOverlay.EnsureCreated();   // WPF ChaosModeService.cs:411
                ChaosTunnelWindow.Open(preload: true);   // WPF ChaosModeService.cs:348
                _overlay.ShowCountdown(BeginRun, shortFlash: isRestart);   // 1s flash on RunAgain
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ChaosRunHost.StartRun failed");
                if (!_active) ChaosHubWindow.SetCompanionActive(false);
                ForceShutdown();
            }
        }

        /// <summary>The countdown's completion. WPF BeginRun also applies the start boon and
        /// lifetime boons and builds the toys; the clock, lifetime boons and toy buttons are here.</summary>
        internal static void BeginRun()
        {
            if (!_active || _state == null || _spawning) return;
            // A countdown skip and a panic press arrive on separate hooks in no fixed order (WPF has the
            // same race: ChaosOverlayWindow.xaml.cs:153 and MainWindow.xaml.cs:961 both BeginInvoke).
            // With a panic already queued, start only after it has run: it usually ends the run, and
            // then nothing (boons, HUD, StartToys) ever starts. Background runs after the panic's Default.
            if (PanicWatchdog.PanicPending) { Dispatcher.UIThread.Post(BeginRun, DispatcherPriority.Background); return; }
            ChaosTunnelWindow.Open();   // WPF ChaosModeService.cs:519-523
            _state.PropertyChanged += OnTunnelStateChanged;
            OnTunnelStateChanged(_state, new System.ComponentModel.PropertyChangedEventArgs(nameof(ChaosRunState.Combo)));
            OnTunnelStateChanged(_state, new System.ComponentModel.PropertyChangedEventArgs(nameof(ChaosRunState.ActIndex)));
            _heavyUntilUtc = DateTime.MinValue;   // WPF ChaosModeService.cs:405
            ChaosBackdropWindow.ShowForRun(ActiveMode, _state.ActIndex); // WPF ChaosModeService.cs:518
            HookRunVideo();   // WPF ChaosModeService.cs:504
            _runsAtStart = ChaosMeta.State.RunsCompleted; _draftsThisRun = 0;
            _scriptThreat = _scriptSnapped = _scriptDraft = _sinRigged = _sinPending = false;
            _finalLoopAnnounced = false;
            // WPF BeginRun :417-420: the run-pick ribbon, rebuilt live on every drafted pick.
            var picks = _state.RunPickTiles;
            _boonBar = ChaosBoonBarOverlay.ForRun();
            _boonBar.SetPicks(picks);
            _boonBar.Show();
            picks.CollectionChanged += (_, _) => _boonBar?.SetPicks(picks);
            // WPF ChaosModeService.cs:422-437: saved start boon, except on the scripted first run.
            var equipped = _state.Config.ScriptedFirstRun ? null : ChaosMeta.State.EquippedStartBoon;
            var boon = ChaosBoonPool.All.FirstOrDefault(b => b.Id == equipped);
            if (boon != null)
            {
                _state.ApplyBoon(boon);
                ChaosMeta.MarkDiscovered("boon:" + boon.Id);
                ChaosAnnouncerOverlay.Announce(boon.Name, ChaosAnnounceKind.Mantra, artKey: boon.Id);
            }
            ChaosRunEffects.ApplyLifetimeBoons(_state);
            RefreshSidebarLoadout();   // WPF :445, lock in the loadout at SINK
            ChaosSkiaFxOverlay.EnsureCreated();   // WPF ChaosModeService.cs:412, idle run-start prewarm
            _hud?.SetPreRunExpanded(false);
            _hud?.SetClockVisible(_state.ShowWaveTimer);
            // WPF :450 Pocket Watch: birth the wave-countdown window now (keep-alive contract).
            if (_state.ShowWaveTimer) ChaosWaveTimerWindow.EnsureCreated();
            if (ChaosMeta.IsBoonActive("e_stim")) ChaosEStimOverlay.EnsureCreated();   // WPF ChaosModeService.cs:453
            _state.StartShields = _state.Shields;
            _thoughtAccumSec = 0;
            StartToys();
            if (ChaosMeta.IsBoonActive("vibe_popping")) ChaosVibeTrailOverlay.EnsureCreated();   // WPF :454-455 (#3506)
            _waveDetonatedAt = _state.Detonated;
            _lastComboBigFired = 0;
            _runDetonations = 0; _focusLowAccumSec = 0; _focusLowBarkFired = false;   // WPF :391, :464
            _lastNoFocusAnnounceUtc = DateTime.MinValue;
            _invulnUntilUtc = DateTime.MinValue;
            _regenPopCount = 0;
            ChaosRunLessons.OnRunStarted();   // WPF :399
            BarkHead.Engine?.Raise("ChaosRunStarted", c => c.Set("difficulty", _state.Config.Difficulty.ToString()));
            _spawning = true;
            _chromeTicks = 0;
            _fx = ChaosFxWindow.Open();   // WPF BeginRun :500, one impact-vignette window per run
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ChaosRunEngine.TICK_SEC) };
            _runTimer.Tick += (_, _) => Tick();
            _runTimer.Start();

            _spawnSerial = _ordinarySpawns = _teaseDenied = 0; _teaseStreakBarked = false;   // WPF :466
            _pendulumWave = 0;   // WPF :402: the swing re-rolls its beat for wave 1
            if (_overlay != null) BubbleOverlay.BeginChaos(_overlay, OnBubblePopped, OnDefused, OnDetonated, CanChannelDefuse, OnTeaseTouched, OnTeaseDenied, OnTreatExpired, OnBoundEnraged);
            RefreshRippleInput();
            _ripplePointer?.Dispose();   // never two grabs if a run restarts without EndField
            _ripplePointer = RipplePointer.Start(OnRippleRightDown);
            _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };   // WPF :510
            _spawnTimer.Tick += (_, _) => SpawnTick();
            _spawnTimer.Start();
            // WPF ChaosModeService.cs:437-438: Welcome Shower equipped as the start boon showers the first GO too.
            if (_state.WelcomeShowerEnabled) SpawnWelcomeShower();
        }

        /// <summary>WPF SpawnTick (:1101), the treat half. Re-times its own timer like WPF.</summary>
        internal static void SpawnTick()
        {
            if (!_spawning || _state == null || _manualPaused || _lessonCardPaused || _draftPaused || _freezeRemainingSec > 0) return;
            if (_overlay is not { IsVisible: true }) return;   // P01
            var cfg = _state.Config;
            double intensity = _state.RunIntensity;
            double effIntensity = ChaosFieldMath.EffectiveIntensity(intensity, cfg.DifficultyMult);
            if (BubbleOverlay.ChaosCount < ChaosFieldMath.MaxConcurrent(intensity, cfg.DifficultyMult))
            {
                IReadOnlyCollection<string> enabled = (cfg.EnabledVariants ?? ChaosBubbleVariants.AllIds())
                    .Where(id => ChaosBubbleVariants.All.Any(v => v.Id == id)).ToList();
                double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
                double waveLeft = waveLen - (_state.ElapsedSec % waveLen);
                double runLeft = _state.RunDurationSec - _state.ElapsedSec;
                if (enabled.Contains("video") && (HeavyEffectActive || waveLeft < 14 || runLeft < 18))
                    enabled = enabled.Where(id => id != "video").ToList();

                if (!cfg.ScriptedFirstRun && ChaosMeta.AtLeast(ChaosRank.Tempted)
                    && Rng.NextDouble() < ChaosTuning.CHAPERONE_SPAWN_CHANCE * (cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1.0))
                {
                    SpawnChaperone(effIntensity);
                    if (_spawnTimer != null)
                        _spawnTimer.Interval = TimeSpan.FromMilliseconds(ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult, SpawnSlowFactor));
                    return;
                }
                // WPF ChaosModeService.cs:1281-1298: Bound replaces the ordinary slot.
                if (!cfg.ScriptedFirstRun && (cfg.Difficulty >= ChaosDifficulty.Hard || ChaosMeta.AtLeast(ChaosRank.Entranced))
                    && Rng.NextDouble() < ChaosTuning.BOUND_SPAWN_CHANCE * (cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1.0))
                {
                    bool debut = !ChaosMeta.State.SeenBound;
                    if (debut)
                    {
                        ChaosMeta.State.SeenBound = true;
                        ChaosMeta.Save();
                        ChaosAnnouncerOverlay.Announce($"{IconMap.Bound} {Loc.Get("chaos_bound_title")}: {Loc.Get("chaos_bound_hint")}", ChaosAnnounceKind.Item,
                            artKey: "bound", subText: Loc.Get("chaos_bound_hint"));
                        _state.PushEvent($"{IconMap.Bound} {Loc.Get("chaos_bound_event")}");
                    }
                    var (a, b) = ChaosBubbleVariants.BuildBoundPair(effIntensity, _state.FuseTimeMult,
                        cfg.EffectIntensity, _state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0);
                    ChaosMeta.MarkDiscovered("bubble:bound");
                    BubbleOverlay.SpawnBoundPair(a, b, cfg.HitboxScale, _state.MagnetEnabled);
                    if (_spawnTimer != null) _spawnTimer.Interval = TimeSpan.FromMilliseconds(
                        ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult, SpawnSlowFactor));
                    return;
                }
                // WPF :1301 (after the Chaperone roll at :1264): a Tease replaces the ordinary slot, never the scripted first run.
                if (TrySpawnTease(cfg, effIntensity))
                {
                    if (_spawnTimer != null) _spawnTimer.Interval = TimeSpan.FromMilliseconds(
                        ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult, SpawnSlowFactor));
                    return;
                }

                EffectBubbleSpec spec;
                if (_state.HeavyDropEvery > 0 && ++_spawnSerial % _state.HeavyDropEvery == 0)
                    spec = ChaosBubbleVariants.BuildHeavy(effIntensity, cfg.EffectIntensity, _state.BubbleScale);
                else
                {
                    double sideDrift = _ordinarySpawns < ChaosTuning.SIDE_DRIFT_GRACE_SPAWNS ? 0 : ChaosTuning.SIDE_DRIFT_CHANCE;
                    spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult, cfg.MotionOverride,
                        enabled.Count > 0 ? enabled : new[] { ChaosBubbleVariants.All[0].Id },
                        cfg.EffectIntensity, _state.BubbleScale, sideDrift);
                }
                if (spec.IsFreeze && BubbleOverlay.Field.Bubbles.Count(b => b.Spec?.IsFreeze == true && !b.Popping) >= ChaosTuning.FREEZE_MAX_ON_SCREEN)
                {
                    var noFreeze = enabled.Where(id => id != "bambifreeze").ToList();
                    spec = ChaosBubbleVariants.Pick(effIntensity, _state.FuseTimeMult, cfg.MotionOverride,
                        noFreeze.Count > 0 ? noFreeze : new[] { ChaosBubbleVariants.All[0].Id },
                        cfg.EffectIntensity, _state.BubbleScale, 0);
                }
                _ordinarySpawns++;
                ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId);
                // WPF ChaosModeService.cs:371, :378 samples the current boon/upgrade at spawn.
                BubbleOverlay.SpawnChaos(spec, hitboxScale: cfg.HitboxScale, liveMagnet: _state.MagnetEnabled);

                // WPF ChaosModeService.cs:1165: a bonus roll accompanies each ordinary spawn.
                if (Rng.NextDouble() < _state.GoldenChance)
                {
                    ChaosMeta.MarkDiscovered("bubble:golden");
                    var golden = ChaosBubbleVariants.BuildGolden();
                    BubbleOverlay.SpawnChaos(golden, motion: golden.Motion);
                    PlayChime(0.30f);   // WPF :1172
                }

                if (_state.PrismChance > 0 && Rng.NextDouble() < _state.PrismChance)
                {
                    ChaosMeta.MarkDiscovered("bubble:prism");
                    BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildPrism(effIntensity, cfg.EffectIntensity,
                        treatOnly: _state.PrismTreatOnly), hitboxScale: cfg.HitboxScale, liveMagnet: _state.MagnetEnabled);
                }
            }
            // WPF :1202-1212: darters roll on their own, independent of the bubble cap.
            if (cfg.DartersEnabled && RollDarter(effIntensity, _state.RabbitRateMult) is { } darter)
            {
                ChaosMeta.MarkDiscovered("bubble:darter");
                BubbleOverlay.SpawnChaos(darter, motion: darter.Motion);
            }
            if (_spawnTimer != null)
                _spawnTimer.Interval = TimeSpan.FromMilliseconds(
                    ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult, SpawnSlowFactor));
        }

        /// <summary>The darter roll (Core ChaosBubbleVariants.RollDarter). Tests swap it.</summary>
        internal static Func<double, double, EffectBubbleSpec?> RollDarter = (i, rate) => ChaosBubbleVariants.RollDarter(i, rate);

        /// <summary>WPF OnDarterCaught (:2351-2375): score, focus, combo; time slows, or a catch while
        /// slow-mo already runs tops it up by 0.8 s (and hands a pendulum swing back to normal pay).</summary>
        internal static void OnDarterCaught(bool quick)
        {
            if (_state == null) return;
            _state.Score += (ChaosBubbleVariants.DARTER_BASE_POINTS + (quick ? ChaosBubbleVariants.DARTER_QUICK_BONUS : 0)) * _state.TotalMult;
            _state.Focus += ChaosTuning.FOCUS_PER_RABBIT;
            _state.Combo++;
            _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
            CreditPop();
            ChaosLessons.Tick("rabbit_caller");   // WPF ChaosLessonHooks.OnRabbitCaught
            bool extended = _slowMoRemainingSec > 0;
            if (extended) { _slowMoRemainingSec += 0.8; _pendulumSlowActive = false; }
            else ActivateSlowMo(SlowMoDurationSec + _state.SlowMoBonusSec, pendulum: false, Loc.Get("chaos_slowmo_banner"));
            Pulse(Color.FromRgb(120, 200, 255), quick ? 0.32 : 0.24);
            _state.PushEvent(Loc.Get(extended ? "chaos_darter_event_extended" : quick ? "chaos_darter_event_quick" : "chaos_darter_event"));
            CheckComboMilestone();
        }

        internal static bool TrySpawnTease(ChaosRunConfig cfg, double intensity)
        {
            if (_state == null || cfg.ScriptedFirstRun || !ChaosMeta.AtLeast(ChaosRank.Slipping)
                || Rng.NextDouble() >= ChaosTuning.TEASE_SPAWN_CHANCE * (cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1.0)) return false;
            if (!ChaosMeta.State.SeenTease)
            {
                ChaosMeta.State.SeenTease = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_tease_debut"), ChaosAnnounceKind.Temptation, artKey: "tease");
                TeaseBark("ChaosTeaseDebut", null);
            }
            ChaosMeta.MarkDiscovered("bubble:tease");
            BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildTease(intensity, cfg.EffectIntensity, _state.BubbleScale));
            return true;
        }

        // WPF ChaosModeService.cs:1329: resistance absorbs only the payload, never the streak cost.
        internal static void OnTeaseTouched(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            _state.Detonated++;
            _runDetonations++;   // WPF :1333
            ChaosRunLessons.OnDetonation();
            Label(b, "✖", Color.FromRgb(0xFF, 0x3D, 0x5A));
            if (_state.Shields > 0)
            {
                _state.Shields--;
                _state.Heat = Math.Max(0, _state.Heat - 0.2);
                ChaosOverlayWindow.ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
            }
            else
            {
                FirePayloadForDetonation(spec);
                _state.EffectsFired++;
                ChaosOverlayWindow.ChaosSfx.Play("trigger", 0.55f);
                Shake(0.3 + spec.Strength / 100.0 * 0.4, 320);
                _hud?.FlashShields(false);
            }
            _state.Combo = _state.Combo > 1 ? _state.Combo / 2 : 0;
            _lastComboBigFired = 0;
            Pulse(Color.FromRgb(0xFF, 0x3D, 0x5A), 0.38);
            TeaseBark("ChaosTeaseClicked", null);
        }

        // WPF BarkService.cs:554-562 through the Core engine the head seeds.
        internal static Action<string, int?> TeaseBark = (t, n) => BarkHead.Engine?.Raise(t, n is int d ? c => c.Set("denied_count", (double)d) : null);
        private static int _teaseDenied;
        private static bool _teaseStreakBarked;
        // WPF BarkService.cs:488-552: the run barks (WPF ChaosModeService call sites cited at each use).
        internal static Action<string, Action<ConditioningControlPanel.Services.Bark.BarkContext>?> RunBark = (t, f) => BarkHead.Engine?.Raise(t, f);
        private static int _runDetonations;          // WPF :84, absorbed and bare
        private static double _focusLowAccumSec;     // WPF :1029-1040, once per run
        private static bool _focusLowBarkFired;
        private static void FirstBark(bool seen, Action mark, string trigger)
        {
            if (seen) return;
            mark(); ChaosMeta.Save();
            RunBark(trigger, null);
        }

        // WPF ChaosModeService.cs:1403: restraint pays gold, score and focus once.
        internal static void OnTeaseDenied(AmbientBubble b)
        {
            if (_state == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            int gold = ChaosRunEngine.GoldScaled(_state, Rng.Next(ChaosTuning.TEASE_GOLD_MIN, ChaosTuning.TEASE_GOLD_MAX + 1));
            BankGold(gold);
            if (ChaosMeta.State.BubbleHintsLearned.Add("tease")) ChaosMeta.Save();
            string announcement = Loc.GetF("chaos_tease_denied_gold", gold);
            ChaosAnnouncerOverlay.Announce(announcement, ChaosAnnounceKind.PowerUp, artKey: "denied", subText: announcement);
            double pts = ChaosTuning.TEASE_DENIED_SCORE * _state.TotalMult * _state.BlindfoldPayMult;
            _state.Score += pts;
            _state.Focus += ChaosTuning.FOCUS_PER_DENIED;
            Label(b, Loc.Get("chaos_tease_denied"), Color.FromRgb(0xFF, 0xD7, 0));
            ShowPopScore(pts);
            Pulse(Color.FromRgb(0xFF, 0xD7, 0), 0.25);
            TeaseBark("ChaosTeaseDenied", ++_teaseDenied);   // WPF :1416-1421
            if (_teaseDenied >= ChaosTuning.TEASE_DENIED_STREAK_COUNT && !_teaseStreakBarked)
            { _teaseStreakBarked = true; TeaseBark("ChaosTeaseDeniedStreak", _teaseDenied); }
        }

        // WPF ChaosModeService.cs:1394 with Bubble.Enrage's label (BubbleService.cs:2760).
        internal static void OnBoundEnraged(AmbientBubble b)
        {
            if (_state == null) return;
            Label(b, Loc.Get("chaos_bound_enraged"), Color.FromRgb(0xFF, 0x5A, 0x5A));
            ChaosOverlayWindow.ChaosSfx.Play("toy_denied", 0.5f);
            Pulse(Color.FromRgb(0xFF, 0x4A, 0x4A), 0.30);
            _state.PushEvent($"{IconMap.Bound} {Loc.Get("chaos_bound_enraged_event")}");
        }

        internal static AmbientBubble SpawnChaperone(double intensity)
        {
            var state = _state!;
            bool debut = !ChaosMeta.State.SeenChaperone;
            if (debut)
            {
                ChaosMeta.State.SeenChaperone = true;
                ChaosMeta.Save();
                ChaosAnnouncerOverlay.Announce($"{IconMap.Chaperone} {Loc.Get("chaos_chaperone_title")}: {Loc.Get("chaos_chaperone_hint")}", ChaosAnnounceKind.Item,
                    artKey: "chaperone", subText: Loc.Get("chaos_chaperone_hint"));
                state.PushEvent($"{IconMap.Chaperone} {Loc.Get("chaos_chaperone_event")}");
            }
            var (liveSpec, escortSpec) = ChaosBubbleVariants.BuildChaperonePair(intensity, state.FuseTimeMult,
                state.Config.EffectIntensity, state.BubbleScale, debut ? ChaosTuning.DEBUT_FUSE_MULT : 1.0);
            ChaosMeta.MarkDiscovered("bubble:chaperone");
            var live = BubbleOverlay.SpawnChaos(liveSpec);
            var escort = BubbleOverlay.SpawnChaos(escortSpec);
            escort.Screen = live.Screen;
            escort.OrbitTarget = live;
            live.InsetChaperoneBounds(escort.Size);
            double radius = Math.Max(ChaosTuning.CHAPERONE_ORBIT_RADIUS_DIP,
                (live.Size + escort.Size) / 2 + ChaosTuning.CHAPERONE_ORBIT_GAP_DIP);
            escort.X = live.CenterX + radius - escort.Size / 2;
            escort.Y = live.CenterY - escort.Size / 2;
            live.Escort = escort;
            return live;
        }

        /// <summary>WPF OnBenignPopped (:1767): the prism and ordinary-treat branches.</summary>
        internal static void OnBubblePopped(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            if (spec.IsDarter)   // WPF BubbleService.cs:4712-4716: the catch label, then the slow-mo
            {
                Label(b, Loc.Get("chaos_slowmo_banner").ToUpperInvariant(), Color.FromUInt32(spec.Tint));
                // ponytail: the quick-catch bonus needs the darter's active clock (WPF BubbleService.cs:3875,
                // :4714), part of the darter motion in #3986; until it lands every catch is a normal catch.
                OnDarterCaught(quick: false);
                return;
            }
            EffectLabel(b);   // WPF BubbleService.cs:4710-4712: label before the pickup or treat callback
            if (spec.IsFreeze)
            {
                _state.Score += 140 * _state.TotalMult;
                _state.Combo++;
                _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
                CreditPop();
                ChaosLessons.Tick("freeze_trigger");
                ActivateFreeze();
                RunBark("ChaosFreezeCaught", c => c.Set("points", 140 * _state.TotalMult).Set("combo", (double)_state.Combo));   // WPF :2391, before the milestone so it wins the 60 s bark gap
                _state.PushEvent(Loc.Get("chaos_freeze_event"));   // WPF :2392 prefixes a snowflake; no emoji in event lines
                CheckComboMilestone();
                return;
            }
            CountRegenPop();   // WPF ChaosModeService :1768
            // WPF ChaosModeService.cs:1786-1820: gold pickups bypass score and combo.
            if (spec.IsGolden || spec.IsDroplet)
            {
                _state.Focus += spec.IsGolden ? ChaosTuning.FOCUS_PER_GOLDEN : ChaosTuning.FOCUS_PER_DROPLET;
                int min, max;
                if (spec.IsGolden)
                {
                    int level = ChaosMeta.IsBoonActive("rabbits_foot") ? ChaosMeta.BoonLevel("rabbits_foot") : 0;
                    (min, max) = ChaosLifetimeBoons.GoldenPayRange(level);
                }
                else { min = 3; max = 7; }
                int gold = ChaosRunEngine.GoldScaled(_state, Rng.Next(min, max + 1));
                BankGold(gold);
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                    $"+{gold} {ChaosGlyphs.Gold}", Color.FromRgb(255, 215, 0));
                _state.PushEvent(spec.IsGolden ? $"{ChaosGlyphs.Gold} lucky bubble +{gold} gold" : $"{ChaosGlyphs.Gold} droplet +{gold} gold");
                if (spec.IsGolden) ChaosAnnouncerOverlay.Announce($"{ChaosGlyphs.Gold} +{gold} gold", ChaosAnnounceKind.PowerUp);
                Pulse(Color.FromRgb(255, 215, 0), spec.IsGolden ? 0.35 : 0.12);
                ChaosOverlayWindow.ChaosSfx.Play("golden_pop", spec.IsGolden ? 0.6f : 0.35f);
                if (spec.IsGolden && _state.GoldDiggerEnabled)
                {
                    var center = BubbleOverlay.CenterPx(b);
                    for (int i = 0; i < 3; i++)
                        BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildGoldDroplet(
                            center.X + Rng.Next(-50, 51), center.Y + Rng.Next(-20, 21)), motion: ChaosMotion.RainDown);
                    _state.PushEvent("gold digger: it spills");   // WPF :1820 prefixes a pickaxe; no emoji in event lines
                }
                return;
            }
            if (spec.IsPrism)
            {
                FireScaledPayload(spec.Payload);
                double prismPts = ChaosFieldMath.ApplyPrismPop(_state, spec);
                ShowPopScore(prismPts);
                ChaosLessons.Tick("taking_chances");   // WPF ChaosLessonHooks.OnPrismPopped
                _state.PushEvent($"🔮 prism! 10x · {spec.Payload.DisplayName} fires");
                Pulse(Color.FromRgb(0xC8, 0xA8, 0xFF), 0.40);
                CreditPop();   // WPF ChaosModeService.cs:1844
                RollCamGirlTip();
                CheckComboMilestone();
                return;
            }
            spec.Payload.Fire();   // benign pop = a treat
            FirstTime(ChaosFirstTimes.Taste);   // WPF ChaosModeService :1844
            if (spec.VariantId == "subliminal") ChaosLessons.Tick("intrusive_thoughts");   // WPF OnTreatPopped
            bool focusWasLow = _state.FocusLow;
            double pts = ChaosFieldMath.ApplyTreatPop(_state, spec, pendulum: PendulumMultiplier, ChaosFieldMath.ChanceFlip(_state, Rng));
            if (focusWasLow)
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 34,
                    $"+{(int)ChaosFieldMath.TreatFocusGain(spec.PayMult)} FOCUS", Color.FromRgb(0x7A, 0xE0, 0xFF));
            ShowPopScore(pts);
            CreditPop();   // WPF ChaosModeService.cs:1873
            Pulse(Color.FromRgb(255, 200, 235), 0.10);
            if (spec.PayMult > 1) _state.PushEvent("🪨 heavy drop! x3");
            RollCamGirlTip();
            BodyBuzz(b);
            // ponytail: GG make more GG (WPF ChaosModeService.cs:1879-1888) waits for darter spawn and movement (lane s10-chaos-darters); spawning now would give catchable, paying floaters.
            RunBark("ChaosBenignPopped", c => c.Set("variant_id", spec.VariantId).Set("payload", spec.Payload.DisplayName).Set("combo", (double)_state.Combo));   // WPF :1890
            CheckComboMilestone();
        }

        /// <summary>WPF AchievementService.TrackBubblePopped (:570-609): a Chaos pop counts toward the lifetime
        /// total (its 100-pop Sparkle) and credits quests, as an ambient pop does in BubbleOverlay.Pop.</summary>
        private static void CreditPop()
        {
            App.Achievements?.TrackBubblePopped();
            try { App.Quests?.TrackBubblePopped(); } catch (Exception ex) { Serilog.Log.Debug("chaos pop quest credit: {E}", ex.Message); }
        }

        /// <summary>WPF BubbleService.Dissolve :4864 and ChaosModeService.OnTreatExpired :1898.</summary>
        internal static void OnTreatExpired(AmbientBubble b)
        {
            if (_state == null || b.Spec == null || _manualPaused || _lessonCardPaused) return;
            Label(b, Loc.Get("chaos_expiry_label"), Color.FromRgb(0xB8, 0xB8, 0xD0));
            string name = b.Spec.IsGolden ? Loc.Get("chaos_expiry_name_lucky") : b.Spec.Payload.DisplayName switch
            {
                "flash" => Loc.Get("chaos_expiry_name_flash"),
                "subliminal" => Loc.Get("chaos_expiry_name_subliminal"),
                "pink_filter" => Loc.Get("chaos_expiry_name_pink"),
                "spiral" => Loc.Get("chaos_expiry_name_spiral"),
                "braindrain" => Loc.Get("chaos_expiry_name_braindrain"),
                "gif cascade" => Loc.Get("chaos_expiry_name_gif"),
                _ => b.Spec.Payload.DisplayName
            };
            if (_state.Combo > 1)
            {
                _state.Combo /= 2;
                _state.PushEvent(IconLabel.ForKey("chaos_expiry_halved", name, _state.Combo).Source);
            }
            else
            {
                _state.Combo = 0;
                _state.PushEvent(IconLabel.ForKey("chaos_expiry_gone", name).Source);
            }
            Pulse(Color.FromRgb(150, 150, 175), 0.10);
        }

        /// <summary>WPF CanChannelDefuse (:1927): the focus must cover the snap's cost (paid on completion).</summary>
        internal static bool CanChannelDefuse(AmbientBubble b)
        {
            if (b.Popping || b.Channeling || !b.IsLiveThreat) return false;
            bool approved = _state != null && (_freezeRemainingSec > 0 || b.Spec is not { } spec || _state.Focus >= ChaosFieldMath.DefuseCost(spec));
            if (approved) ChaosRunLessons.OnChannelStarted(BubbleOverlay.Field.Clock());
            return approved;
        }

        /// <summary>WPF OnDefused (:1984): the core snap (cost, counts, score, Drip Feed), the Snap
        /// Chain window, Playing with Fire's brink gold and the Pendulum's scoring window.</summary>
        internal static void OnDefused(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused) return;
            if (_state.Config.ScriptedFirstRun && _scriptThreat && !_scriptSnapped)
            {
                _scriptSnapped = true;
                var enabled = _state.Config.EnabledVariants;
                if (enabled != null) foreach (var id in new[] { "pink", "spiral" }) if (!enabled.Contains(id)) enabled.Add(id);
            }
            Label(b, "SNAP", SnapColor);   // WPF CompleteDefuse :4577 / Pop :4684
            double fuseSecLeft = b.DefuseJudgeFuseSec;
            if (_state.DefuseInvulnMs > 0) _invulnUntilUtc = BubbleOverlay.Field.Clock().AddMilliseconds(_state.DefuseInvulnMs);   // WPF :2000
            CountRegenPop();   // WPF ChaosModeService :2001
            FirstTime(ChaosFirstTimes.Snap);
            if (b.DefusedViaChannel) ChaosRunLessons.OnChannelEnded(BubbleOverlay.Field.Clock());
            if (b.DefusedViaChannel) FirstBark(ChaosMeta.State.SeenBarkDefuseFirst, () => ChaosMeta.State.SeenBarkDefuseFirst = true, "ChaosDefuseFirst");   // WPF :1990-1996
            ChaosRunLessons.OnDefuseCompleted(fuseSecLeft, b.DefusedViaChannel);
            var (pts, lastBreath, slowburn) = ChaosFieldMath.ApplyDefuse(_state, spec, fuseSecLeft,
                b.DefusedViaChannel, free: _freezeRemainingSec > 0, pendulum: PendulumMultiplier, ChaosFieldMath.ChanceFlip(_state, Rng));
            // WPF ChaosLessonHooks.cs:161, 250: live rain remains busy after its spawn window.
            if (ChaosGifCascadeOverlay.IsRaining) ChaosLessons.Tick("blindfold");
            ShowPopScore(pts);
            CreditPop();   // WPF ChaosModeService.cs:2025
            RollCamGirlTip();
            if (_state.LastSecondGoldEnabled && fuseSecLeft <= 1.0)
            {
                int gold = ChaosRunEngine.GoldScaled(_state, Rng.Next(5, 10));
                BankGold(gold, floatAtPop: true);
                _state.PushEvent($"{ChaosGlyphs.Gold} playing with fire +{gold} gold");
                Pulse(Color.FromRgb(255, 140, 60), 0.30);
            }
            // WPF ChaosModeService.cs:2035-2040 (#3502, #4305): a brink snap leaves Aftermath residue.
            if (_state.AftermathEnabled && fuseSecLeft <= 1.5)
            {
                BubbleOverlay.AddChaosResidue(b);
                _state.PushEvent(IconLabel.ForKey("chaos_aftermath_event").Source);
            }
            // WPF ChaosModeService.cs:2041-2043: every snap emits Size Queen.
            if (_state.RippleEnabled) BubbleOverlay.TriggerChaosRipple(b);
            RunBark("ChaosBubbleDefused", c => c.Set("combo", (double)_state.Combo).Set("payload", spec.VariantId).Set("difficulty", _state.Config.Difficulty.ToString()));   // WPF :2037
            if (lastBreath > 1)
            {
                _state.PushEvent($"⏱ last breath! x{lastBreath:0}");
                Pulse(Color.FromRgb(255, 215, 0), 0.40);   // gold flash (WPF :2045)
            }
            else Pulse(Color.FromRgb(90, 255, 150), 0.16);   // soft green confirm
            if (slowburn > 1) _state.PushEvent("🐌 slow burn! x3");
            _state.PushEvent($"✔ snapped {spec.Payload.DisplayName}");
            CheckComboMilestone();
            // ponytail: blindfold lesson and
            // the brink narrative -> chaos-payout-fallin-d.
        }

        /// <summary>WPF OnDetonated (:2055) + OnChannelBroken's feed line: the payload fires, the loop
        /// is no longer clean, and the cheapest protection pays (ChaosFieldMath.ApplyDetonation).</summary>
        internal static void OnDetonated(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused) return;
            if (b.BrokenReason is "click" or "release" or "nofocus")
                ChaosRunLessons.OnChannelEnded(BubbleOverlay.Field.Clock());
            switch (b.BrokenReason)
            {
                case "nofocus":   // WPF OnPlayerPress :4502 + OnChannelBroken :1943
                    Label(b, "NO FOCUS", Color.FromRgb(0xFF, 0x46, 0x46), -(b.Size / 2.0 + 30));
                    ChaosOverlayWindow.ChaosSfx.Play("focus_empty", 0.55f);
                    Pulse(Color.FromRgb(255, 80, 80), 0.22);
                    _hud?.FlashFocusBar();
                    // WPF ChaosModeService.cs:1951-1956: do not queue a banner for every mis-grab.
                    var now = BubbleOverlay.Field.Clock();
                    if ((now - _lastNoFocusAnnounceUtc).TotalSeconds >= 1.5)
                    {
                        _lastNoFocusAnnounceUtc = now;
                        ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_no_focus_announce"), ChaosAnnounceKind.Temptation, holdMs: 1300);
                    }
                    _state.PushEvent("✋ no focus — it triggers in your grip");
                    FirstBark(ChaosMeta.State.SeenBarkDefuseNoFocus, () => ChaosMeta.State.SeenBarkDefuseNoFocus = true, "ChaosDefuseNoFocus");   // WPF :1959-1963
                    break;
                case "click":
                    _state.PushEvent("💥 a tap isn't a hold");
                    FirstBark(ChaosMeta.State.SeenBarkClickDetonate, () => ChaosMeta.State.SeenBarkClickDetonate = true, "ChaosClickDetonate");   // WPF :1967-1971
                    break;
                case "release":
                    _state.PushEvent("💥 you let go");
                    FirstBark(ChaosMeta.State.SeenBarkDefuseRelease, () => ChaosMeta.State.SeenBarkDefuseRelease = true, "ChaosDefuseRelease");   // WPF :1975-1979
                    break;
            }
            // Other payload families still need their runners.
            EffectLabel(b);   // WPF Detonate :4862
            FirePayloadForDetonation(spec);
            ChaosRunLessons.OnDetonation();   // silk_touch: the loop is no longer clean
            string name = spec.Payload.DisplayName;
            double s = spec.Strength / 100.0;
            _runDetonations++;   // WPF :2068
            int comboBeforeBreak = _state.Combo;   // WPF :2122
            var outcome = ChaosFieldMath.ApplyDetonation(_state, BubbleOverlay.Field.Clock() < _invulnUntilUtc);
            string diff = _state.Config.Difficulty.ToString();
            bool bare = outcome is not (ChaosFieldMath.Detonation.SnapChainHeld or ChaosFieldMath.Detonation.ShieldAbsorbed or ChaosFieldMath.Detonation.CollarHeld);
            switch (outcome)
            {
                case ChaosFieldMath.Detonation.SnapChainHeld:   // WPF :2081
                    _state.PushEvent($"⛓ snap chain holds ({name})");
                    Pulse(Color.FromRgb(0x7A, 0xE0, 0xFF), 0.22);
                    break;
                case ChaosFieldMath.Detonation.ShieldAbsorbed:   // WPF :2091
                    _state.PushEvent($"♥ resistance crumbles ({name})");
                    ChaosOverlayWindow.ChaosSfx.Play(_state.Shields == 0 ? "resist_crumble" : "resist_absorb", 0.6f);
                    Pulse(Color.FromRgb(80, 160, 255), 0.28);
                    Shake(0.25 + s * 0.3, 280);
                    break;
                case ChaosFieldMath.Detonation.CollarHeld:   // WPF :2104
                    _hud?.FlashShields(false);
                    _state.PushEvent($"📿 the collar holds ({_state.CollarSaves} left)");
                    ChaosOverlayWindow.ChaosSfx.Play("collar_save", 0.6f);
                    Pulse(Color.FromRgb(255, 215, 0), 0.32);
                    Shake(0.25 + s * 0.3, 280);
                    if (_state.UnleashedEnabled)
                    {
                        BubbleOverlay.Field.DefuseAllLive(BubbleOverlay.BounceShield);
                        ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_unleashed_announce"), ChaosAnnounceKind.PowerUp, artKey: "unleashed");   // WPF :2114
                        _state.PushEvent("📿 unleashed — the field lets go");
                        Pulse(Color.FromRgb(255, 215, 0), 0.50);
                    }
                    break;
                default:   // WPF :2126
                    _lastComboBigFired = 0;
                    _hud?.FlashShields(false);
                    _state.PushEvent($"💥 {name} triggered!");
                    ChaosOverlayWindow.ChaosSfx.Play("trigger", 0.55f);
                    Pulse(Color.FromRgb(255, 50, 50), 0.4 + s * 0.35);
                    Shake(0.4 + s * 0.5, 380);
                    break;
            }
            // WPF :2083, :2098, :2118 (absorbed) and :2132 (bare, combo captured before the break):
            // each WPF branch barks last, so Unleashed defuse barks speak first and the combo includes them.
            RunBark(bare ? "ChaosBubbleDetonated" : "ChaosBubbleDetonatedAbsorbed", c =>
            {
                c.Set("payload", spec.VariantId).Set("strength", spec.Strength).Set("shield_absorbed", !bare)
                 .Set("run_detonations", (double)_runDetonations).Set("combo", (double)(bare ? comboBeforeBreak : _state.Combo)).Set("difficulty", diff);
                if (!bare) c.Set("shields_left", (double)_state.Shields);
            });
            // ponytail: the first-bare-detonation narrative -> chaos-payout-fallin-d.
        }

        /// <summary>WPF SnapColor (BubbleService.cs:4869): the cool "safe" accent of a snap.</summary>
        private static readonly Color SnapColor = Color.FromRgb(0x7A, 0xE0, 0xFF);

        /// <summary>WPF ShowChaosLabel (:4884): a word at the bubble's centre, via the pop text.</summary>
        private static void Label(AmbientBubble b, string word, Color color, double yOffset = 0)
        {
            if (!string.IsNullOrEmpty(word)) PopText?.Invoke(b.CenterX, b.CenterY + yOffset, word, color);
        }

        /// <summary>WPF ShowChaosEffectLabel (:4877): the variant's pop word in its tint.</summary>
        private static void EffectLabel(AmbientBubble b)
        {
            if (b.Spec != null) Label(b, ChaosBubbleVariants.PopWordFor(b.Spec.VariantId), Color.FromUInt32(b.Spec.Tint));
        }

        // WPF ChaosModeService.cs:108-112.
        private static readonly Color ShieldGainColor = Color.FromRgb(120, 220, 160), WaveClearColor = Color.FromRgb(150, 200, 255);
        private const double ShieldGainPulse = 0.22, WaveClearPulse = 0.30;

        /// <summary>WPF Pulse (:1672): gated by the run's colour-flash toggle.</summary>
        private static void Pulse(Color color, double strength)
        {
            if (_state?.Config.ColorFlashesEnabled != true) return;
            _fx?.Pulse(color, strength);
            PulseFx?.Invoke(color, strength);
        }

        /// <summary>WPF Shake (:1676): gated and scaled by the run's shake settings.</summary>
        private static void Shake(double baseIntensity, int durMs)
        {
            var cfg = _state?.Config;
            if (cfg?.ScreenShakeEnabled == true) ShakeFx?.Invoke(Math.Clamp(baseIntensity * cfg.ShakeIntensity, 0, 1), durMs);
        }

        // WPF CheckComboMilestone: preserve call order, including the micro/×10 pulse that
        // replaces a big-threshold pulse on the same layer in the same callback.
        private static readonly int[] ComboBigThresholds = { 25, 50, 100 };

        private static void CheckComboMilestone()
        {
            int combo = _state?.Combo ?? 0;
            if (combo <= 0) return;
            foreach (int t in ComboBigThresholds)
            {
                if (combo < t || _lastComboBigFired >= t) continue;
                _lastComboBigFired = t;
                _state!.PushEvent($"{ConditioningControlPanel.Localization.Loc.Get("label_streak")} x{combo}");
                RunBark("ChaosComboBig", c => c.Set("combo", (double)combo).Set("threshold", (double)t));   // WPF :3080
                ChaosOverlayWindow.ChaosSfx.Play("streak_milestone", 0.5f);   // WPF :3081
                ChaosAnnouncerOverlay.Announce($"{ConditioningControlPanel.Localization.Loc.Get("label_streak")} ×{t}", ChaosAnnounceKind.Streak,
                    artKey: "streak", subText: $"×{t}");                       // WPF :3082
                Pulse(Color.FromRgb(255, 230, 120), 0.55);
            }
            if (combo % 10 == 0)
            {
                _state!.PushEvent($"{ConditioningControlPanel.Localization.Loc.Get("label_streak")} x{combo}");
                RunBark("ChaosComboMilestone", c => c.Set("combo", (double)combo).Set("difficulty", _state.Config.Difficulty.ToString()));   // WPF :3091
                Pulse(Color.FromRgb(255, 200, 60), 0.4);
            }
            else Pulse(Color.FromRgb(255, 170, 80), 0.04 + 0.08 * ((combo % 10) / 10.0));
        }

        /// <summary>WPF CountRegenPop (:2427), Slow Recovery: every N pops regrow one resistance,
        /// up to what the run descended with.</summary>
        private static void CountRegenPop()
        {
            if (_state == null || _state.ShieldRegenPops <= 0) return;
            if (_state.Shields >= _state.StartShields) { _regenPopCount = 0; return; }
            if (++_regenPopCount < _state.ShieldRegenPops) return;
            _regenPopCount = 0;
            _state.Shields += 1;
            _state.PushEvent($"♥ {Loc.Get("chaos_resistance_regrows")}");
            Pulse(Color.FromRgb(120, 220, 160), 0.18);   // SHIELD_GAIN_COLOR
        }

        /// <summary>WPF ChaosFirstTimes.TryAward: bank once; <see cref="OnFirstTimeAwarded"/> shows it.</summary>
        private static void FirstTime(string bonusId)
        {
            if (_state == null || ChaosFirstTimes.IsAwarded(bonusId)) return;
            ChaosFirstTimes.TryAward(bonusId);
        }

        /// <summary>WPF OnFirstTimeAwarded (:209): a feed line and a float where the hand was. Inline on
        /// the UI thread (every award site is there), posted otherwise; the run is captured, so a queued
        /// award never lands in a later descent after panic.</summary>
        private static void OnFirstTimeAwarded(string bonusId, int amount)
        {
            var run = _state;
            if (run == null) return;
            void Show()
            {
                try
                {
                    if (!ReferenceEquals(_state, run)) return;
                    string label = ChaosFirstTimes.Labels.ContainsKey(bonusId) ? Loc.Get("chaos_bonus_" + bonusId) : bonusId;
                    string text = $"+{amount} {ChaosGlyphs.Drops} {label}";
                    run.PushEvent(text);
                    PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY - 30, text, Color.FromRgb(0xC8, 0xA8, 0xFF));
                }
                catch (Exception ex) { Log.Debug(ex, "Chaos first-time feedback failed"); }
            }
            if (Dispatcher.UIThread.CheckAccess()) Show();
            else Dispatcher.UIThread.Post(Show);
        }

        /// <summary>WPF ShowPopScore (:1685), Blank Eyes.</summary>
        private static void ShowPopScore(double pts)
        {
            if (!ChaosFieldMath.ShowsPopScore(_state?.ShowPopScores == true, pts)) return;
            PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                "+" + ((long)pts).ToString("N0"), Color.FromRgb(0xFF, 0xE9, 0xA0));
        }

        private static void OnTunnelStateChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            var state = _state;
            var tunnel = ChaosTunnelWindow.Active;
            if (state == null || tunnel == null) return;
            if (e.PropertyName == nameof(ChaosRunState.Combo))
                tunnel.Post(new { type = "streak", combo = state.Combo, mult = state.ComboMult });
            else if (e.PropertyName == nameof(ChaosRunState.ActIndex))
                tunnel.Post(new { type = "zone-hint", depth = state.ActIndex, intensity = Math.Min(1.0, (state.ActIndex - 1) / 5.0) });
        }

        private static DateTime _heavyUntilUtc = DateTime.MinValue;
        private static DateTime _videoQuarantineUntilUtc = DateTime.MinValue;
        private static MandatoryVideoScheduler? _runVideo;
        internal static MandatoryVideoScheduler? RunVideo => _runVideo;
        private static ChaosVideoGuard? _videoGuard;

        // WPF ChaosModeService.cs:503-504,2164-2174,2260: a mandatory video that ends mid-run keeps
        // the heavy gate shut 3 s more (never shortening an existing floor).
        private static void HookRunVideo()
        {
            UnhookRunVideo();
            _runVideo = CoreEngine.Video;
            if (_runVideo != null)
            {
                _videoGuard = new ChaosVideoGuard(_runVideo, () => BubbleOverlay.Field.Clock(), () =>
                {
                    try { ChaosLessons.Tick("porn_dvd"); }
                    catch (Exception ex) { Log.Debug(ex, "Chaos video lesson failed"); }
                });
                _runVideo.VideoStarted += OnVideoStartedDuringRun;
                _runVideo.VideoEnded += OnVideoEndedDuringRun;
            }
        }

        private static void OnVideoStartedDuringRun()
        {
            _videoGuard?.Started();
            TunnelVideo(true);
        }

        internal static void TunnelVideo(bool on)
        {
            var tunnel = ChaosTunnelWindow.Active;
            if (Dispatcher.UIThread.CheckAccess()) tunnel?.SetVideoPlaying(on);
            else Dispatcher.UIThread.Post(() => { if (ChaosTunnelWindow.Active == tunnel) tunnel?.SetVideoPlaying(on); });
        }

        internal static void OnVideoEndedDuringRun()
        {
            _videoGuard?.Ended();
            TunnelVideo(false);
            var until = BubbleOverlay.Field.Clock().AddSeconds(3);
            if (until > _heavyUntilUtc) _heavyUntilUtc = until;
            if (until > _videoQuarantineUntilUtc) _videoQuarantineUntilUtc = until;
        }

        private static void UnhookRunVideo()
        {
            // Unsubscribe the captured publisher even if CoreEngine.Video has since changed.
            if (_videoGuard?.Stop() == true)
            {
                var until = BubbleOverlay.Field.Clock().AddSeconds(3);
                if (until > _videoQuarantineUntilUtc) _videoQuarantineUntilUtc = until;
            }
            _videoGuard = null;
            if (_runVideo != null)
            {
                _runVideo.VideoStarted -= OnVideoStartedDuringRun;
                _runVideo.VideoEnded -= OnVideoEndedDuringRun;
            }
            _runVideo = null;
        }

        internal static Random AmbientRng = new();
        private static bool HeavyEffectActive => CoreEngine.Video?.IsPlaying == true
            || _videoGuard?.Armed == true || ChaosGifCascadeOverlay.IsRaining
            || BubbleOverlay.Field.Clock() < _heavyUntilUtc || BubbleOverlay.Field.Clock() < _videoQuarantineUntilUtc;

        // WPF ChaosModeService.cs:2155-2156,2295-2322: drop overlapping heavy
        // detonations, including during the off-thread pool pick, without queuing.
        internal static void FirePayloadForDetonation(EffectBubbleSpec spec)
        {
            var payload = spec.Payload;
            // WPF ChaosModeService.cs:2279-2291,2340-2349: only HT links soften;
            // videos retain their opt-out in the variant pool. Busy rain always selects text.
            if (_state?.Config.AmbientMode == true && payload.Kind == EffectBubblePayloadKind.HtLink)
            {
                bool rain = !HeavyEffectActive && AmbientRng.NextDouble() < 0.5;
                EffectPayload soft = rain ? new GifCascadePayload() : new BouncingTextPayload();
                soft.Strength = payload.Strength;
                if (rain) _heavyUntilUtc = BubbleOverlay.Field.Clock().AddSeconds(GifCascadePayload.DURATION_SEC + 5);
                ChaosOverlayWindow.ChaosSfx.Play(rain ? "fx_rain_start" : "fx_text", 0.45f);
                FireScaledPayload(soft);
                return;
            }
            bool heavy = payload.Kind is EffectBubblePayloadKind.GifCascade or EffectBubblePayloadKind.Video;
            var now = BubbleOverlay.Field.Clock();
            if (heavy && HeavyEffectActive)
            {
                // WPF ChaosModeService.cs:2308-2309: drop only the effect, with a feed line.
                Log.Information("Chaos: dropped {Kind} detonation: a heavy effect is already running", payload.Kind);
                _state?.PushEvent(Loc.GetF("chaos_heavy_fizzle", payload.DisplayName));
                return;
            }
            if (payload is VideoPayload video && _spawning)
            {
                // A missing run-owned service must not fall back to an unowned dashboard tape.
                video.ChaosToken = _videoGuard?.Arm() ?? -1;
                if (_videoGuard != null) _heavyUntilUtc = now.AddSeconds(VideoPayload.SEGMENT_SEC + 3);
            }
            if (payload.Kind == EffectBubblePayloadKind.GifCascade)
                _heavyUntilUtc = now.AddSeconds(GifCascadePayload.DURATION_SEC + 5);
            // WPF ChaosModeService.cs:2321-2337: the variant's stinger follows gate acceptance.
            if (spec.VariantId == "htlink") ChaosOverlayWindow.ChaosSfx.Play("fx_rain_start", 0.45f);
            FireScaledPayload(payload);
        }

        /// <summary>WPF FireScaledPayload (:2267) minus the lesson hook.</summary>
        private static void FireScaledPayload(EffectPayload payload)
        {
            double m = _state?.DetonationDurationMult ?? 1.0;
            if (m == 1.0) { payload.Fire(); return; }
            EffectPayload.GlobalDurationMult *= m;
            try { payload.Fire(); }
            finally { EffectPayload.GlobalDurationMult /= m; }
        }

        private static void EndField()
        {
            _spawnTimer?.Stop();
            _spawnTimer = null;
            EndSlowMo();
            EndFreeze();
            Volatile.Write(ref _rippleTarget, null);
            _ripplePointer?.Dispose();
            _ripplePointer = null;
            BubbleOverlay.EndChaos();
            _hud?.SetCursorOnLive(false);
        }

        // WPF ChaosModeService.KeepChromeTopmost: chrome first, then bubbles.
        internal static void KeepChromeTopmost(Action? raiseWave = null)
        {
            if (!_spawning || !CoreSettings.Current.ChaosPinOnTop) return;
            void Raise(Window? w) { if (w is { IsVisible: true }) RaiseChromeWindow(w); }
            Raise(_hud);
            Raise(_fx);
            if (raiseWave != null) raiseWave();
            else ChaosWaveTimerWindow.RaiseActive();
            Raise(_boonBar);
            foreach (var toy in _toyButtons) Raise(toy);
            RaiseBubbles();
            ChaosGifCascadeOverlay.RaiseActive();
            ChaosFlashOverlay.RaiseActive(RaiseOverlayWindow);
            FlashOverlay.RaiseAll(RaiseOverlayWindow);
        }

        // WPF ChaosModeService.cs:745-757, 937-951: lift every visible DVD surface.
        internal static void RaiseDvdAboveVideo()
        {
            if (!IsDescending || !CoreSettings.Current.ChaosPinOnTop) return;
            foreach (var window in ChaosDvdOverlay.Windows)
                if (window.IsVisible) RaiseOverlayWindow(window);
        }

        /// <summary>WPF ChaosModeService.cs:745-774 (hand-ported from #4245 and #4525): over a playing
        /// mandatory video, lift the ambient band first (field and Skia FX, pop text, DVD, banner,
        /// announcer, cursor glow, e-stim, vibe trail), then the chrome, bubbles and attention assets.
        /// Existing visible windows only; nothing is activated.</summary>
        internal static void RaiseGameLayerAboveVideo(Action? raiseWave = null)
        {
            if (!_spawning || !CoreSettings.Current.ChaosPinOnTop) return;
            void Raise(Window? w)
            {
                if (w is not { IsVisible: true }) return;
                try { RaiseOverlayWindow(w); } catch (Exception ex) { Log.Debug("Chaos video restack: {E}", ex.Message); }
            }
            ChaosFieldFxOverlay.RaiseActive();
            if (ChaosSkiaFxOverlay.Active is { } skiaFx) foreach (var host in skiaFx.Hosts) Raise(host);   // every per-monitor host (restack b123 #4247)
            // WPF ChaosModeService.cs:755 raises the legacy bubble host before the chrome and bubbles.
            if (!CoreSettings.Current.UnifiedOverlayHost) SharedOverlayHost.RaiseLive();
            foreach (var w in ChaosPopText.Pool.All) Raise(w);
            RaiseDvdAboveVideo();
            ChaosEffectBannerOverlay.RaiseActive();
            Raise(ChaosAnnouncerOverlay.Active);
            Raise(ChaosCursorGlowOverlay.Active);
            ChaosEStimOverlay.RaiseActive();
            ChaosVibeTrailOverlay.RaiseActive();
            KeepChromeTopmost(raiseWave);
        }

        /// <summary>One RunTick, the clock half of WPF ChaosModeService.cs:929-1099.</summary>
        internal static void Tick()
        {
            if (!_spawning || _state == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            if (_overlay is not { IsVisible: true }) return;   // P01: nothing ticks while hidden
            if (++_chromeTicks >= 4)
            {
                _chromeTicks = 0;
                // WPF :943-951: the whole game layer rides over a playing video, else just the chrome.
                if (CoreEngine.Video?.IsPlaying == true) RaiseGameLayerAboveVideo();
                else KeepChromeTopmost();
            }
            double elapsed = ChaosRunEngine.Advance(_state);
            if (_videoGuard?.Tick(_state.RunDurationSec - elapsed <= 3) == true)
                _state.PushEvent(Loc.Get("chaos_tape_snaps"));
            if (_state.Config.ScriptedFirstRun && TickScriptedDraft()) return;
            UpdateHeatTint();
            if (_freezeRemainingSec > 0)
            {
                _freezeRemainingSec -= ChaosRunEngine.TICK_SEC;
                if (_freezeRemainingSec <= 0) EndFreeze();
            }
            if (_slowMoRemainingSec > 0)
            {
                _slowMoRemainingSec -= ChaosRunEngine.TICK_SEC;
                if (_slowMoRemainingSec <= 0) EndSlowMo();
            }
            TickPendulum();
            _hud?.SetCursorOnLive(BubbleOverlay.CursorOnLive);
            // WPF ChaosModeService.cs:984-989: gather only while the run clock advances.
            if (_state.RippleCooldown > 0)
            {
                _state.RippleCooldown -= ChaosRunEngine.TICK_SEC;
                if (_state.RippleReady) RippleCue("toy_ready", 0.3f);
            }
            TickToys();
            // WPF :1029-1040: focus below a defuse's price for 8 s while a live hangs on screen, once per run; checked before the
            // T-10s warning as WPF does, so it can take the shared bark gap.
            if (!_focusLowBarkFired)
            {
                if (_state.FocusLow && BubbleOverlay.Field.Bubbles.Any(x => x.IsLiveThreat && !x.Popping))
                {
                    _focusLowAccumSec += ChaosRunEngine.TICK_SEC;
                    if (_focusLowAccumSec >= ChaosTuning.FOCUS_LOW_BARK_SEC) { _focusLowBarkFired = true; RunBark("ChaosFocusLow", null); }
                }
                else _focusLowAccumSec = 0;
            }
            // WPF :1068 T-10s, once per run (a Relapse extension does not re-arm it).
            if (!_endingSoonFired && ChaosRunEngine.IsEndingSoon(_state, elapsed))
            {
                _endingSoonFired = true;
                ChaosAnnouncerOverlay.Announce("the hole is closing…", ChaosAnnounceKind.Depth,
                    artKey: "ending_soon", subText: "ten seconds");
                _state.PushEvent("⏳ ten seconds. make them count.");
                RunBark("ChaosEndingSoon", null);   // WPF :1074
            }
            var end = ChaosRunEngine.CheckEnd(_state, elapsed);
            if (end == ChaosRunEngine.EndCheck.RunOver) { EndRun(); return; }
            if (end == ChaosRunEngine.EndCheck.Relapse)
            {
                ChaosAnnouncerOverlay.Announce("☠ RELAPSE — one more loop", ChaosAnnounceKind.Temptation,
                    artKey: "relapse", subText: "one more loop");   // WPF :1083
                _state.PushEvent("☠ relapse. one more loop — everything drips double");
                BarkHead.Engine?.Raise("ChaosWaveEscalated", c => c.Set("wave", (double)(_state.WaveIndex + 1)));   // WPF :1087
            }
            var (newWave, waveLen) = ChaosRunEngine.WaveAt(_state, elapsed);
            // WPF :1094 Pocket Watch: the wave countdown (+ a live score line).
            if (_state.ShowWaveTimer)
                ChaosWaveTimerWindow.Update(newWave, _state.WaveCount, waveLen - (elapsed % waveLen), _state.Score);
            if (newWave > _state.WaveIndex)
            {
                // WPF BeginWaveTransition with drafts off (:1442-1449): lessons, tip, then roll straight on.
                // Claim the draft pause before loop lessons fire, so their cards are deferred.
                if (_state.Config.BoonDraftEnabled) { _draftPaused = true; RefreshRippleInput(); }
                ChaosRunLessons.OnLoopCompleted();
                AwardLoopTip();
                _state.AllLiveNextWave = false;
                if (_state.Config.BoonDraftEnabled)
                {
                    _pendingWave = newWave;
                    _spawnTimer?.Stop();
                    ChaosWaveTimerWindow.Clear();
                    BubbleOverlay.ClearChaos();
                    // WPF ChaosModeService.cs:1464-1467: one burst cue and a soft pulse as the field clears.
                    ChaosOverlayWindow.ChaosSfx.PlayWaveClear();
                    Pulse(WaveClearColor, WaveClearPulse);
                    RunBark("ChaosWaveCleared", c => c.Set("wave", (double)_state.WaveIndex));   // WPF :1468
                    RunBark("ChaosWaveEscalated", c => c.Set("wave", (double)newWave));   // WPF :1474, after cleared (60 s bark gap)
                    var options = DealDraft(rig: true);
                    _overlay!.ShowBoonDraft(_state.WaveIndex, options, ChooseBoon,
                        _state.Config.DraftAutoResumeSec, _state.RerollsLeft, RerollDraft);
                }
                else
                {
                    _state.WaveIndex = newWave;
                    int previousAct = _state.ActIndex;
                    _state.ActIndex = ChaosRunEngine.ActFor(newWave);
                    ChaosBackdropWindow.Active?.SetDepth(_state.ActIndex);
                    RunBark("ChaosWaveEscalated", c => c.Set("wave", (double)newWave));   // WPF :1453
                    AnnounceDepthIfCrossed(previousAct);
                    AnnounceFinalLoopIfEntering();   // WPF ChaosModeService.cs:1460
                }
            }
        }

        // WPF ChaosHappyPath.cs:116-145, 207-218: the first run teaches one long-fuse threat,
        // then interrupts the same wave with three starter cards after a snap or at 70%.
        private static bool TickScriptedDraft()
        {
            var s = _state!;
            if (!_scriptThreat && s.RunProgress >= 0.30)
            {
                _scriptThreat = true;
                var pink = ChaosBubbleVariants.All.First(v => v.Id == "pink");
                var spec = ChaosBubbleVariants.Build(pink, s.RunIntensity, s.FuseTimeMult * 3, null, s.Config.EffectIntensity, s.BubbleScale);
                ChaosMeta.MarkDiscovered("bubble:" + spec.VariantId); BubbleOverlay.SpawnChaos(spec);
            }
            if (_scriptDraft || s.RunProgress < 0.55 || (!_scriptSnapped && s.RunProgress < 0.70)) return false;
            var ids = new[] { "extra_shield", "defuse_chain", "golden_touch", "welcome_shower", "heavy_drop" };
            var cards = ChaosBoonPool.All.Where(b => !b.IsCurse && ids.Contains(b.Id)).OrderBy(_ => Rng.Next()).Take(3).ToList();
            _scriptDraft = true;
            if (cards.Count == 0) return false;
            _draftPaused = true; _pendingWave = s.WaveIndex; _spawnTimer?.Stop();
            RefreshRippleInput();   // the right-click hook stops consuming while the draft card is up
            ChaosWaveTimerWindow.Clear(); BubbleOverlay.ClearChaos();
            ChaosOverlayWindow.ChaosSfx.PlayWaveClear();   // WPF ChaosModeService.cs:1499
            foreach (var card in cards) ChaosMeta.MarkDiscovered("boon:" + card.Id);
            _overlay!.ShowBoonDraft(s.WaveIndex, cards, ChooseBoon, s.Config.DraftAutoResumeSec);
            return true;
        }

        private static List<ChaosBoon> DealDraft(bool rig = false)
        {
            var s = _state!;
            var taken = s.ActiveBoons.Concat(s.ActiveCurses).Select(b => b.Id).ToHashSet();
            var options = ChaosBoonPool.Draft(s.Config.AllowCurses, s.Config.DraftChoices,
                s.MaxedBoons.Contains("surrender"), taken, s.Config.SinChance);
            if (rig && ++_draftsThisRun == 1 && options.Count > 0 && _runsAtStart >= 3 && !ChaosMeta.State.SeenFirstSin && s.Config.AllowCurses)
            {
                var sin = ChaosBoonPool.All.FirstOrDefault(b => b.Id == "double_or_nothing");
                if (sin != null)
                {
                    int index = options.FindIndex(b => b.IsCurse);
                    if (!options.Any(b => b.Id == sin.Id)) options[index < 0 ? options.Count - 1 : index] = sin;
                    _sinRigged = _sinPending = true;
                    ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_first_sin_demo"), ChaosAnnounceKind.Temptation, holdMs: ChaosAnnounceTiming.TeachHoldMs);
                }
            }
            else if (rig && _draftsThisRun == 1 && options.Count > 0 && !ChaosMeta.State.SeenDuoDemo
                && ChaosMeta.IsBoonActive("the_spanker") && ChaosMeta.IsBoonActive("e_stim"))
            {
                var duo = ChaosBoonPool.All.FirstOrDefault(b => b.Id == "electrified_rabbits");
                if (duo != null)
                {
                    if (!options.Any(b => b.Id == duo.Id)) options[options.Count - 1] = duo;
                    ChaosMeta.State.SeenDuoDemo = true; ChaosMeta.Save();
                    ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_duo_demo"), ChaosAnnounceKind.Item, holdMs: ChaosAnnounceTiming.TeachHoldMs);
                    BarkHead.Engine?.Raise("ChaosDuoDemo");
                }
            }
            foreach (var option in options) ChaosMeta.MarkDiscovered("boon:" + option.Id);
            return options;
        }

        internal static (List<ChaosBoon> options, int rerollsLeft)? RerollDraft()
        {
            if (!_draftPaused || _pendingWave == 0 || _state == null || _state.RerollsLeft <= 0) return null;
            _state.RerollsLeft--;
            _state.PushEvent(Loc.Get("chaos_draft_reroll"));
            return (DealDraft(), _state.RerollsLeft);
        }

        internal static void ChooseBoon(ChaosBoon? boon)
        {
            if (!_draftPaused || _pendingWave == 0 || _state == null) return;
            if (_sinPending) { _sinPending = false; ChaosMeta.State.SeenFirstSin = true; ChaosMeta.Save(); }
            if (boon == null)
            {
                // WPF ChaosModeService.cs:1578-1584: resisting the table pays +1 resistance, seen and heard.
                _state.Shields++;
                _state.PushEvent(Loc.Get("chaos_event_skip"));
                Pulse(ShieldGainColor, ShieldGainPulse);
                ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_announce_resist"), ChaosAnnounceKind.Willpower, artKey: "resistance");
                RunBark("ChaosBoonSkipped", c => c.Set("shields_now", (double)_state.Shields));   // WPF :1582
            }
            else
            {
                bool shield = boon.IsCurse && _state.MaxedBoons.Contains("surrender") && !_state.SurrenderShieldUsed;
                if (shield) _state.SurrenderShieldUsed = true;
                if (!shield && _sinRigged && boon.Id == "double_or_nothing") { shield = true; _sinRigged = false; }
                if (!ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Whisper)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Whisper);
                if (boon.IsCurse && !ChaosFirstTimes.IsAwarded(ChaosFirstTimes.Yes)) ChaosFirstTimes.TryAward(ChaosFirstTimes.Yes);
                try
                {
                    ChaosLessons.Tick("draft4");
                    if (boon.IsCurse) ChaosLessons.Tick("surrender");
                }
                catch (Exception ex) { Log.Debug("Chaos draft lessons: {E}", ex.Message); }
                _state.ApplyBoon(boon, shield);
                if (boon.Id == "extra_shield") Pulse(ShieldGainColor, ShieldGainPulse);   // WPF :1548
                if (boon.IsCurse)
                {
                    // WPF ChaosModeService.cs:1549-1567: the sin's event lines, cue and announcement.
                    _state.BoonMult += _state.SinExtraMult;
                    if (_state.SinExtraMult > 0) _state.PushEvent(Loc.GetF("chaos_event_sin_bonus", _state.SinExtraMult));
                    if (shield) _state.PushEvent(Loc.Get("chaos_event_sin_shielded"));
                    if (_state.MaxedBoons.Contains("surrender"))
                    {
                        _state.Shields++;
                        _state.PushEvent(Loc.Get("chaos_event_sin_resistance"));
                        Pulse(ShieldGainColor, ShieldGainPulse);
                    }
                    ChaosOverlayWindow.ChaosSfx.Play("sin_accept", 0.6f);
                    RunBark("ChaosCursePicked", c => c.Set("boon", boon.Name).Set("rarity", boon.Rarity.ToString()).Set("run_mult_bonus", boon.RunMultBonus));   // WPF :1564
                    ChaosAnnouncerOverlay.Announce(boon.Name, ChaosAnnounceKind.Temptation, artKey: boon.Id);   // WPF prefixes a skull; IconGuard keeps emoji out, the Temptation colour carries it
                }
                else
                {
                    ChaosAnnouncerOverlay.Announce(boon.Name, ChaosAnnounceKind.Mantra, artKey: boon.Id);
                    RunBark("ChaosBoonPicked", c => c.Set("boon", boon.Name));   // WPF :1573
                }
            }
            _state.WaveIndex = _pendingWave;
            int previousAct = _state.ActIndex;
            _state.ActIndex = ChaosRunEngine.ActFor(_pendingWave);
            ChaosBackdropWindow.Active?.SetDepth(_state.ActIndex);
            AnnounceDepthIfCrossed(previousAct);
            _pendingWave = 0;
            _overlay?.ShowReadyGo(ResumeAfterDraft);
        }

        // WPF ChaosModeService.cs:3104-3113: one depth cue when a wave commit crosses an act.
        private static void AnnounceDepthIfCrossed(int previousAct)
        {
            if (_state == null || _state.ActIndex <= previousAct) return;
            RunBark("ChaosActChanged", c => c.Set("act", (double)_state.ActIndex).Set("wave", (double)_state.WaveIndex));   // WPF :3110
            ChaosOverlayWindow.ChaosSfx.Play("depth_change", 0.55f);
            ChaosAnnouncerOverlay.Announce(Loc.GetF("chaos_announce_depth", _state.ActIndex), ChaosAnnounceKind.Depth,
                artKey: "depth", subText: $"{_state.ActIndex}");
        }

        private static void ResumeAfterDraft()
        {
            if (!_draftPaused || !_spawning) return;
            if (_pendingLessonCards.Count > 0)
            {
                _draftPaused = false;
                _lessonCardPaused = true;
                _lessonCardsAfterDraft = true;
                BubbleOverlay.HoldChaos(true);
                ChaosOverlayWindow.ChaosSfx.Play("ui_unlock", 0.55f);   // WPF ChaosModeService.cs:1614, once for the deferred cards
                var cards = _pendingLessonCards.ToArray();
                _pendingLessonCards.Clear();
                foreach (var card in cards) ChaosUnlockCardOverlay.Show(card, ResumeAfterLessonCard, autoDismiss: false);
                return;
            }
            _draftPaused = false;
            if (!_manualPaused && !_lessonCardPaused) _spawnTimer?.Start();
            if (_state?.WelcomeShowerEnabled == true) SpawnWelcomeShower();
            AnnounceFinalLoopIfEntering();   // WPF ChaosModeService.cs:1625
        }

        /// <summary>WPF AnnounceFinalLoopIfEntering (ChaosModeService.cs:1639-1647): the last loop's banner, once a run,
        /// after the post-pick GO (or the last deferred card) on the draft path, at the wave commit without drafts.
        /// The Relapse bonus loop keeps its own announce.</summary>
        private static void AnnounceFinalLoopIfEntering()
        {
            if (_state == null || _finalLoopAnnounced) return;
            if (_state.WaveCount <= 1 || _state.WaveIndex < _state.WaveCount) return;
            _finalLoopAnnounced = true;
            ChaosAnnouncerOverlay.Announce(Loc.Get("chaos_last_loop"), ChaosAnnounceKind.Depth,
                artKey: "final_loop", subText: Loc.Get("chaos_last_loop_sub"));
            _state.PushEvent(Loc.Get("chaos_event_last_loop"));
        }

        /// <summary>WPF SpawnWelcomeShower (:1645): six flash or subliminal treats from above.</summary>
        private static void SpawnWelcomeShower()
        {
            if (_state == null) return;
            for (int i = 0; i < 6; i++)
            {
                var variant = ChaosBubbleVariants.All[Rng.Next(2)];
                BubbleOverlay.SpawnChaos(ChaosBubbleVariants.Build(variant, _state.RunIntensity, _state.FuseTimeMult,
                    ChaosMotion.RainDown, _state.Config.EffectIntensity, _state.BubbleScale), motion: ChaosMotion.RainDown);
            }
            PlayChime(0.25f);
            _state.PushEvent(Loc.Get("chaos_welcome_shower"));
        }

        /// <summary>WPF BubbleService.PlayChime (:2065): a random chime at (master x bubbles)^1.5 x scale.</summary>
        private static void PlayChime(float scale)
        {
            var chime = $"chime{Rng.Next(1, 4)}.mp3";
            var sound = CoreModArt.AudioOverridePath(chime) ?? ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", chime));
            if (System.IO.File.Exists(sound))
            {
                var settings = CoreSettings.Current;
                float volume = (float)Math.Pow(settings.MasterVolume / 100.0 * settings.BubblesVolume / 100.0, 1.5) * scale;
                CoreAudio.PlayOneShot(sound, volume, "bubble-chime");
            }
        }

        /// <summary>WPF UpdateHeatTint (:3131): a warm edge tint that rises with run Heat,
        /// re-issued only when the quantized level moves. Honours the colour-flashes toggle.</summary>
        private static void UpdateHeatTint()
        {
            if (_state?.Config?.ColorFlashesEnabled != true || _fx == null) return;
            if (ChaosFxEnvelope.HeatTintLevel(_state.Heat, _lastHeatTint) is not { } q) return;
            _lastHeatTint = q;
            var (r, g, b) = ChaosFxEnvelope.HeatTintColor;
            if (q <= 0) _fx.EndHeatTint();
            else _fx.SetHeatTint(global::Avalonia.Media.Color.FromRgb(r, g, b), q * ChaosFxEnvelope.HEAT_TINT_MAX_OPACITY);
        }

        private static void CloseFx()
        {
            try { _fx?.Close(); } catch (Exception ex) { Log.Debug("Chaos FX close: {E}", ex.Message); }
            _fx = null;
        }

        /// <summary>WPF ToggleManualPause (:257), the clock half: paused holds the clock.</summary>
        public static void ToggleManualPause()
        {
            if (!_spawning || _lessonCardPaused || _draftPaused) return;
            _manualPaused = !_manualPaused;
            BubbleOverlay.HoldChaos(_manualPaused);   // WPF :267-268 freeze + input lock
            _state?.PushEvent(_manualPaused ? "⏸ held. the hole waits." : "▶ sinking again");
            _hud?.SetPausedUi(_manualPaused);
        }

        /// <summary>WPF OnLessonCompleted: completion may come from any thread; present only
        /// in the run that earned it. A manual pause or recap keeps its own pause ownership.</summary>
        private static void OnLessonCompleted(string id)
        {
            var run = _state;
            void Present()
            {
                if (!_active || run == null || !ReferenceEquals(run, _state)) return;
                var card = ChaosUnlockCardRules.ForLesson(id);
                if (card == null) return;
                _state.PushEvent($"lesson learned — {card.Title}");
                if (_spawning && _draftPaused && !_manualPaused && !_lessonCardPaused)
                {
                    _pendingLessonCards.Add(card);
                    return;
                }
                bool claimPause = _spawning && !_manualPaused && !_lessonCardPaused && !_draftPaused;
                if (claimPause)
                {
                    _lessonCardPaused = true;
                    _spawnTimer?.Stop();
                    BubbleOverlay.HoldChaos(true);
                }
                ChaosUnlockCardOverlay.Show(card, ResumeAfterLessonCard, autoDismiss: !_lessonCardPaused);
            }
            // Draft completions must be queued before a skippable GO can finish.
            if (Dispatcher.UIThread.CheckAccess() && _draftPaused) Present();
            else Dispatcher.UIThread.Post(Present);
        }

        private static void ResumeAfterLessonCard()
        {
            if (!_lessonCardPaused || ChaosUnlockCardOverlay.IsShowing) return;
            _lessonCardPaused = false;
            BubbleOverlay.HoldChaos(_manualPaused);
            if (_spawning && !_manualPaused && !_draftPaused) _spawnTimer?.Start();
            if (_lessonCardsAfterDraft)
            {
                _lessonCardsAfterDraft = false;
                if (_spawning && _state?.WelcomeShowerEnabled == true) SpawnWelcomeShower();
                if (_spawning) AnnounceFinalLoopIfEntering();   // WPF ChaosModeService.cs:205
            }
        }

        public static void RequestStop()
        {
            if (_manualPaused) { _manualPaused = false; _hud?.SetPausedUi(false); }
            if (_spawning) EndRun();
        }

        /// <summary>Panic / app exit: close everything, no recap, no payout. Safe when idle.</summary>
        public static void ForceShutdown()
        {
            ChaosBackdropWindow.CloseActive();
            CloseLoadoutSidebar();
            ChaosTunnelWindow.CloseActive();
            ChaosUnlockCardOverlay.CloseActive();
            ChaosAnnouncerOverlay.CloseActive();   // WPF :3177; also clears a queue left by an idle caller
            ChaosCursorGlowOverlay.CloseActive();   // WPF ForceShutdown :3183
            ChaosSkiaFxOverlay.CloseActive();   // WPF ChaosModeService.ForceShutdown; before the idle return
            UnhookRunVideo();   // WPF ChaosModeService.cs:3220
            _heavyUntilUtc = DateTime.MinValue;
            ChaosGifCascadeOverlay.CloseActive(); // also cancel a pending pick before the idle return
            if (!_active && _hud == null && _overlay == null && _fx == null) return;
            _spawning = false;
            _runTimer?.Stop();
            EndField();
            if (_overlay != null)
            {
                _overlay.OnDismissed = null;   // avoid re-entrant cleanup
                _overlay.OnRunAgain = null;
                try { _overlay.Close(); } catch (Exception ex) { Log.Debug("Chaos overlay close: {E}", ex.Message); }
            }
            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            ChaosWaveTimerWindow.CloseActive();   // WPF ForceShutdown :3181
            CloseBoonBar();   // WPF ForceShutdown :3181
            CloseFx();   // WPF ForceShutdown :3189
            CleanupAfterRun();
        }

        /// <summary>WPF AwardLoopTip (:1743) + BankGold (:1709): gold for the loop just finished,
        /// doubled when it had no detonation.</summary>
        private static void AwardLoopTip()
        {
            if (_state == null) return;
            bool clean = _state.Detonated == _waveDetonatedAt;
            _waveDetonatedAt = _state.Detonated;
            int tip = ChaosPayout.LoopTip(Rng.Next(3, 7), _state.Config.DifficultyMult, clean, _state);
            BankGold(tip);
            _state.PushEvent(clean
                ? $"{ChaosGlyphs.Gold} clean loop — she tips +{tip} gold"
                : $"{ChaosGlyphs.Gold} loop done — she tips +{tip} gold");
        }

        /// <summary>WPF RollCamGirlTip (:1756): chance per treat, prism or snap.</summary>
        private static void RollCamGirlTip()
        {
            if (_state == null || _state.CamGirlTipChance <= 0 || Rng.NextDouble() >= _state.CamGirlTipChance) return;
            int tip = ChaosRunEngine.GoldScaled(_state, Rng.Next(2, 5));
            BankGold(tip, floatAtPop: true);
            _state.PushEvent($"{ChaosGlyphs.Gold} {Loc.GetF("chaos_camgirl_tip", tip)}");
        }

        /// <summary>WPF BankGold (:1709): persists at once; the very first gold gets its feed line.</summary>
        private static void BankGold(int gold, bool floatAtPop = false)
        {
            if (gold <= 0 || _state == null) return;
            bool first = !ChaosMeta.State.SeenGoldFirst;
            if (first) ChaosMeta.State.SeenGoldFirst = true;
            ChaosMeta.AddGold(gold);   // persists
            if (floatAtPop)
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                    $"+{gold} {ChaosGlyphs.Gold}", Color.FromRgb(0xFF, 0xD7, 0x00));
            if (first)
            {
                _state.PushEvent($"{ChaosGlyphs.Gold} gold. she takes it at her bench.");
                BarkHead.Engine?.Raise("ChaosGoldFirst");
            }
        }

        /// <summary>WPF EndRun (:3199): the loop tip on a full course, then XP, Sparks, reveals,
        /// the rank card and the recap. Each banking step is isolated like WPF's.</summary>
        private static void EndRun()
        {
            if (!_spawning || _state == null) return;
            bool ranFullCourse = _state.ElapsedSec >= _state.RunDurationSec;
            if (ranFullCourse) AwardLoopTip();   // a quit forfeits it
            // WPF :3211: final-loop + end-of-descent lessons (a quit didn't run the full course).
            ChaosRunLessons.OnRunCompleted(_state.Shields, _state.Config.Difficulty, ranFullCourse);
            _spawning = false;
            UnhookRunVideo();   // WPF EndRun :3220
            _heavyUntilUtc = DateTime.MinValue;
            _draftPaused = false;
            _pendingWave = 0;
            _lessonCardsAfterDraft = false;
            _pendingLessonCards.Clear();
            _overlay?.CancelDraftAndCountdown();
            _runTimer?.Stop();
            EndField();
            StopToys();
            try { ChaosPopText.ShutdownPool(); } catch (Exception ex) { Log.Debug("Chaos pop text: {E}", ex.Message); }   // WPF EndRun :3241

            var (baseXp, skillMult, finalXp) = ChaosPayout.Xp(_state.RunDurationSec,
                _state.Config.DifficultyMult, _state.Score, _state.SkillMult);
            if (!CoreProgression.AddXP(baseXp, "Chaos")) Log.Debug("Chaos payout AddXP failed");
            long previousBest = ChaosMeta.State.BestScore;   // before the award moves it
            int sparksEarned = 0;
            try { sparksEarned = ChaosRunEffects.AwardRunRewards(_state); }
            catch (Exception ex) { Log.Debug("Chaos meta award: {E}", ex.Message); }
            try { RevealService.Sync("run_end"); } catch (Exception ex) { Log.Debug("Chaos reveal sync: {E}", ex.Message); }
            ChaosRank? rankUp = null;
            try { rankUp = ChaosPayout.RankUp(ChaosMeta.State.RunsCompleted, ChaosMeta.State.LastRankSeen); }
            catch (Exception ex) { Log.Debug("Chaos rank-up: {E}", ex.Message); }
            BarkHead.Engine?.Raise("ChaosRunCompleted", c => c.Set("xp", (double)(int)finalXp)
                .Set("difficulty", _state.Config.Difficulty.ToString())
                .Set("runs_completed", (double)ChaosMeta.State.RunsCompleted)
                .Set("rank", ChaosRanks.NameLower(ChaosMeta.RankIndex)));

            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            _hud = null;
            Overlays.ChaosFlashOverlay.CloseActive();   // WPF EndRun :3228: the wash never sits over the results card
            ChaosGifCascadeOverlay.CloseActive();   // WPF EndRun :3229
            ChaosUnlockCardOverlay.CloseActive();
            _lessonCardPaused = false;
            ChaosAnnouncerOverlay.CloseActive();   // WPF :3230
            _dvdBannerOn = false;
            ChaosDvdOverlay.CloseActive();   // WPF EndRun (:3232)
            ChaosEffectBannerOverlay.CloseActive();   // WPF EndRun :3233
            ChaosWaveTimerWindow.CloseActive();   // WPF EndRun :3234
            CloseBoonBar();   // WPF EndRun :3235
            ChaosCursorGlowOverlay.CloseActive();   // WPF EndRun :3236
            ChaosVibeTrailOverlay.CloseActive();   // WPF EndRun :3237
            ChaosEStimOverlay.CloseActive();   // WPF EndRun :3238
            Overlays.ChaosFieldFxOverlay.CloseActive();   // WPF ChaosModeService.cs:3236: before results
            ChaosSkiaFxOverlay.CloseActive();   // WPF EndRun
            CloseFx();   // WPF EndRun :3242
            _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned, rankUp);
            Log.Information("Chaos run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP, {Sparks} sparks",
                baseXp, skillMult, finalXp, sparksEarned);
        }

        /// <summary>WPF BeginRun :483-496: build the toys, listen for their keys, park one hero button
        /// per toy. The keys ride the panic key's own listener (X11PanicKey / Win32PanicKey), which
        /// hands the press to panic first.</summary>
        private static void StartToys()
        {
            if (_state == null) return;
            var s = CoreSettings.HasProvider ? CoreSettings.Current : null;
            ChaosToyRules.Build(_state, ChaosMeta.SlotsFor(ChaosBoonCategory.Skill), ChaosMeta.IsBoonActive,
                s?.ChaosAccessoryKey1, s?.ChaosAccessoryKey2);
            var state = _state;
            X11PanicKey.ToyKeys = () => ToyKeyLabels(state);
            X11PanicKey.ToyKey = key => Dispatcher.UIThread.Post(() => OnToyKey(key));
            for (int i = 0; i < _state.ActiveToys.Count; i++)
            {
                try
                {
                    var btn = new ChaosToyButtonWindow(_state.ActiveToys[i], i);
                    btn.Show();
                    _toyButtons.Add(btn);
                }
                catch (Exception ex) { Log.Debug("Toy button: {E}", ex.Message); }
            }
        }

        private static string[] ToyKeyLabels(ChaosRunState state)
        {
            var keys = new List<string>();
            foreach (var t in state.ActiveToys) if (!string.IsNullOrEmpty(t.KeyLabel)) keys.Add(t.KeyLabel);
            return keys.ToArray();
        }

        /// <summary>WPF StopKeyHook + CloseToyButtons (EndRun / ForceShutdown / overlay closed). Idempotent.</summary>
        private static void StopToys()
        {
            StopToyEffects();
            X11PanicKey.ToyKey = null;
            X11PanicKey.ToyKeys = null;
            foreach (var b in _toyButtons.ToArray())
                try { b.Close(); } catch (Exception ex) { Log.Debug("Toy button close: {E}", ex.Message); }
            _toyButtons.Clear();
        }

        // WPF ChaosModeService.cs:3038-3064: pickup holds the field for 3.5 seconds.
        private static void ActivateFreeze()
        {
            _freezeRemainingSec = 3.5;
            BubbleOverlay.Field.FreezeChaos = true;
            BubbleOverlay.Field.FreezeVibrateRemainingMs = 200;
            EffectPayload.GlobalDurationMult = 2.5;
            var edge = Color.FromRgb(150, 210, 255);
            ChaosEffectBannerOverlay.Show("freeze", Loc.Get("chaos_freeze_banner"), edge);
            if (_state?.Config.ColorFlashesEnabled == true) { _fx?.FreezeBurst(edge); _fx?.BeginEdgeHold(edge, 0.5); }
            Shake(0.3, 200);
        }

        private static void EndFreeze()
        {
            if (!BubbleOverlay.Field.FreezeChaos) return;
            _freezeRemainingSec = 0;
            BubbleOverlay.Field.FreezeChaos = false;
            BubbleOverlay.Field.ClearFreezeVisuals();
            if (_slowMoRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;   // WPF :3070: keep a running slow-mo
            ChaosEffectBannerOverlay.End("freeze");
            _fx?.EndEdgeHold();
            ChaosOverlayWindow.ChaosSfx.Play("freeze_shatter", 0.5f);
        }

        /// <summary>WPF ChaosModeService.cs:2958-2979: at a random beat of every loop the field dips into
        /// a 2.5 s slow-mo, announced on top; with Focus Here it is an x3 scoring window.</summary>
        private static void TickPendulum()
        {
            if (_state == null || !_spawning || !_state.Config.PendulumSwing) return;
            if (_state.WaveIndex != _pendulumWave)
            {
                _pendulumWave = _state.WaveIndex;
                _pendulumFireAt = 0.15 + Rng.NextDouble() * 0.65;
                _pendulumFired = false;
            }
            if (_pendulumFired || _state.WaveProgress < _pendulumFireAt || _freezeRemainingSec > 0 || _slowMoRemainingSec > 0) return;
            _pendulumFired = true;
            ActivateSlowMo(2.5, pendulum: true, Loc.Get("chaos_pendulum_banner"));
            ChaosOverlayWindow.ChaosSfx.Play("ticktock", 0.45f);   // silent until the asset ships, as WPF
            bool scoring = _state.PendulumPayMult > 1;
            ChaosAnnouncerOverlay.Announce(Loc.Get(scoring ? "chaos_pendulum_focus" : "chaos_pendulum_swing"),
                ChaosAnnounceKind.PowerUp, artKey: scoring ? "focus_here" : "pendulum",
                subText: scoring ? Loc.Get("chaos_pendulum_focus_sub") : null);
            _state.PushEvent(Loc.Get("chaos_pendulum_swing"));
        }

        /// <summary>WPF ActivateSlowMo (:2414): the chaos field drifts and burns slower, spawns stretch,
        /// payloads linger. A refresh never replays the warp-in cue.</summary>
        internal static void ActivateSlowMo(double durationSec, bool pendulum, string bannerLabel)
        {
            if (!_spawning) return;
            _slowMoRemainingSec = durationSec;
            _pendulumSlowActive = pendulum;
            BubbleOverlay.Field.ChaosTimeScale = SlowMoFactor;
            ChaosEffectBannerOverlay.Show("slowmo", bannerLabel, Color.FromRgb(0x7A, 0xE0, 0xFF),
                artKey: pendulum ? "pendulum" : "slowmo");
            EffectPayload.GlobalDurationMult = 1.0 / SlowMoFactor;
            if (!_slowMoCueOn) ChaosOverlayWindow.ChaosSfx.Play("time_slow_in", 0.5f);
            _slowMoCueOn = true;
        }

        /// <summary>WPF EndSlowMo (:2465). Idempotent; run end, panic and the overlay closing reach it.</summary>
        private static void EndSlowMo()
        {
            _slowMoRemainingSec = 0;
            _pendulumSlowActive = false;
            BubbleOverlay.Field.ChaosTimeScale = 1.0;
            ChaosEffectBannerOverlay.End("slowmo");
            if (_slowMoCueOn) ChaosOverlayWindow.ChaosSfx.Play("time_slow_out", 0.45f);
            _slowMoCueOn = false;
            if (_freezeRemainingSec <= 0) EffectPayload.GlobalDurationMult = 1.0;
        }

        /// <summary>WPF TickActiveToys: cooldowns and temporary effect windows.</summary>
        private static void TickToys()
        {
            if (_state == null) return;
            foreach (var t in ChaosToyRules.Tick(_state.ActiveToys, ChaosRunEngine.TICK_SEC))
                ChaosOverlayWindow.ChaosSfx.Play("toy_ready", 0.45f);
            TickToyEffects();
        }

        /// <summary>WPF OnToyKey (UI thread): a toy keybind press. The panic key never fires a toy.</summary>
        internal static void OnToyKey(string key)
        {
            if (_state == null) return;
            var s = CoreSettings.HasProvider ? CoreSettings.Current : null;
            var toy = ChaosToyRules.Match(_state.ActiveToys, key, s?.PanicKeyEnabled == true, s?.PanicKey);
            if (toy != null) UseToy(toy);
        }

        /// <summary>WPF UseToyById: the hero button's click.</summary>
        internal static void UseToyById(string id)
        {
            if (_state == null) return;
            foreach (var t in _state.ActiveToys)
                if (t.Id == id) { UseToy(t); return; }
        }

        /// <summary>WPF UseToy (:2681): gate, effect, then the once-per-save award.</summary>
        private static void UseToy(ChaosToyState toy)
        {
            if (_state == null) return;
            // WPF UseToy :2690: a wave draft holds the run (_paused), so toys wait for GO.
            if (!ToyEffectPorted(toy.Id)) return;   // #4225: an inert toy spends no charge or cooldown
            var r = ChaosToyRules.Use(_state, toy, _spawning && !_manualPaused && !_lessonCardPaused && !_draftPaused && _overlay is { IsVisible: true });
            if (r == ChaosToyRules.UseResult.Denied) ChaosOverlayWindow.ChaosSfx.Play("toy_denied", 0.4f);
            if (r == ChaosToyRules.UseResult.Fired && FireToyEffect(toy))
                ChaosFirstTimes.TryAward(ChaosFirstTimes.Play);
        }

        private static void RunAgain()
        {
            _overlay?.Close();   // OnOverlayClosed -> CleanupAfterRun
            _active = true;   // pending restart; panic clears it before the posted callback
            Dispatcher.UIThread.Post(() => { if (_active && _state == null) { _active = false; StartRun(isRestart: true); } });
        }

        private static void OnOverlayClosed()
        {
            _spawning = false;
            _runTimer?.Stop();
            EndField();
            ChaosWaveTimerWindow.CloseActive();   // WPF CleanupAfterRun :3321
            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            CleanupAfterRun();
        }

        private static void CloseBoonBar()
        {
            try { _boonBar?.Close(); } catch (Exception ex) { Log.Debug("Chaos boon bar close: {E}", ex.Message); }
            _boonBar = null;
        }

        /// <summary>WPF CleanupAfterRun :3322 (also reached by ForceShutdown / panic).</summary>
        private static void CleanupAfterRun()
        {
            ChaosHubWindow.SetCompanionActive(false);
            ChaosBackdropWindow.CloseActive();   // WPF CleanupAfterRun :3321; EndRun keeps the plate under the recap
            ChaosUnlockCardOverlay.CloseActive();
            _lessonCardPaused = false;
            StopToys();
            Overlays.ChaosFlashOverlay.CloseActive();   // WPF ForceShutdown :3175
            _dvdBannerOn = false;
            ChaosDvdOverlay.CloseActive();   // WPF CleanupAfterRun (:3319), ForceShutdown (:3179)
            try { ChaosPopText.ShutdownPool(); } catch (Exception ex) { Log.Debug("Chaos pop text: {E}", ex.Message); }   // WPF :3188/:3330
            ChaosEffectBannerOverlay.CloseActive();   // WPF ForceShutdown :3180 / CleanupAfterRun :3320
            CloseBoonBar();
            ChaosCursorGlowOverlay.CloseActive();   // WPF CleanupAfterRun :3323
            ChaosVibeTrailOverlay.CloseActive();   // WPF ForceShutdown :3184 / CleanupAfterRun :3324
            ChaosEStimOverlay.CloseActive();   // WPF ForceShutdown :3185 / :3325
            Overlays.ChaosFieldFxOverlay.CloseActive();   // WPF ChaosModeService.cs:3186/3239/3326
            ChaosSkiaFxOverlay.CloseActive();   // WPF CleanupAfterRun
            CloseFx();   // WPF CleanupAfterRun :3338 (also covers an overlay closed mid-run)
            _runTimer = null;
            _hud = null;
            _overlay = null;
            if (_state != null) _state.PropertyChanged -= OnTunnelStateChanged;
            ChaosTunnelWindow.EndActive();
            _state = null;
            UnhookRunVideo();   // WPF CleanupAfterRun :3318
            _heavyUntilUtc = DateTime.MinValue;
            _active = false;
            _spawning = false;
            _manualPaused = false;
            _draftPaused = false;
            _pendingWave = 0;
            _lessonCardsAfterDraft = false;
            _pendingLessonCards.Clear();
            _endingSoonFired = false;
        }
    }
}
