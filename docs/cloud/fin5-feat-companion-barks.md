# FIN5 companion barks

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; local snapshot branch name is irrelevant.
- Adapted the pinned WPF BarkService (same base SHA): existing host subscriptions for progression, companion levels, achievements, quests, mantra, lockdown, bubble pops; all detach on Stop.
- Added persisted idle rule rotation with 0.35 gated bias, mute/speaking suppression, normal gates, slider timer cadence and reset on speech. Timer closes with the tube; existing tube-speech panic registration drops queued clips/speech. No new window/surface.
- Added dry-run simulation without persistent changes, current-tier latch keys, and Core whisper/narrator floor seams (attribution exemptions preserved). Audio-floor sources are not yet available on this head.
- Ordinary voiced barks use Giggle rather than priority; delayed lines lose the clip, matching WPF's text-only delayed path.
- Kept one honest `stub` ledger row; replaced repeated historical claims with current evidence and numbered tester steps.
- Fetched `parity/trunk` read-only; no trunk code imported, so there are no imported trunk commit credits.

## WPF gap inventory

Read `Services/Companion/BarkService.cs` at the pinned base: Start/Stop, Notify methods, WireSubscriptions, AttachSessionEngine/focus/tray, setup debounce, field resolver, gate/rotation and Speak; also `AvatarTube/AvatarTubeWindow.Speech.cs` for idle cadence and Giggle.

At entry this head lacked all WireSubscriptions except chat/mod, the earlier bouncing overlay adapters and explicit CoreBark UI/Chaos-summary/panic callers. This unit closes the host subscriptions listed above plus idle selection/cadence, dry-run and ordinary voiced routing. Tier keying is implemented; tier-change source remains missing. Whisper/narrator gate logic is implemented and tested; its head providers remain missing.

Remaining literal WPF Raise triggers with no matching Avalonia Raise caller (mechanical inventory, checked against the pinned source):

`ActivityChanged`, `AppOpened`, `AttentionCheckFail`, `AttentionCheckPass`, `AvatarChanged`, `Blink`, `BlinkTrainerStateChanged`, `BrainDrainTriggered`, `BubbleCountCompleted`, `BubbleCountFailed`, `BubbleMissed`, `ChaosActChanged`, `ChaosBenignPopped`, `ChaosBoonPicked`, `ChaosBoonSkipped`, `ChaosBubbleDefused`, `ChaosBubbleDetonated`, `ChaosBubbleDetonatedAbsorbed`, `ChaosClickDetonate`, `ChaosComboBig`, `ChaosComboMilestone`, `ChaosCursePicked`, `ChaosDarterCaught`, `ChaosDefuseFirst`, `ChaosDefuseNoFocus`, `ChaosDefuseRelease`, `ChaosDollhouseFirstOpen`, `ChaosDuoDemo`, `ChaosEndingSoon`, `ChaosFirstTime`, `ChaosFocusLow`, `ChaosFreezeCaught`, `ChaosGiftGiven`, `ChaosGoldFirst`, `ChaosLessonComplete`, `ChaosRevealFlash`, `ChaosRunCompleted`, `ChaosRunStarted`, `ChaosTeaseClicked`, `ChaosTeaseDebut`, `ChaosTeaseDenied`, `ChaosTeaseDeniedStreak`, `ChaosWaveCleared`, `ChaosWaveEscalated`, `ControllerConnectedChanged`, `DescentDayBanked`, `DescentFirstSpiralOpen`, `DescentLapseReturn`, `DescentNearBank`, `DescentStageCrossed`, `DiscordAuthChanged`, `EnhancementApplied`, `FaceFound`, `FaceLost`, `FlashDisplayed`, `GazeActiveChanged`, `GazePopped`, `IdleStateChanged`, `KeywordTriggerFired`, `LeaderboardViewed`, `LockCardCompleted`, `LongStare`, `LuckyProc`, `MindWipeTriggered`, `MouthOpen`, `PatreonTierChanged`, `PersistentMemoryRecalled`, `PinkRushEnded`, `PinkRushStarted`, `QuizCompleted`, `RemoteCommandReceived`, `RoadmapBadgeEarned`, `RoadmapStepCompleted`, `RoadmapTrackUnlocked`, `SessionCompleted`, `SessionPaused`, `SessionPhaseChanged`, `SessionProgress`, `SessionRefocused`, `SessionResumed`, `SessionSetupReady`, `SessionStarted`, `SessionStopped`, `SkillUnlocked`, `StillOnActivity`, `StreakMilestone`, `SubliminalDisplayed`, `TongueOut`, `TrackingStateChanged`, `TutorialCompleted`, `UpdateAvailable`, `VideoAboutToStart`, `VideoEnded`, `VideoStarted`, `WakeBambiRequested`.

