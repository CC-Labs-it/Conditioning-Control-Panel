using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Media;
using ConditioningControlPanel.Avalonia.Platform;
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
    /// <para>ponytail: still NOT here: the freeze pickup, darters, the Brittle,
    /// the lucky golden (and its gold/Gold Digger droplets), the heart and the behavioral bubbles
    /// (Echo/Chaperone/Bound/Tease) - SpawnTick spawns only treat variants until they land; every
    /// motion but FloatUp, treat rot, chain reaction, the Body Buzz shockwave, cam-girl tips, combo
    /// milestones, lessons/first-times, pulses, SFX, announcer and the achievement credit; slow-mo,
    /// freeze hold and the perf backoff on the cadence; the payload firer is a logging no-op; the
    /// pop text is the <see cref="PopText"/> hook (chaos-pop-text lane). Also missing: the boon draft
    /// on a wave boundary (BeginWaveTransition rolls straight on, as WPF does with drafts off); every
    /// chaos bark (run started/completed, gold-first, defuse/detonate) waits for chaos-payout-fallin-c;
    /// the fuse ring is a two-phase ring (no flash phase, no danger breathing); the SNAP and NO FOCUS
    /// bubble labels (WPF BubbleService.cs:4500,4574) are not drawn. The hold and the Snap Chain window
    /// run on wall time (<c>BubbleOverlay.Field.Clock</c>), as WPF does (:4531, ChaosModeService :2000).
    /// The FX window's Pulse/FreezeBurst/edge-hold callers (pops, detonations, the freeze power-up)
    /// are not wired yet; only the heat tint is driven from here.</para>
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
        private static int _spawnSerial, _ordinarySpawns, _waveDetonatedAt;
        private static DateTime _invulnUntilUtc = DateTime.MinValue;   // Snap Chain, wall time (WPF :1916)
        /// <summary>The host's dice (loop-tip roll, chance flip, prism). Tests seed it and put it back.</summary>
        internal static Random Rng = new();

        /// <summary>WPF ChaosPopText.Show(xDip, yDip, text, color); seeded by the pop-text lane,
        /// a no-op until then.</summary>
        internal static Action<double, double, string, Color>? PopText;
        private static ChaosFxWindow? _fx;
        private static double _lastHeatTint = -1;
        private static bool _active, _spawning, _manualPaused, _endingSoonFired;
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
        /// lifetime boons and builds the toys; the clock, lifetime boons and toy buttons are here.</summary>
        internal static void BeginRun()
        {
            if (!_active || _state == null || _spawning) return;
            ChaosRunEffects.ApplyLifetimeBoons(_state);
            ChaosSkiaFxOverlay.EnsureCreated();   // WPF BeginRun :412: prewarm the sparkle surface
            _hud?.SetPreRunExpanded(false);
            _hud?.SetClockVisible(_state.ShowWaveTimer);
            // WPF BeginRun :417-420: the run-pick ribbon, rebuilt live on every drafted pick.
            var picks = _state.RunPickTiles;
            _boonBar = ChaosBoonBarOverlay.ForRun();
            _boonBar.SetPicks(picks);
            _boonBar.Show();
            picks.CollectionChanged += (_, _) => _boonBar?.SetPicks(picks);
            // WPF :450 Pocket Watch: birth the wave-countdown window now (keep-alive contract).
            if (_state.ShowWaveTimer) ChaosWaveTimerWindow.EnsureCreated();
            _state.StartShields = _state.Shields;
            StartToys();
            _waveDetonatedAt = _state.Detonated;
            _invulnUntilUtc = DateTime.MinValue;
            ChaosRunLessons.OnRunStarted();   // WPF :399
            // ponytail: NotifyChaosRunStarted bark -> chaos-payout-fallin-c.
            _spawning = true;
            _fx = ChaosFxWindow.Open();   // WPF BeginRun :500, one impact-vignette window per run
            _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(ChaosRunEngine.TICK_SEC) };
            _runTimer.Tick += (_, _) => Tick();
            _runTimer.Start();

            _spawnSerial = _ordinarySpawns = 0;
            if (_overlay != null) BubbleOverlay.BeginChaos(_overlay, OnBubblePopped, OnDefused, OnDetonated, CanChannelDefuse);
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
                // Treats and plain lives; the freeze pickup waits for its lane (see the ponytail note).
                IReadOnlyCollection<string> enabled = (cfg.EnabledVariants ?? ChaosBubbleVariants.AllIds())
                    .Where(id => ChaosBubbleVariants.All.Any(v => v.Id == id
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

        /// <summary>WPF CanChannelDefuse (:1927): the focus must cover the snap's cost (paid on completion).</summary>
        private static bool CanChannelDefuse(AmbientBubble b) =>
            _state != null && _state.Focus >= ChaosTuning.DEFUSE_COST;

        /// <summary>WPF OnDefused (:1984): the core snap (cost, counts, score, Drip Feed), the Snap
        /// Chain window and Playing with Fire's brink gold. Pendulum is 1.0 until its boon lands.</summary>
        internal static void OnDefused(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused) return;
            double fuseSecLeft = b.DefuseJudgeFuseSec;
            if (_state.DefuseInvulnMs > 0) _invulnUntilUtc = BubbleOverlay.Field.Clock().AddMilliseconds(_state.DefuseInvulnMs);   // WPF :2000
            var (pts, lastBreath, slowburn) = ChaosFieldMath.ApplyDefuse(_state, spec, fuseSecLeft,
                b.DefusedViaChannel, free: false, pendulum: 1.0, ChaosFieldMath.ChanceFlip(_state, Rng));
            ShowPopScore(pts);
            if (_state.LastSecondGoldEnabled && fuseSecLeft <= 1.0)
            {
                int gold = ChaosRunEngine.GoldScaled(_state, Rng.Next(5, 10));
                BankGold(gold);
                PopText?.Invoke(BubbleOverlay.ChaosLastPopX, BubbleOverlay.ChaosLastPopY + 30,
                    $"+{gold} {ChaosGlyphs.Gold}", Color.FromRgb(0xFF, 0xD7, 0x00));
                _state.PushEvent($"{ChaosGlyphs.Gold} playing with fire +{gold} gold");
            }
            if (lastBreath > 1) _state.PushEvent($"⏱ last breath! x{lastBreath:0}");
            if (slowburn > 1) _state.PushEvent("🐌 slow burn! x3");
            _state.PushEvent($"✔ snapped {spec.Payload.DisplayName}");
            // ponytail: defuse lessons, first-times, regen, aftermath, ripple, pulses, combo
            // milestones and the defuse barks are not here yet (chaos-payout-fallin-c).
        }

        /// <summary>WPF OnDetonated (:2055) + OnChannelBroken's feed line: the payload fires, the loop
        /// is no longer clean, and the cheapest protection pays (ChaosFieldMath.ApplyDetonation).</summary>
        internal static void OnDetonated(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused) return;
            switch (b.BrokenReason)
            {
                case "nofocus": _state.PushEvent("✋ no focus — it triggers in your grip"); break;
                case "click": _state.PushEvent("💥 a tap isn't a hold"); break;
                case "release": _state.PushEvent("💥 you let go"); break;
            }
            // ponytail: WPF FirePayloadForDetonation's ambient remap and one-heavy-at-a-time gate
            // land with the real payload firer (the Avalonia firer is a logging no-op).
            FireScaledPayload(spec.Payload);
            ChaosRunLessons.OnDetonation();   // silk_touch: the loop is no longer clean
            string name = spec.Payload.DisplayName;
            switch (ChaosFieldMath.ApplyDetonation(_state, BubbleOverlay.Field.Clock() < _invulnUntilUtc))
            {
                case ChaosFieldMath.Detonation.SnapChainHeld: _state.PushEvent($"⛓ snap chain holds ({name})"); break;
                case ChaosFieldMath.Detonation.ShieldAbsorbed: _state.PushEvent($"♥ resistance crumbles ({name})"); break;
                case ChaosFieldMath.Detonation.CollarHeld:
                    _state.PushEvent($"📿 the collar holds ({_state.CollarSaves} left)");
                    if (_state.UnleashedEnabled)
                    {
                        BubbleOverlay.Field.DefuseAllLive();
                        _state.PushEvent("📿 unleashed — the field lets go");
                    }
                    break;
                default: _state.PushEvent($"💥 {name} triggered!"); break;
            }
            // ponytail: SFX, pulses, shake, HUD flashes, narrative and the detonation barks -> chaos-payout-fallin-c.
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
                ChaosRunLessons.OnLoopCompleted();
                AwardLoopTip();
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
            ChaosSkiaFxOverlay.CloseActive();   // WPF ChaosModeService.ForceShutdown; before the idle return
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
            // ponytail: NotifyChaosGoldFirst bark -> chaos-payout-fallin-c (the CoreBark seam is changing).
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
            // ponytail: NotifyChaosRunCompleted((int)finalXp, difficulty) bark (WPF :3270) -> chaos-payout-fallin-c.

            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            _hud = null;
            Overlays.ChaosFlashOverlay.CloseActive();   // WPF EndRun :3228: the wash never sits over the results card
            ChaosGifCascadeOverlay.CloseActive();   // WPF EndRun :3229
            ChaosAnnouncerOverlay.CloseActive();   // WPF :3230
            ChaosDvdOverlay.CloseActive();   // WPF EndRun (:3232)
            ChaosEffectBannerOverlay.CloseActive();   // WPF EndRun :3233
            ChaosWaveTimerWindow.CloseActive();   // WPF EndRun :3234
            CloseBoonBar();   // WPF EndRun :3235
            ChaosCursorGlowOverlay.CloseActive();   // WPF EndRun :3236
            ChaosVibeTrailOverlay.CloseActive();   // WPF EndRun :3237
            ChaosEStimOverlay.CloseActive();   // WPF EndRun :3238
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
            X11PanicKey.ToyKey = null;
            X11PanicKey.ToyKeys = null;
            foreach (var b in _toyButtons.ToArray())
                try { b.Close(); } catch (Exception ex) { Log.Debug("Toy button close: {E}", ex.Message); }
            _toyButtons.Clear();
        }

        /// <summary>WPF TickActiveToys' toy loop. ponytail: no toy effect runs on this head yet (they
        /// live with the bubble field), so the glow state drops on the next tick.</summary>
        private static void TickToys()
        {
            if (_state == null) return;
            foreach (var t in ChaosToyRules.Tick(_state.ActiveToys, ChaosRunEngine.TICK_SEC))
                ChaosOverlayWindow.ChaosSfx.Play("toy_ready", 0.45f);
            foreach (var t in _state.ActiveToys) t.IsEffectActive = false;
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

        /// <summary>WPF UseToy, the gate + cooldown half. ponytail: the effect itself (vibe sweep,
        /// freeze, DVD, snap, rabbits, e-stim) and the first_play Spark award wait for the bubble
        /// field and the payout, which this head's run does not have yet.</summary>
        private static void UseToy(ChaosToyState toy)
        {
            if (_state == null) return;
            var r = ChaosToyRules.Use(_state, toy, _spawning && !_manualPaused);
            if (r == ChaosToyRules.UseResult.Denied) ChaosOverlayWindow.ChaosSfx.Play("toy_denied", 0.4f);
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
            StopToys();
            Overlays.ChaosFlashOverlay.CloseActive();   // WPF ForceShutdown :3175
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
            _active = false;
            _spawning = false;
            _manualPaused = false;
            _endingSoonFired = false;
        }
    }
}
