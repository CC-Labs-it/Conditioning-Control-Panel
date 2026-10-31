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
using ConditioningControlPanel.Localization;

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
    /// (Echo/Chaperone/Bound/Tease) - SpawnTick spawns treats and plain live variants; every
    /// motion but FloatUp, treat rot, chain reaction, the Body Buzz shockwave, cam-girl tips, combo
    /// milestones, lessons/first-times, the announcer and the achievement credit; slow-mo,
    /// the perf backoff on the cadence; the payload firer is a logging no-op; the
    /// pop text is the <see cref="PopText"/> hook (chaos-pop-text lane). The wave draft and lesson handoff are wired;
    /// chaos barks (run started/completed, gold-first, defuse/detonate) still need their callers
    /// wired to CoreBark (chaos-payout-barks). NO FOCUS TO DEFUSE, last breath and UNLEASHED
    /// announcer callers remain missing even though the announcer overlay is now present.
    /// The FX window drives heat tint; colour pulse and screen shake calls retain WPF's values
    /// through the unseeded <see cref="PulseFx"/>/<see cref="ShakeFx"/> hooks
    /// (chaos-payout-fallin-d). Freeze pickup behavior is in the additive freeze lane.
    /// The hold and the Snap Chain window run on wall time (<c>BubbleOverlay.Field.Clock</c>),
    /// as WPF does (:4531, ChaosModeService :2000).</para>
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
        /// <summary>Optional observer for WPF <c>_fx.Pulse(color, strength)</c>; the window renders directly.</summary>
        internal static Action<Color, double>? PulseFx;
        /// <summary>WPF <c>App.ScreenShake.Shake(intensity, ms)</c>; unseeded until it lands.</summary>
        internal static Action<double, int>? ShakeFx;
        private static ChaosFxWindow? _fx;
        private static double _lastHeatTint = -1;
        private static bool _active, _spawning, _manualPaused, _endingSoonFired, _lessonCardPaused;
        private static bool _draftPaused, _lessonCardsAfterDraft, _finalLoopAnnounced;
        private static int _pendingWave;
        private static readonly List<ChaosUnlockCardInfo> PendingLessonCards = new();

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
            // ponytail: NotifyChaosRunStarted bark (WPF :389) -> chaos-payout-barks.
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
            if (!_spawning || _state == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            if (_overlay is not { IsVisible: true }) return;   // P01
            var cfg = _state.Config;
            double intensity = _state.RunIntensity;
            double effIntensity = ChaosFieldMath.EffectiveIntensity(intensity, cfg.DifficultyMult);
            if (BubbleOverlay.ChaosCount < ChaosFieldMath.MaxConcurrent(intensity, cfg.DifficultyMult))
            {
                // Freeze pickup spawning is reviewed in the additive freeze lane.
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
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
            EffectLabel(b);   // WPF Bubble.Pop: ShowChaosEffectLabel before onBenignPop (:4685)
            if (spec.IsPrism)
            {
                FireScaledPayload(spec.Payload);
                double prismPts = ChaosFieldMath.ApplyPrismPop(_state, spec);
                ShowPopScore(prismPts);
                ChaosLessons.Tick("taking_chances");   // WPF ChaosLessonHooks.OnPrismPopped
                _state.PushEvent($"🔮 prism! 10x · {spec.Payload.DisplayName} fires");
                return;
            }
            spec.Payload.Fire();   // benign pop = a treat
            if (spec.VariantId == "subliminal") ChaosLessons.Tick("intrusive_thoughts");   // WPF OnTreatPopped
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
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
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
            if (_state == null || spec == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
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
            if (_state?.Config.ColorFlashesEnabled != true) return;
            _fx?.Pulse(color, strength); // WPF ChaosModeService.cs:1672; ChaosFxWindow.cs:151
            PulseFx?.Invoke(color, strength); // optional observer; rendering does not depend on it
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
            if (!_spawning || _state == null || _manualPaused || _lessonCardPaused || _draftPaused) return;
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
                BeginWaveTransition(newWave);
            }
        }

        // WPF ChaosModeService.cs:1442-1491: the real wave-boundary draft path.
        private static void BeginWaveTransition(int newWave)
        {
            if (_state == null) return;
            ChaosRunLessons.OnLoopCompleted();
            AwardLoopTip();
            _state.AllLiveNextWave = false;
            if (!_state.Config.BoonDraftEnabled)
            {
                _state.WaveIndex = newWave;
                _state.ActIndex = ChaosRunEngine.ActFor(newWave);
                AnnounceFinalLoopIfEntering();
                return;
            }
            _draftPaused = true;
            _spawnTimer?.Stop();
            ChaosWaveTimerWindow.Clear();
            BubbleOverlay.Field.Bubbles.RemoveAll(b => b.Spec != null);
            // WPF ChaosModeService.cs:1465-1467; Pulse retains the colour-flash gate (:1672).
            ChaosOverlayWindow.ChaosSfx.PlayWaveClear();
            Pulse(Color.FromRgb(150, 200, 255), 0.30);
            _pendingWave = newWave;
            ShowDraft();
        }

        private static List<ChaosBoon> DraftOptions()
        {
            var state = _state!;
            var taken = new HashSet<string>(state.ActiveBoons.Concat(state.ActiveCurses).Select(b => b.Id));
            var options = ChaosBoonPool.Draft(state.Config.AllowCurses, state.Config.DraftChoices,
                guaranteeCurse: state.MaxedBoons.Contains("surrender"), takenIds: taken, sinChance: state.Config.SinChance);
            foreach (var option in options) ChaosMeta.MarkDiscovered("boon:" + option.Id);
            return options;
        }

        private static void ShowDraft() => _overlay?.ShowBoonDraft(_state!.WaveIndex, DraftOptions(),
            OnBoonChosen, _state.Config.DraftAutoResumeSec, _state.RerollsLeft, RerollDraft);

        // WPF ChaosModeService.cs:1516-1526.
        private static (List<ChaosBoon> options, int rerollsLeft)? RerollDraft()
        {
            if (!_draftPaused || _state == null || _state.RerollsLeft <= 0) return null;
            _state.RerollsLeft--;
            return (DraftOptions(), _state.RerollsLeft);
        }

        // WPF ChaosModeService.cs:1529-1611; the overlay owns click/timeout selection.
        private static void OnBoonChosen(ChaosBoon? boon)
        {
            if (!_active || !_draftPaused || _state == null) return;
            if (boon == null)
            {
                _state.Shields++;
                Pulse(Color.FromRgb(120, 220, 160), 0.22); // WPF :1581
                ChaosAnnouncerOverlay.Announce(Loc.Get("Chaos_DraftResistance"),
                    ChaosAnnounceKind.Willpower, artKey: "resistance"); // WPF :1583
            }
            else
            {
                bool shielded = boon.IsCurse && _state.MaxedBoons.Contains("surrender") && !_state.SurrenderShieldUsed;
                if (shielded) _state.SurrenderShieldUsed = true;
                ChaosFirstTimes.TryAward(ChaosFirstTimes.Whisper);
                if (boon.IsCurse) ChaosFirstTimes.TryAward(ChaosFirstTimes.Yes);
                ChaosLessons.Tick("draft4");
                if (boon.IsCurse) ChaosLessons.Tick("surrender");
                _state.ApplyBoon(boon, shieldDrawback: shielded);
                if (boon.Id == "extra_shield") Pulse(Color.FromRgb(120, 220, 160), 0.22); // WPF :1548
                if (boon.IsCurse)
                {
                    if (_state.SinExtraMult > 0) _state.BoonMult += _state.SinExtraMult;
                    if (_state.MaxedBoons.Contains("surrender"))
                    {
                        _state.Shields++;
                        Pulse(Color.FromRgb(120, 220, 160), 0.22); // WPF :1562
                    }
                    ChaosOverlayWindow.ChaosSfx.Play("sin_accept", 0.6f); // WPF :1565
                }
                // WPF ChaosModeService.cs:1566,1574: the accepted card names its kind and art.
                ChaosAnnouncerOverlay.Announce(boon.Name,
                    boon.IsCurse ? ChaosAnnounceKind.Temptation : ChaosAnnounceKind.Mantra, artKey: boon.Id);
            }
            _state.WaveIndex = _pendingWave;
            _state.ActIndex = ChaosRunEngine.ActFor(_pendingWave);
            _overlay?.ShowReadyGo(ResumeAfterDraft);
        }

        // WPF ChaosModeService.cs:1615-1640: no spawns or clock between GO and the last card.
        private static void ResumeAfterDraft()
        {
            if (!_active || !_spawning || !_draftPaused) return;
            _draftPaused = false;
            if (PendingLessonCards.Count > 0)
            {
                _lessonCardPaused = true;
                _lessonCardsAfterDraft = true;
                BubbleOverlay.HoldChaos(true);
                foreach (var card in PendingLessonCards)
                    ChaosUnlockCardOverlay.Show(card, ResumeAfterLessonCard, autoDismiss: false);
                PendingLessonCards.Clear();
                return;
            }
            _spawnTimer?.Start();
            AfterDraft();
        }

        // WPF ChaosModeService.cs:1649-1669: deferred GO work runs once, after the last card.
        private static void AfterDraft()
        {
            if (_state?.WelcomeShowerEnabled == true)
                for (int i = 0; i < 6; i++)
                    BubbleOverlay.SpawnChaos(ChaosBubbleVariants.Build(ChaosBubbleVariants.All[Rng.Next(2)],
                        _state.RunIntensity, _state.FuseTimeMult, ChaosMotion.RainDown,
                        _state.Config.EffectIntensity, _state.BubbleScale));
            AnnounceFinalLoopIfEntering();
        }

        private static void AnnounceFinalLoopIfEntering()
        {
            if (_state == null || _finalLoopAnnounced || _state.WaveCount <= 1 || _state.WaveIndex < _state.WaveCount) return;
            _finalLoopAnnounced = true;
            ChaosAnnouncerOverlay.Announce(Loc.Get("Chaos_LastLoop"), ChaosAnnounceKind.Depth,
                artKey: "final_loop", subText: Loc.Get("Chaos_LastLoopSub"));
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

        /// <summary>WPF ChaosModeService.cs:146-184: draft/ReadyGo owns the field
        /// until GO, then queued lesson cards take its pause.</summary>
        private static void OnLessonCompleted(string id)
        {
            var run = _state;
            Dispatcher.UIThread.Post(() =>
            {
                if (!_active || run == null || !ReferenceEquals(run, _state)) return;
                var card = ChaosUnlockCardRules.ForLesson(id);
                if (card == null) return;
                _state.PushEvent($"lesson learned — {card.Title}");
                if (_spawning && _draftPaused && !_manualPaused && !_lessonCardPaused)
                {
                    PendingLessonCards.Add(card);
                    return;
                }
                bool claimPause = _spawning && !_manualPaused && !_lessonCardPaused;
                if (claimPause)
                {
                    _lessonCardPaused = true;
                    _spawnTimer?.Stop();
                    BubbleOverlay.HoldChaos(true);
                }
                ChaosUnlockCardOverlay.Show(card, ResumeAfterLessonCard, autoDismiss: !_lessonCardPaused);
            });
        }

        private static void ResumeAfterLessonCard()
        {
            if (!_lessonCardPaused || ChaosUnlockCardOverlay.IsShowing) return;
            _lessonCardPaused = false;
            BubbleOverlay.HoldChaos(_manualPaused);
            if (_spawning && !_manualPaused) _spawnTimer?.Start();
            if (_lessonCardsAfterDraft)
            {
                _lessonCardsAfterDraft = false;
                AfterDraft();
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
        /// freeze, DVD, snap, rabbits, e-stim) and the first_play Spark award are not wired yet.</summary>
        private static void UseToy(ChaosToyState toy)
        {
            if (_state == null) return;
            var r = ChaosToyRules.Use(_state, toy, _spawning && !_manualPaused && !_lessonCardPaused && !_draftPaused);
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
            ChaosUnlockCardOverlay.CloseActive();
            _lessonCardPaused = _draftPaused = _lessonCardsAfterDraft = _finalLoopAnnounced = false;
            PendingLessonCards.Clear();
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
