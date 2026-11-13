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
    /// <para>ponytail: still NOT here: darters, the Brittle,
    /// the lucky golden (and its gold/Gold Digger droplets), the heart and the behavioral bubbles
    /// (Echo/Bound) - SpawnTick spawns treats, plain live variants, the Tease and the Chaperone; every
    /// motion other than FloatUp, RainDown and Chaperone roaming, chain reaction, cam-girl tips,
    /// treat lessons, the announcer and the achievement credit; slow-mo,
    /// the perf backoff on the cadence; only the Gif Rain payload fires; the
    /// first-time bonus pop text.
    /// chaos barks (run started/completed, gold-first, defuse/detonate) still need their callers
    /// wired to CoreBark (chaos-payout-barks). NO FOCUS TO DEFUSE, last breath and UNLEASHED
    /// announcer callers remain missing even though the announcer overlay is now present.
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
            Volatile.Write(ref _rippleTarget, _spawning && run != null && !_manualPaused && !_lessonCardPaused
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
            if (!_spawning || run == null || _manualPaused || _lessonCardPaused) return false;
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
        private static double _freezeRemainingSec;
        private static bool _active, _spawning, _manualPaused, _endingSoonFired, _lessonCardPaused, _draftPaused;
        private static int _pendingWave, _runsAtStart, _draftsThisRun;
        private static bool _scriptThreat, _scriptSnapped, _scriptDraft, _sinRigged, _sinPending;
        private static bool _lessonCardsAfterDraft;
        private static readonly List<ChaosUnlockCardInfo> _pendingLessonCards = new();

        static ChaosRunHost() => ChaosLessons.LessonCompleted += OnLessonCompleted;
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
                _overlay = ChaosOverlayWindow.ForRun();
                _overlay.OnRunAgain = RunAgain;
                _overlay.OnDismissed = OnOverlayClosed;
                _overlay.Show();
                ChaosEffectBannerOverlay.EnsureCreated();   // WPF :410 birth the banner window NOW, not mid-chaos
                Overlays.ChaosFieldFxOverlay.EnsureCreated();   // WPF ChaosModeService.cs:411
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
            _heavyUntilUtc = DateTime.MinValue;   // WPF ChaosModeService.cs:405
            HookRunVideo();   // WPF ChaosModeService.cs:504
            _runsAtStart = ChaosMeta.State.RunsCompleted; _draftsThisRun = 0;
            _scriptThreat = _scriptSnapped = _scriptDraft = _sinRigged = _sinPending = false;
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
                ChaosAnnouncerOverlay.Announce($"◈ {boon.Name}", ChaosAnnounceKind.Mantra, artKey: boon.Id);
            }
            ChaosRunEffects.ApplyLifetimeBoons(_state);
            ChaosSkiaFxOverlay.EnsureCreated();   // WPF ChaosModeService.cs:412, idle run-start prewarm
            _hud?.SetPreRunExpanded(false);
            _hud?.SetClockVisible(_state.ShowWaveTimer);
            // WPF :450 Pocket Watch: birth the wave-countdown window now (keep-alive contract).
            if (_state.ShowWaveTimer) ChaosWaveTimerWindow.EnsureCreated();
            if (ChaosMeta.IsBoonActive("e_stim")) ChaosEStimOverlay.EnsureCreated();   // WPF ChaosModeService.cs:453
            _state.StartShields = _state.Shields;
            _thoughtAccumSec = 0;
            StartToys();
            _waveDetonatedAt = _state.Detonated;
            _lastComboBigFired = 0;
            _invulnUntilUtc = DateTime.MinValue;
            _regenPopCount = 0;
            ChaosRunLessons.OnRunStarted();   // WPF :399
            // ponytail: NotifyChaosRunStarted bark (WPF :389) -> chaos-payout-barks.
            _spawning = true;
            _chromeTicks = 0;
            _fx = ChaosFxWindow.Open();   // WPF BeginRun :500, one impact-vignette window per run
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ChaosRunEngine.TICK_SEC) };
            _runTimer.Tick += (_, _) => Tick();
            _runTimer.Start();

            _spawnSerial = _ordinarySpawns = _teaseDenied = 0; _teaseStreakBarked = false;   // WPF :466
            if (_overlay != null) BubbleOverlay.BeginChaos(_overlay, OnBubblePopped, OnDefused, OnDetonated, CanChannelDefuse, OnTeaseTouched, OnTeaseDenied, OnTreatExpired);
            RefreshRippleInput();
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
                if (enabled.Contains("video") && (waveLeft < 14 || runLeft < 18))
                    enabled = enabled.Where(id => id != "video").ToList();

                if (!cfg.ScriptedFirstRun && ChaosMeta.AtLeast(ChaosRank.Tempted)
                    && Rng.NextDouble() < ChaosTuning.CHAPERONE_SPAWN_CHANCE * (cfg.Difficulty == ChaosDifficulty.Easy ? 0.5 : 1.0))
                {
                    SpawnChaperone(effIntensity);
                    if (_spawnTimer != null)
                        _spawnTimer.Interval = TimeSpan.FromMilliseconds(ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult));
                    return;
                }
                // WPF :1301 (after the Chaperone roll at :1264): a Tease replaces the ordinary slot, never the scripted first run.
                if (TrySpawnTease(cfg, effIntensity))
                {
                    if (_spawnTimer != null) _spawnTimer.Interval = TimeSpan.FromMilliseconds(
                        ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult));
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
                BubbleOverlay.SpawnChaos(spec);

                if (_state.PrismChance > 0 && Rng.NextDouble() < _state.PrismChance)
                {
                    ChaosMeta.MarkDiscovered("bubble:prism");
                    BubbleOverlay.SpawnChaos(ChaosBubbleVariants.BuildPrism(effIntensity, cfg.EffectIntensity,
                        treatOnly: _state.PrismTreatOnly));
                }
            }
            if (_spawnTimer != null)
                _spawnTimer.Interval = TimeSpan.FromMilliseconds(
                    ChaosFieldMath.SpawnIntervalMs(intensity, cfg.DifficultyMult, cfg.SpawnRateMult));
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
            EffectLabel(b);   // WPF BubbleService.cs:4710-4712: label before the pickup or treat callback
            if (spec.IsFreeze)
            {
                _state.Score += 140 * _state.TotalMult;
                _state.Combo++;
                _state.Heat = Math.Min(1.0, _state.Heat + 0.05);
                CreditPop();
                ChaosLessons.Tick("freeze_trigger");
                _state.PushEvent(Loc.Get("chaos_freeze_event"));   // WPF :2391 prefixes a snowflake; no emoji in event lines
                CheckComboMilestone();
                ActivateFreeze();
                return;
            }
            CountRegenPop();   // WPF ChaosModeService :1768
            if (spec.IsPrism)
            {
                FireScaledPayload(spec.Payload);
                double prismPts = ChaosFieldMath.ApplyPrismPop(_state, spec);
                ShowPopScore(prismPts);
                ChaosLessons.Tick("taking_chances");   // WPF ChaosLessonHooks.OnPrismPopped
                _state.PushEvent($"🔮 prism! 10x · {spec.Payload.DisplayName} fires");
                Pulse(Color.FromRgb(0xC8, 0xA8, 0xFF), 0.40);
                CreditPop();   // WPF ChaosModeService.cs:1844
                CheckComboMilestone();
                return;
            }
            spec.Payload.Fire();   // benign pop = a treat
            FirstTime(ChaosFirstTimes.Taste);   // WPF ChaosModeService :1844
            if (spec.VariantId == "subliminal") ChaosLessons.Tick("intrusive_thoughts");   // WPF OnTreatPopped
            bool focusWasLow = _state.FocusLow;
            double pts = ChaosFieldMath.ApplyTreatPop(_state, spec, pendulum: 1.0, ChaosFieldMath.ChanceFlip(_state, Rng));
            if (focusWasLow)
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 34,
                    $"+{(int)ChaosFieldMath.TreatFocusGain(spec.PayMult)} FOCUS", Color.FromRgb(0x7A, 0xE0, 0xFF));
            ShowPopScore(pts);
            CreditPop();   // WPF ChaosModeService.cs:1873
            Pulse(Color.FromRgb(255, 200, 235), 0.10);
            if (spec.PayMult > 1) _state.PushEvent("🪨 heavy drop! x3");
            BodyBuzz(b);
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
        private static bool CanChannelDefuse(AmbientBubble b) =>
            _state != null && (_freezeRemainingSec > 0 || _state.Focus >= ChaosTuning.DEFUSE_COST);

        /// <summary>WPF OnDefused (:1984): the core snap (cost, counts, score, Drip Feed), the Snap
        /// Chain window and Playing with Fire's brink gold. Pendulum is 1.0 until its boon lands.</summary>
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
            ChaosRunLessons.OnDefuseCompleted(fuseSecLeft, b.DefusedViaChannel);
            var (pts, lastBreath, slowburn) = ChaosFieldMath.ApplyDefuse(_state, spec, fuseSecLeft,
                b.DefusedViaChannel, free: _freezeRemainingSec > 0, pendulum: 1.0, ChaosFieldMath.ChanceFlip(_state, Rng));
            // WPF ChaosLessonHooks.cs:161, 250: live rain remains busy after its spawn window.
            if (ChaosGifCascadeOverlay.IsRaining) ChaosLessons.Tick("blindfold");
            ShowPopScore(pts);
            CreditPop();   // WPF ChaosModeService.cs:2025
            if (_state.LastSecondGoldEnabled && fuseSecLeft <= 1.0)
            {
                int gold = ChaosRunEngine.GoldScaled(_state, Rng.Next(5, 10));
                BankGold(gold);
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                    $"+{gold} {ChaosGlyphs.Gold}", Color.FromRgb(0xFF, 0xD7, 0x00));
                _state.PushEvent($"{ChaosGlyphs.Gold} playing with fire +{gold} gold");
                Pulse(Color.FromRgb(255, 140, 60), 0.30);
            }
            // WPF ChaosModeService.cs:2035-2036: every snap emits Size Queen.
            if (_state.RippleEnabled) BubbleOverlay.TriggerChaosRipple(b);
            if (lastBreath > 1)
            {
                _state.PushEvent($"⏱ last breath! x{lastBreath:0}");
                Pulse(Color.FromRgb(255, 215, 0), 0.40);   // gold flash (WPF :2045)
            }
            else Pulse(Color.FromRgb(90, 255, 150), 0.16);   // soft green confirm
            if (slowburn > 1) _state.PushEvent("🐌 slow burn! x3");
            _state.PushEvent($"✔ snapped {spec.Payload.DisplayName}");
            CheckComboMilestone();
            // ponytail: slow_fuses/blindfold lessons, aftermath and
            // the brink narrative -> chaos-payout-fallin-d; the defuse barks -> chaos-payout-barks.
        }

        /// <summary>WPF OnDetonated (:2055) + OnChannelBroken's feed line: the payload fires, the loop
        /// is no longer clean, and the cheapest protection pays (ChaosFieldMath.ApplyDetonation).</summary>
        internal static void OnDetonated(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused) return;
            switch (b.BrokenReason)
            {
                case "nofocus":   // WPF OnPlayerPress :4502 + OnChannelBroken :1943
                    Label(b, "NO FOCUS", Color.FromRgb(0xFF, 0x46, 0x46), -(b.Size / 2.0 + 30));
                    ChaosOverlayWindow.ChaosSfx.Play("focus_empty", 0.55f);
                    Pulse(Color.FromRgb(255, 80, 80), 0.22);
                    _hud?.FlashFocusBar();
                    _state.PushEvent("✋ no focus — it triggers in your grip");
                    break;
                case "click": _state.PushEvent("💥 a tap isn't a hold"); break;
                case "release": _state.PushEvent("💥 you let go"); break;
            }
            // Other payload runners and the ambient remap remain unported.
            EffectLabel(b);   // WPF Detonate :4862
            FirePayloadForDetonation(spec);
            ChaosRunLessons.OnDetonation();   // silk_touch: the loop is no longer clean
            string name = spec.Payload.DisplayName;
            double s = spec.Strength / 100.0;
            switch (ChaosFieldMath.ApplyDetonation(_state, BubbleOverlay.Field.Clock() < _invulnUntilUtc))
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
            // ponytail: the first-bare-detonation narrative -> chaos-payout-fallin-d; the detonation barks -> chaos-payout-barks.
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
        // ponytail: combo barks (WPF :3080/:3090 NotifyChaosComboBig/Milestone) wait on chaos-payout-barks.
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
                ChaosOverlayWindow.ChaosSfx.Play("streak_milestone", 0.5f);   // WPF :3081
                ChaosAnnouncerOverlay.Announce($"{ConditioningControlPanel.Localization.Loc.Get("label_streak")} ×{t}", ChaosAnnounceKind.Streak,
                    artKey: "streak", subText: $"×{t}");                       // WPF :3082
                Pulse(Color.FromRgb(255, 230, 120), 0.55);
            }
            if (combo % 10 == 0)
            {
                _state!.PushEvent($"{ConditioningControlPanel.Localization.Loc.Get("label_streak")} x{combo}");
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

        /// <summary>WPF ChaosFirstTimes.TryAward + OnFirstTimeAwarded (:209): bank once, then a feed
        /// line and a float where the hand was.</summary>
        private static void FirstTime(string bonusId)
        {
            if (_state == null || ChaosFirstTimes.IsAwarded(bonusId) || !ChaosFirstTimes.TryAward(bonusId)) return;
            int amount = ChaosFirstTimes.Amounts[bonusId];
            string text = $"+{amount} {ChaosGlyphs.Drops} {ChaosFirstTimes.Labels.GetValueOrDefault(bonusId, bonusId)}";
            _state.PushEvent(text);
            PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY - 30, text, Color.FromRgb(0xC8, 0xA8, 0xFF));
        }

        /// <summary>WPF ShowPopScore (:1685), Blank Eyes.</summary>
        private static void ShowPopScore(double pts)
        {
            if (!ChaosFieldMath.ShowsPopScore(_state?.ShowPopScores == true, pts)) return;
            PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                "+" + ((long)pts).ToString("N0"), Color.FromRgb(0xFF, 0xE9, 0xA0));
        }

        private static DateTime _heavyUntilUtc = DateTime.MinValue;
        private static MandatoryVideoScheduler? _runVideo;

        // WPF ChaosModeService.cs:503-504,2164-2174,2260: a mandatory video that ends mid-run keeps
        // the heavy gate shut 3 s more (never shortening an existing floor).
        private static void HookRunVideo()
        {
            UnhookRunVideo();
            _runVideo = CoreEngine.Video;
            if (_runVideo != null) _runVideo.VideoEnded += OnVideoEndedDuringRun;
        }

        internal static void OnVideoEndedDuringRun()
        {
            var until = BubbleOverlay.Field.Clock().AddSeconds(3);
            if (until > _heavyUntilUtc) _heavyUntilUtc = until;
        }

        private static void UnhookRunVideo()
        {
            // Unsubscribe the captured publisher even if CoreEngine.Video has since changed.
            if (_runVideo != null) _runVideo.VideoEnded -= OnVideoEndedDuringRun;
            _runVideo = null;
        }

        // WPF ChaosModeService.cs:2155-2156,2295-2322: drop overlapping heavy
        // detonations, including during the off-thread pool pick, without queuing.
        internal static void FirePayloadForDetonation(EffectBubbleSpec spec)
        {
            var payload = spec.Payload;
            bool heavy = payload.Kind is EffectBubblePayloadKind.GifCascade or EffectBubblePayloadKind.Video;
            var now = BubbleOverlay.Field.Clock();
            if (heavy && (CoreEngine.Video?.IsPlaying == true || ChaosGifCascadeOverlay.IsRaining || now < _heavyUntilUtc))
            {
                // WPF ChaosModeService.cs:2308-2309: drop only the effect, with a feed line.
                Log.Information("Chaos: dropped {Kind} detonation: a heavy effect is already running", payload.Kind);
                _state?.PushEvent(Loc.GetF("chaos_heavy_fizzle", payload.DisplayName));
                return;
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

        /// <summary>One RunTick, the clock half of WPF ChaosModeService.cs:929-1099.</summary>
        internal static void Tick()
        {
            if (!_spawning || _state == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            if (_overlay is not { IsVisible: true }) return;   // P01: nothing ticks while hidden
            if (++_chromeTicks >= 4)
            {
                _chromeTicks = 0;
                if (CoreEngine.Video?.IsPlaying == true) RaiseDvdAboveVideo();
                KeepChromeTopmost();
            }
            double elapsed = ChaosRunEngine.Advance(_state);
            if (_state.Config.ScriptedFirstRun && TickScriptedDraft()) return;
            UpdateHeatTint();
            if (_freezeRemainingSec > 0)
            {
                _freezeRemainingSec -= ChaosRunEngine.TICK_SEC;
                if (_freezeRemainingSec <= 0) EndFreeze();
            }
            _hud?.SetCursorOnLive(BubbleOverlay.CursorOnLive);
            // WPF ChaosModeService.cs:984-989: gather only while the run clock advances.
            if (_state.RippleCooldown > 0)
            {
                _state.RippleCooldown -= ChaosRunEngine.TICK_SEC;
                if (_state.RippleReady) RippleCue("toy_ready", 0.3f);
            }
            TickToys();
            // WPF :1068 T-10s, once per run (a Relapse extension does not re-arm it).
            if (!_endingSoonFired && ChaosRunEngine.IsEndingSoon(_state, elapsed))
            {
                _endingSoonFired = true;
                ChaosAnnouncerOverlay.Announce("the hole is closing…", ChaosAnnounceKind.Depth,
                    artKey: "ending_soon", subText: "ten seconds");
                _state.PushEvent("⏳ ten seconds. make them count.");
            }
            var end = ChaosRunEngine.CheckEnd(_state, elapsed);
            if (end == ChaosRunEngine.EndCheck.RunOver) { EndRun(); return; }
            if (end == ChaosRunEngine.EndCheck.Relapse)
            {
                ChaosAnnouncerOverlay.Announce("☠ RELAPSE — one more loop", ChaosAnnounceKind.Temptation,
                    artKey: "relapse", subText: "one more loop");   // WPF :1083
                _state.PushEvent("☠ relapse. one more loop — everything drips double");
            }
            var (newWave, waveLen) = ChaosRunEngine.WaveAt(_state, elapsed);
            // WPF :1094 Pocket Watch: the wave countdown (+ a live score line).
            if (_state.ShowWaveTimer)
                ChaosWaveTimerWindow.Update(newWave, _state.WaveCount, waveLen - (elapsed % waveLen), _state.Score);
            if (newWave > _state.WaveIndex)
            {
                // WPF BeginWaveTransition with drafts off (:1442-1449): lessons, tip, then roll straight on.
                // Claim the draft pause before loop lessons fire, so their cards are deferred.
                if (_state.Config.BoonDraftEnabled) _draftPaused = true;
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
                    var options = DealDraft(rig: true);
                    _overlay!.ShowBoonDraft(_state.WaveIndex, options, ChooseBoon,
                        _state.Config.DraftAutoResumeSec, _state.RerollsLeft, RerollDraft);
                }
                else
                {
                    _state.WaveIndex = newWave;
                    _state.ActIndex = ChaosRunEngine.ActFor(newWave);
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
                    ChaosAnnouncerOverlay.Announce(boon.Name, ChaosAnnounceKind.Temptation, artKey: boon.Id);   // WPF prefixes a skull; IconGuard keeps emoji out, the Temptation colour carries it
                }
                else ChaosAnnouncerOverlay.Announce($"◈ {boon.Name}", ChaosAnnounceKind.Mantra, artKey: boon.Id);
            }
            _state.WaveIndex = _pendingWave;
            _state.ActIndex = ChaosRunEngine.ActFor(_pendingWave);
            _pendingWave = 0;
            _overlay?.ShowReadyGo(ResumeAfterDraft);
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
                var cards = _pendingLessonCards.ToArray();
                _pendingLessonCards.Clear();
                foreach (var card in cards) ChaosUnlockCardOverlay.Show(card, ResumeAfterLessonCard, autoDismiss: false);
                return;
            }
            _draftPaused = false;
            if (!_manualPaused && !_lessonCardPaused) _spawnTimer?.Start();
            if (_state?.WelcomeShowerEnabled == true) SpawnWelcomeShower();
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
            var sound = ContentLocator.Resolve(System.IO.Path.Combine("Resources", "sounds", $"chime{Rng.Next(1, 4)}.mp3"));
            if (System.IO.File.Exists(sound))
            {
                var settings = CoreSettings.Current;
                float volume = (float)Math.Pow(settings.MasterVolume / 100.0 * settings.BubblesVolume / 100.0, 1.5) * 0.25f;
                CoreAudio.PlayOneShot(sound, volume, "bubble-chime");
            }
            _state.PushEvent(Loc.Get("chaos_welcome_shower"));
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

        /// <summary>WPF BankGold (:1709): persists at once; the very first gold gets its feed line.</summary>
        private static void BankGold(int gold)
        {
            if (gold <= 0 || _state == null) return;
            bool first = !ChaosMeta.State.SeenGoldFirst;
            if (first) ChaosMeta.State.SeenGoldFirst = true;
            ChaosMeta.AddGold(gold);   // persists
            if (first) _state.PushEvent($"{ChaosGlyphs.Gold} gold. she takes it at her bench.");
            // ponytail: NotifyChaosGoldFirst bark (WPF :1721) -> chaos-payout-barks.
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
            // ponytail: NotifyChaosRunCompleted((int)finalXp, difficulty) bark (WPF :3269) -> chaos-payout-barks.

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
            EffectPayload.GlobalDurationMult = 1.0;
            ChaosEffectBannerOverlay.End("freeze");
            _fx?.EndEdgeHold();
            ChaosOverlayWindow.ChaosSfx.Play("freeze_shatter", 0.5f);
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
            // The field cannot yet place or move a summoned darter. Do not spend the whistle.
            if (toy.Id == "rabbit_caller")
            {
                if (_spawning && !_manualPaused && !_lessonCardPaused && _overlay is { IsVisible: true })
                    ChaosOverlayWindow.ChaosSfx.Play("toy_denied", 0.4f);
                return;
            }
            var r = ChaosToyRules.Use(_state, toy, _spawning && !_manualPaused && !_lessonCardPaused && _overlay is { IsVisible: true });
            if (r == ChaosToyRules.UseResult.Denied) ChaosOverlayWindow.ChaosSfx.Play("toy_denied", 0.4f);
            if (r == ChaosToyRules.UseResult.Fired && FireToyEffect(toy))
                ChaosFirstTimes.TryAward(ChaosFirstTimes.Play);
        }

        private static void RunAgain()
        {
            _overlay?.Close();   // OnOverlayClosed -> CleanupAfterRun
            Dispatcher.UIThread.Post(() => StartRun(isRestart: true));
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
