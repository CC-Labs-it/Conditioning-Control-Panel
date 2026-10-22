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
    /// milestones, lessons/first-times, the announcer and the achievement credit; slow-mo,
    /// freeze hold and the perf backoff on the cadence; the payload firer is a logging no-op; the
    /// pop text is the <see cref="PopText"/> hook (chaos-pop-text lane). Also missing: the boon draft
    /// on a wave boundary (BeginWaveTransition rolls straight on, as WPF does with drafts off); every
    /// chaos bark (run started/completed, gold-first, defuse/detonate) waits for lane chaos-payout-barks
    /// (stacked after companion-barks + barks-wpf: this base has no Core BarkEngine and Avalonia never
    /// seeds CoreBark); the announcer banners (NO FOCUS TO DEFUSE, last breath, UNLEASHED) wait for
    /// the announcer overlay. The colour pulse and screen shake are called with WPF's values through
    /// <see cref="PulseFx"/>/<see cref="ShakeFx"/>, unseeded until the fx window and shake land
    /// (chaos-payout-fallin-d). The hold and the Snap Chain window run on wall time
    /// (<c>BubbleOverlay.Field.Clock</c>), as WPF does (:4531, ChaosModeService :2000).</para>
    ///
    /// <para><see cref="Tick"/> is the stepped clock: the DispatcherTimer only calls it, tests call
    /// it directly. It does nothing while the overlay is hidden (P01).</para>
    /// </summary>
    internal static class ChaosRunHost
    {
        private static ChaosRunState? _state;
        private static ChaosHudWindow? _hud;
        private static ChaosOverlayWindow? _overlay;
        private static DispatcherTimer? _runTimer, _spawnTimer;
        private static int _spawnSerial, _ordinarySpawns, _waveDetonatedAt;
        private static DateTime _invulnUntilUtc = DateTime.MinValue;   // Snap Chain, wall time (WPF :1916)
        /// <summary>The host's dice (loop-tip roll, chance flip, prism). Tests seed it and put it back.</summary>
        internal static Random Rng = new();

        /// <summary>WPF ChaosPopText.Show(xDip, yDip, text, color); seeded by the pop-text lane,
        /// a no-op until then.</summary>
        internal static Action<double, double, string, Color>? PopText;
        /// <summary>WPF <c>_fx.Pulse(color, strength)</c> (ChaosFxWindow); unseeded until it lands.</summary>
        internal static Action<Color, double>? PulseFx;
        /// <summary>WPF <c>App.ScreenShake.Shake(intensity, ms)</c>; unseeded until it lands.</summary>
        internal static Action<double, int>? ShakeFx;
        private static bool _active, _spawning, _manualPaused;

        internal static ChaosRunState? State => _state;
        internal static ChaosOverlayWindow? Overlay => _overlay;
        internal static ChaosHudWindow? Hud => _hud;
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
            _state.StartShields = _state.Shields;
            _waveDetonatedAt = _state.Detonated;
            _invulnUntilUtc = DateTime.MinValue;
            ChaosRunLessons.OnRunStarted();   // WPF :399
            // ponytail: NotifyChaosRunStarted bark (WPF :389) -> chaos-payout-barks.
            _spawning = true;
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
            EffectLabel(b);   // WPF Bubble.Pop: ShowChaosEffectLabel before onBenignPop (:4685)
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
            Label(b, "SNAP", SnapColor);   // WPF CompleteDefuse :4577 / Pop :4684
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
                Pulse(Color.FromRgb(255, 140, 60), 0.30);
            }
            if (lastBreath > 1)
            {
                _state.PushEvent($"⏱ last breath! x{lastBreath:0}");
                Pulse(Color.FromRgb(255, 215, 0), 0.40);   // gold flash (WPF :2045)
            }
            else Pulse(Color.FromRgb(90, 255, 150), 0.16);   // soft green confirm
            if (slowburn > 1) _state.PushEvent("🐌 slow burn! x3");
            _state.PushEvent($"✔ snapped {spec.Payload.DisplayName}");
            // ponytail: defuse lessons, first-times, regen, aftermath, ripple, combo milestones and
            // the brink narrative -> chaos-payout-fallin-d; the defuse barks -> chaos-payout-barks.
        }

        /// <summary>WPF OnDetonated (:2055) + OnChannelBroken's feed line: the payload fires, the loop
        /// is no longer clean, and the cheapest protection pays (ChaosFieldMath.ApplyDetonation).</summary>
        internal static void OnDetonated(AmbientBubble b)
        {
            var spec = b.Spec;
            if (_state == null || spec == null || _manualPaused) return;
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
            // ponytail: WPF FirePayloadForDetonation's ambient remap and one-heavy-at-a-time gate
            // land with the real payload firer (the Avalonia firer is a logging no-op).
            EffectLabel(b);   // WPF Detonate :4862
            FireScaledPayload(spec.Payload);
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
                        BubbleOverlay.Field.DefuseAllLive();
                        _state.PushEvent("📿 unleashed — the field lets go");
                        Pulse(Color.FromRgb(255, 215, 0), 0.50);
                    }
                    break;
                default:   // WPF :2126
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

        /// <summary>WPF Pulse (:1672): gated by the run's colour-flash toggle.</summary>
        private static void Pulse(Color color, double strength)
        {
            if (_state?.Config.ColorFlashesEnabled == true) PulseFx?.Invoke(color, strength);
        }

        /// <summary>WPF Shake (:1676): gated and scaled by the run's shake settings.</summary>
        private static void Shake(double baseIntensity, int durMs)
        {
            var cfg = _state?.Config;
            if (cfg?.ScreenShakeEnabled == true) ShakeFx?.Invoke(Math.Clamp(baseIntensity * cfg.ShakeIntensity, 0, 1), durMs);
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
            var end = ChaosRunEngine.CheckEnd(_state, elapsed);
            if (end == ChaosRunEngine.EndCheck.RunOver) { EndRun(); return; }
            if (end == ChaosRunEngine.EndCheck.Relapse) _state.PushEvent("☠ relapse. one more loop — everything drips double");
            var (newWave, _) = ChaosRunEngine.WaveAt(_state, elapsed);
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
            if (!_active && _hud == null && _overlay == null) return;
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
            _runTimer?.Stop();
            EndField();

            var (baseXp, skillMult, finalXp) = ChaosPayout.Xp(_state.RunDurationSec,
                _state.Config.DifficultyMult, _state.Score, _state.SkillMult);
            if (!CoreProgression.AddXP(baseXp, "Chaos")) Log.Debug("Chaos payout AddXP failed");
            long previousBest = ChaosMeta.State.BestScore;   // before the award moves it
            int sparksEarned = 0;
            try { sparksEarned = ChaosRunEffects.AwardRunRewards(_state); }
            catch (Exception ex) { Log.Debug("Chaos meta award: {E}", ex.Message); }
            try { RevealService.Sync("run_end"); } catch (Exception ex) { Log.Debug("Chaos reveal sync: {E}", ex.Message); }
            var rankUp = ChaosPayout.RankUp(ChaosMeta.State.RunsCompleted, ChaosMeta.State.LastRankSeen);
            // ponytail: NotifyChaosRunCompleted((int)finalXp, difficulty) bark (WPF :3269) -> chaos-payout-barks.

            try { _hud?.Close(); } catch (Exception ex) { Log.Debug("Chaos HUD close: {E}", ex.Message); }
            _hud = null;
            ChaosEffectBannerOverlay.CloseActive();   // WPF EndRun :3233
            _overlay?.ShowResults(_state, baseXp, skillMult, finalXp, previousBest, sparksEarned, rankUp);
            Log.Information("Chaos run complete: base {Base:0} x skill {Mult:0.0} = {Final:0} XP, {Sparks} sparks",
                baseXp, skillMult, finalXp, sparksEarned);
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

        private static void CleanupAfterRun()
        {
            ChaosEffectBannerOverlay.CloseActive();   // WPF ForceShutdown :3180 / CleanupAfterRun :3320
            _runTimer = null;
            _hud = null;
            _overlay = null;
            _state = null;
            _active = false;
            _spawning = false;
            _manualPaused = false;
        }
    }
}
