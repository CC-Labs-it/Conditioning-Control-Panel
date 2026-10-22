using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
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
    /// <para>ponytail: still NOT here, so the hub's FALL IN stays a logged no-op: live bubbles (fuses,
    /// snap/defuse, detonation, shields) and therefore the freeze pickup, darters, the Brittle,
    /// the lucky golden (and its gold/Gold Digger droplets), the heart and the behavioral bubbles
    /// (Echo/Chaperone/Bound/Tease) - SpawnTick spawns only treat variants until they land; every
    /// motion but FloatUp, treat rot, chain reaction, the Body Buzz shockwave, cam-girl tips, combo
    /// milestones, lessons/first-times, pulses, SFX, announcer and the achievement credit; slow-mo,
    /// freeze hold and the perf backoff on the cadence; the payload firer is a logging no-op; the
    /// pop text is the <see cref="PopText"/> hook (chaos-pop-text lane). Also missing: the boon draft
    /// on a wave boundary (BeginWaveTransition rolls straight on, as WPF does with drafts off) and
    /// the payout (AddXP, AwardRunRewards, RevealService.Sync, the rank card) - the recap shows the
    /// computed XP and never saves (lane chaos-payout-fallin). The FX window's Pulse/FreezeBurst/edge-hold
    /// callers (pops, detonations, the freeze power-up) are not wired yet; only the heat tint is driven from here.</para>
    ///
    /// <para><see cref="Tick"/> is the stepped clock: the DispatcherTimer only calls it, tests call
    /// it directly. It does nothing while the overlay is hidden (P01).</para>
    /// </summary>
    internal static class ChaosRunHost
    {
        private static ChaosRunState? _state;
        private static ChaosHudWindow? _hud;
        private static ChaosOverlayWindow? _overlay;
        private static ChaosBoonBarOverlay? _boonBar;
        private static DispatcherTimer? _runTimer, _spawnTimer;
        private static int _spawnSerial, _ordinarySpawns;
        private static readonly Random Rng = new();

        /// <summary>WPF ChaosPopText.Show(xDip, yDip, text, color); seeded by the pop-text lane,
        /// a no-op until then.</summary>
        internal static Action<double, double, string, Color>? PopText;
        private static ChaosFxWindow? _fx;
        private static double _lastHeatTint = -1;
        private static bool _active, _spawning, _manualPaused, _endingSoonFired;

        internal static ChaosRunState? State => _state;
        internal static ChaosOverlayWindow? Overlay => _overlay;
        internal static ChaosHudWindow? Hud => _hud;
        internal static ChaosBoonBarOverlay? BoonBar => _boonBar;
        internal static ChaosFxWindow? Fx => _fx;
        internal static bool IsActive => _active;
        /// <summary>WPF IsDescending: the clock is running (countdown over, recap not up).</summary>
        internal static bool IsDescending => _spawning;
        internal static bool IsManuallyPaused => _manualPaused;

        public static void StartRun(ChaosRunConfig? config = null, bool isRestart = false)
        {
            if (_active) return;
            var cfg = config ?? ChaosRunConfig.FromSettings();
            try
            {
                _state = new ChaosRunState(cfg);
                _active = true;
                _lastHeatTint = -1;
                _hud = new ChaosHudWindow(_state);
                _hud.Show();
                _overlay = ChaosOverlayWindow.ForRun();
                _overlay.OnRunAgain = RunAgain;
                _overlay.OnDismissed = OnOverlayClosed;
                _overlay.Show();
                ChaosEffectBannerOverlay.EnsureCreated();   // WPF :410 birth the banner window NOW, not mid-chaos
                _overlay.ShowCountdown(BeginRun, shortFlash: isRestart);   // 1s flash on RunAgain
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ChaosRunHost.StartRun failed");
                ForceShutdown();
            }
        }

        /// <summary>The countdown's completion. WPF BeginRun also applies the start boon and
        /// lifetime boons and builds the toys; only the clock half is here.</summary>
        internal static void BeginRun()
        {
            if (!_active || _state == null || _spawning) return;
            ChaosRunEffects.ApplyLifetimeBoons(_state);
            _hud?.SetPreRunExpanded(false);
            _hud?.SetClockVisible(_state.ShowWaveTimer);
            // WPF BeginRun :417-420: the run-pick ribbon, rebuilt live on every drafted pick.
            var picks = _state.RunPickTiles;
            _boonBar = ChaosBoonBarOverlay.ForRun();
            _boonBar.SetPicks(picks);
            _boonBar.Show();
            picks.CollectionChanged += (_, _) => _boonBar?.SetPicks(picks);
            _state.StartShields = _state.Shields;
            _spawning = true;
            _fx = ChaosFxWindow.Open();   // WPF BeginRun :500, one impact-vignette window per run
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ChaosRunEngine.TICK_SEC) };
            _runTimer.Tick += (_, _) => Tick();
            _runTimer.Start();

            _spawnSerial = _ordinarySpawns = 0;
            if (_overlay != null) BubbleOverlay.BeginChaos(_overlay, OnBubblePopped);
            _spawnTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };   // WPF :510
            _spawnTimer.Tick += (_, _) => SpawnTick();
            _spawnTimer.Start();
        }

        /// <summary>WPF SpawnTick (:1101), the treat half. Re-times its own timer like WPF.</summary>
        internal static void SpawnTick()
        {
            if (!_spawning || _state == null || _manualPaused) return;
            if (_overlay is not { IsVisible: true }) return;   // P01
            var cfg = _state.Config;
            double intensity = _state.RunIntensity;
            double effIntensity = ChaosFieldMath.EffectiveIntensity(intensity, cfg.DifficultyMult);
            if (BubbleOverlay.ChaosCount < ChaosFieldMath.MaxConcurrent(intensity, cfg.DifficultyMult))
            {
                // Only treat variants until live bubbles land (see the ponytail note).
                IReadOnlyCollection<string> enabled = (cfg.EnabledVariants ?? ChaosBubbleVariants.AllIds())
                    .Where(id => ChaosBubbleVariants.All.Any(v => v.Id == id && !v.IsLive
                                 && v.PayloadKind != EffectBubblePayloadKind.BambiFreeze)).ToList();
                double waveLen = (double)_state.RunDurationSec / _state.WaveCount;
                double waveLeft = waveLen - (_state.ElapsedSec % waveLen);
                double runLeft = _state.RunDurationSec - _state.ElapsedSec;
                if (enabled.Contains("video") && (waveLeft < 14 || runLeft < 18))
                    enabled = enabled.Where(id => id != "video").ToList();

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

        /// <summary>WPF OnBenignPopped (:1767): the prism and ordinary-treat branches.</summary>
        internal static void OnBubblePopped(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused) return;
            if (spec.IsPrism)
            {
                FireScaledPayload(spec.Payload);
                double prismPts = ChaosFieldMath.ApplyPrismPop(_state, spec);
                ShowPopScore(prismPts);
                _state.PushEvent($"🔮 prism! 10x · {spec.Payload.DisplayName} fires");
                return;
            }
            spec.Payload.Fire();   // benign pop = a treat
            bool focusWasLow = _state.FocusLow;
            double pts = ChaosFieldMath.ApplyTreatPop(_state, spec, pendulum: 1.0, ChaosFieldMath.ChanceFlip(_state, Rng));
            if (focusWasLow)
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 34,
                    $"+{(int)ChaosFieldMath.TreatFocusGain(spec.PayMult)} FOCUS", Color.FromRgb(0x7A, 0xE0, 0xFF));
            ShowPopScore(pts);
            if (spec.PayMult > 1) _state.PushEvent("🪨 heavy drop! x3");
        }

        /// <summary>WPF ShowPopScore (:1685), Blank Eyes.</summary>
        private static void ShowPopScore(double pts)
        {
            if (!ChaosFieldMath.ShowsPopScore(_state?.ShowPopScores == true, pts)) return;
            PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                "+" + ((long)pts).ToString("N0"), Color.FromRgb(0xFF, 0xE9, 0xA0));
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
            BubbleOverlay.EndChaos();
        }

        /// <summary>One RunTick, the clock half of WPF ChaosModeService.cs:929-1099.</summary>
        internal static void Tick()
        {
            if (!_spawning || _state == null || _manualPaused) return;
            if (_overlay is not { IsVisible: true }) return;   // P01: nothing ticks while hidden
            double elapsed = ChaosRunEngine.Advance(_state);
            UpdateHeatTint();
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
            var (newWave, _) = ChaosRunEngine.WaveAt(_state, elapsed);
            if (newWave > _state.WaveIndex)
            {
                // WPF BeginWaveTransition with drafts off (:1449): roll straight into the next wave.
                _state.AllLiveNextWave = false;
                _state.WaveIndex = newWave;
                _state.ActIndex = ChaosRunEngine.ActFor(newWave);
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
            if (!_spawning) return;
            _manualPaused = !_manualPaused;
            BubbleOverlay.HoldChaos(_manualPaused);   // WPF :267-268 freeze + input lock
            _state?.PushEvent(_manualPaused ? "⏸ held. the hole waits." : "▶ sinking again");
            _hud?.SetPausedUi(_manualPaused);
        }

        public static void RequestStop()
        {
            if (_manualPaused) { _manualPaused = false; _hud?.SetPausedUi(false); }
            if (_spawning) EndRun();
        }

        /// <summary>Panic / app exit: close everything, no recap, no payout. Safe when idle.</summary>
        public static void ForceShutdown()
        {
            ChaosAnnouncerOverlay.CloseActive();   // WPF :3177; also clears a queue left by an idle caller
            ChaosCursorGlowOverlay.CloseActive();   // WPF ForceShutdown :3183
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
            ChaosGifCascadeOverlay.CloseActive();   // WPF ForceShutdown :3176
            CloseBoonBar();   // WPF ForceShutdown :3181
            CloseFx();   // WPF ForceShutdown :3189
            CleanupAfterRun();
        }

        /// <summary>WPF EndRun (:3200) minus the payout: the same XP arithmetic, shown not banked.</summary>
        private static void EndRun()
        {
            if (!_spawning || _state == null) return;
            _spawning = false;
            _runTimer?.Stop();
            EndField();
            try { ChaosPopText.ShutdownPool(); } catch (Exception ex) { Log.Debug("Chaos pop text: {E}", ex.Message); }   // WPF EndRun :3241

            double durMin = Math.Max(1, _state.RunDurationSec) / 60.0;
            double capBase = 250.0 * durMin * _state.Config.DifficultyMult;
            double baseXp = Math.Min(_state.Score, capBase);
            double skillMult = _state.SkillMult;
            double finalXp = baseXp * skillMult;
            long previousBest = ChaosMeta.State.BestScore;

            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            _hud = null;
            Overlays.ChaosFlashOverlay.CloseActive();   // WPF EndRun :3228: the wash never sits over the results card
            ChaosGifCascadeOverlay.CloseActive();   // WPF EndRun :3229
            ChaosAnnouncerOverlay.CloseActive();   // WPF :3230
            ChaosEffectBannerOverlay.CloseActive();   // WPF EndRun :3233
            CloseBoonBar();   // WPF EndRun :3235
            ChaosCursorGlowOverlay.CloseActive();   // WPF EndRun :3236
            ChaosVibeTrailOverlay.CloseActive();   // WPF EndRun :3237
            CloseFx();   // WPF EndRun :3242
            _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned: 0);
            Log.Information("Chaos run complete (no payout on this head): base {Base:0} x skill {Mult:0.0} = {Final:0} XP",
                baseXp, skillMult, finalXp);
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
            Overlays.ChaosFlashOverlay.CloseActive();   // WPF ForceShutdown :3175
            try { ChaosPopText.ShutdownPool(); } catch (Exception ex) { Log.Debug("Chaos pop text: {E}", ex.Message); }   // WPF :3188/:3330
            ChaosEffectBannerOverlay.CloseActive();   // WPF ForceShutdown :3180 / CleanupAfterRun :3320
            CloseBoonBar();
            ChaosCursorGlowOverlay.CloseActive();   // WPF CleanupAfterRun :3323
            ChaosVibeTrailOverlay.CloseActive();   // WPF ForceShutdown :3184 / CleanupAfterRun :3324
            CloseFx();   // WPF CleanupAfterRun :3338 (also covers an overlay closed mid-run)
            _runTimer = null;
            _hud = null;
            _overlay = null;
            _state = null;
            _active = false;
            _spawning = false;
            _manualPaused = false;
            _endingSoonFired = false;
        }
    }
}