Additional gaps: stateful session elapsed/phase/deepener/blink/lost-face/pause/refocus/setup-idle/days-away/relaunch fields; setup-ready debounce and stall; shared awareness arbiter cooldown accounting/guaranteed priority route; lock-card 50/50 fork and context; invite-week-aware Patreon transition bookkeeping; quiz pass/perfect and modal-intake/Arcademy suppression; whisper/narrator live source providers; idle stale-speech watchdog. Core idle bias/rotation and priority gates alone do not make these callers reachable.

These are live WPF paths, not P122 dead-code candidates. A complete webcam/gaze, session-state, awareness arbiter and missing-host port with behavior tests exceeds this unit's 1000-added-line cap. Existing roadmap/video/visual hosts still need their specific adapters and tests; this is a partial implementation, not a dependency-only NOOP.

## Assumptions

- Retain stub until all WPF paths are reachable; no claim of wired or verified parity.
- Read tier from the Patreon provider, matching WPF LatchKey; no new entitlement policy.
- Reuse ProgressionBank.Awarded to supply current post-level PlayerXP as WPF XPChanged does. Claimed-XP paths outside that host remain outside this adapter.
- Keep the existing tube speech panic registration for the added queue path; stop the idle timer on window close. Panic preserves idle cadence as WPF does, subject to the safety floor.

## Verification limits

- .NET 10 SDK installed with dotnet-install; final SDK version 10.0.402.
- `dotnet build <project> -c Release`: Core, Avalonia, and every Linux-compatible test project named in build.yml were built. CatalogueStartup initially hit MSB4166 during overlapping builds; sequential `-m:1` retry passed with zero errors. Final head build also uses `-m:1`.
- Linux test projects: CCP.Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests, CCP.Core.Tests (each under Tests with its full CCP prefix).
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script does not exist on this base. Numbered tester steps added regardless.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 3963 passed, 5 existing Vosk-model skips, 0 failed. This preceded the two extra bias-boundary cases.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~BarkEngineTests`: final exit 0; 18 passed, 0 skipped/failed, including both bias-boundary cases.
- Headless event-only filter: exit 0; all 14 new event/payload/unsubscribe cases passed.
- Initial window tests hit the known missing-font failure; both relevant fixtures now use the repository's existing WithInterFont/AppFonts bootstrap. Explicit tube injection exercises the real routing method despite Live intentionally being null without a desktop lifetime.

- Old ordinary-voiced routing was temporarily restored with the new regression test in place: exit 1, expected “already speaking”, actual “ordinary bark”. The final routing was then restored (red/green proof).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. WPF/Windows code compiled only; it was not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release -m:1 --filter "FullyQualifiedName~CompanionBarkHeadTests|FullyQualifiedName~TubeSpeechQueueTests"`: final exit 0; 24 passed, 0 failed/skipped. Bundled-font setup resolved the environment failure; all new window tests ran successfully. Windows desktop/audio and real account transitions cannot be exercised on this Linux worker. All headless tests use the repository's isolated userdata fixture.
