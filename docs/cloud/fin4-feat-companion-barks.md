# fin4-feat-companion-barks

- Added detachable subscriptions over existing player/companion progression, achievement, quest, mantra, lockdown and bubble hosts. Existing user-message/mod-change order is retained.
- Connected shown flash/subliminal producers directly to the existing engine.
- Added WPF idle dispatch: conditions, mute/speaking gate, persisted pool-wide no-repeat, last-rule reseeding and 0.35 gated-rule bias. The tube owns cadence, follows the slider, and stops its timer on close.
- Added dry-run: opt-in environment flag, in-memory cooldown/rotation/one-shot decisions, no rendering or persisted latch/rotation writes.
- Consolidated the repeated ledger evidence into one honest stub row and numbered manual steps. No new surface; the existing tube-speech panic registration remains the owner.
- No trunk code imported. Read-only reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapted from WPF at base `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`.

## WPF gap inventory

Compared the ledger with the complete pinned WPF BarkService, including WireSubscriptions, AttachSessionEngine, AttachTray, setup debounce, live-field resolution, gate, latch and speech paths. Initial missing behavior: the newly connected producers above, idle dispatch/cadence/rotation, dry-run, and all remaining items below. The historical ledger's claims that idle/dry-run were already ported did not match the base code.

Remaining producer keys (literal WPF Raise calls absent from this head; external doorbells such as UiAction retain their existing adapters):

`ActivityChanged`, `AppOpened`, `AttentionCheckFail`, `AttentionCheckPass`, `AvatarChanged`, `Blink`, `BlinkTrainerStateChanged`, `BrainDrainTriggered`, `BubbleCountCompleted`, `BubbleCountFailed`, `BubbleMissed`, `ChaosActChanged`, `ChaosBenignPopped`, `ChaosBoonPicked`, `ChaosBoonSkipped`, `ChaosBubbleDefused`, `ChaosBubbleDetonated`, `ChaosBubbleDetonatedAbsorbed`, `ChaosClickDetonate`, `ChaosComboBig`, `ChaosComboMilestone`, `ChaosCursePicked`, `ChaosDarterCaught`, `ChaosDefuseFirst`, `ChaosDefuseNoFocus`, `ChaosDefuseRelease`, `ChaosDollhouseFirstOpen`, `ChaosDuoDemo`, `ChaosEndingSoon`, `ChaosFirstTime`, `ChaosFocusLow`, `ChaosFreezeCaught`, `ChaosGiftGiven`, `ChaosGoldFirst`, `ChaosLessonComplete`, `ChaosRevealFlash`, `ChaosRunCompleted`, `ChaosRunStarted`, `ChaosTeaseClicked`, `ChaosTeaseDebut`, `ChaosTeaseDenied`, `ChaosTeaseDeniedStreak`, `ChaosWaveCleared`, `ChaosWaveEscalated`, `ControllerConnectedChanged`, `DescentDayBanked`, `DescentFirstSpiralOpen`, `DescentLapseReturn`, `DescentNearBank`, `DescentStageCrossed`, `DiscordAuthChanged`, `EnhancementApplied`, `FaceFound`, `FaceLost`, `GazeActiveChanged`, `GazePopped`, `IdleStateChanged`, `KeywordTriggerFired`, `LeaderboardViewed`, `LockCardCompleted`, `LongStare`, `LuckyProc`, `MindWipeTriggered`, `MouthOpen`, `PatreonTierChanged`, `PersistentMemoryRecalled`, `PinkRushEnded`, `PinkRushStarted`, `QuizCompleted`, `RemoteCommandReceived`, `RoadmapBadgeEarned`, `RoadmapStepCompleted`, `RoadmapTrackUnlocked`, `SessionCompleted`, `SessionPaused`, `SessionPhaseChanged`, `SessionProgress`, `SessionRefocused`, `SessionResumed`, `SessionSetupReady`, `SessionStarted`, `SessionStopped`, `SkillUnlocked`, `StillOnActivity`, `StreakMilestone`, `TongueOut`, `TrackingStateChanged`, `TutorialCompleted`, `UpdateAvailable`, `VideoAboutToStart`, `VideoEnded`, `VideoStarted`, `WakeBambiRequested`.

Additional gaps not captured by literal-Raise comparison: IdleStateChanged and persistent-memory/account/update/tutorial routing; lock-card Fork-D 50/50 outcome and audio handling; anticipation debounce (8–12 seconds, then 45-second stall); session phase/time/pause/refocus, blink/face loss, launch recency and setup live-field state; all-skills/webcam live reads; whisper floor with possession/emergency-exit exemptions; Chaos narrator floor; tier-specific latch identity; ordinary voiced Giggle queue routing (the existing head uses GigglePriority). Some CoreBark specialized doorbells also have no consumer on this engine. See the pinned WPF file for context keys, timing and edge conditions.

This is not a pure dependency NOOP: existing hosts provided user-visible work. Full completion also needs substantial BarkState/session/awareness host integration and specialized producers, beyond the unit's 1000-added-line allowance with behavior tests; row remains stub. No P122 exception is claimed: these have live WPF callers.

## Assumptions

- Use existing Core/heads rather than importing a competing bark engine.
- Keep the frozen WPF reference unchanged.
- Start idle cadence when the tube opens, avoiding timers on unshown render-only windows; re-read slider each tick and restart immediately from the slider.
- Keep existing visual/bark gates and ordinary voiced routing unchanged in this partial continuation.

## Verification limits

- Checkout: supplied snapshot HEAD differed; its fetch failed read-only. Used the authorized fallback clone, verified exact base commit and clean status before creating only the requested feature branch.
- .NET 10 SDK installed. `dotnet build -c Release` passed (exit 0, 0 errors) for CCP.Core, CCP.Avalonia and all twelve Linux test projects listed in build.yml: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests.
- WPF test project built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors. Compiled but not run; Windows native behavior was not exercised.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3957 passed, 5 existing Vosk skips, 0 failed. Earlier full run had DTRH cache-order and board-budget failures; final full retry passed. No tests weakened or skipped by this change.
- Core BarkEngineTests focused run: exit 0; 10 passed.
- Avalonia CompanionBarkHeadTests: compiled with 0 errors; executed headlessly, 20 passed and 1 failed (exit 1). Sole failure: existing ALadderPanicPressRaisesThePanicBark cannot create Segoe UI Black glyphTypeface, the documented environment limitation. The 17 newly added cases run separately all passed (exit 0).
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. `git diff --check`: exit 0. Test-plan coverage script is absent on this pinned base; numbered WPF/Rows steps were checked manually.
- Initial build attempts hit disk exhaustion from repeated asset copies and one overlapping-build PDB lock. Cleared only generated outputs and rebuilt sequentially; final required builds all passed. Generated output was cleared between test projects to fit the runtime disk limit.
- No live desktop, render-all, Windows runtime checks or full Avalonia suite run. No Vosk download attempted.
