# fin4-shell-quests-tab

- Add the missing completed-session producer: natural completion credits session
  quests once; early stop and repeated stop do not. The existing quest events
  repaint the board.
- Seed Perfect Bimbo Week using Core SkillTreeRules adapted from the frozen WPF
  SkillTreeService: 7-day multiples, special 14/30 rewards, level scaling,
  persisted paid-once latch, break reset, grant before latch. Reuse the existing
  AchievementPopup with WPF localized copy/art and register this notification
  under `perfect-week-bonus` in PanicSurfaces.
- Restore reroll companion actions through the existing CoreBark seam.
- Keep `shell-quests-tab` at `stub`, with one row and explicit remaining gaps.
  Add numbered manual tester steps and focused Core/headless coverage.
- Source inspection: expected base/WPF SHA
  `e3593f618bd7ccb608761cdd18c580bf44e22a03`. Fetched read-only `parity/trunk`
  at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap audit and remaining work

The base already renders all three daily seats, weekly including the retired
completed-definition fallback, quest art resolution/cache/mod invalidation,
season title, scaled XP/bonuses, reroll pools/refusals, desktop plus mobile totals,
month calendar/shield marks, live streak-fix balance and manual fix mode,
confirmation, serialized server spend, authoritative balance, cancellation and
status timing. Those paths are retained. Cache eviction remains GC-owned in both
heads to avoid disposing art still referenced by an Image.

The missing live paths found in the row and its references were:

1. Completed sessions: WPF AchievementService.TrackSessionComplete calls
   TrackSessionCompleted. Implemented through SessionRunner.Stopped.
2. Perfect-week bonus: WPF App seeds CheckPerfectWeekBonus; SkillTreeService pays
   and shows its popup. Implemented with the same milestones and latch ordering.
3. Reroll companion feedback: MainWindow.QuestsTab notifies reroll_daily/weekly
   before attempting either reroll. Implemented through the existing bark seam.
4. Intake punch-card accrual: WPF RefreshPunchCard explicitly collapses the panel
   while IntakePunchCardService.UiEnabled is false, but the 395-line service
   still accrues silently. It needs the intake draft completion producer, session
   threshold watcher, persistence/autosave, claim lifecycle and tests. This is not
   dead WPF code; P122 does not apply. Its dormant paint is also absent here.
5. Brain-drain minutes: WPF AchievementService tracks running overlay elapsed
   time. This head has settings/help only; it needs the BrainDrain runtime,
   capture/overlay/audio and panic lifecycle before counting minutes honestly.
6. Remote commands received/issued: WPF App wires both RemoteControl events;
   issued credit excludes self. The full remote-control host is absent here.
7. Keyword triggers: WPF KeywordTriggerService counts after a trigger fires;
   this head's KeywordTriggerHead.FireDemo is a no-op. The WPF service alone is
   2084 lines and needs the actual watcher/action host, not a synthetic counter.
8. Startup login streak: WPF App calls TrackStreak after AchievementService's
   UpdateDailyStreak. Its 315-line head lifecycle includes shield/insurance,
   pending cloud break decisions and streak reconciliation. Forwarding the stale
   stored count would not reproduce that behavior.
9. Streak-fix unauthorized recovery: WPF ProfileSync.UseOopsieInsuranceAsync
   invokes HandleUnauthorizedAsync, which serializes restore/provider recovery
   with cooldowns and restarts heartbeat. V2AuthService lacks that recovery host;
   clearing the token would differ from WPF's retain-on-failure policy.

The complete dependency hosts plus their behavior tests exceed the 1000-added-line
unit cap. Visible rewards and existing session/bark producers take priority;
there are no placeholder success paths for unported hosts. Quest-completion
flash voice and header stamps belong to their other shell surfaces and also
remain absent as noted by QuestsTabView.Quests.cs.

## Verification limits

Commands used .NET 10 with `DiffEngine_Disabled=true` for tests.

- `dotnet build <project> -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors
  for Core, Avalonia and all 12 workflow test projects: CatalogueStartup, Consent,
  GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia.Tests,
  Core.Content, Core.Settings, Core.Tests and ConditioningControlPanel.Tests.
  Existing warnings remain. The final sweep succeeded after correcting a save
  callback and a missing test extension-method import during development.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `git diff --check`: exit 0. The base has no test-plan-coverage script; numbered
  tester steps were added in its expected format.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter
  FullyQualifiedName~PerfectWeekQuestTests`: exit 0, 8 passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter
  'FullyQualifiedName~QuestRewardWireTests|FullyQualifiedName~QuestsTab'`:
  exit 0, 13 passed, zero skipped. Includes real headless popup/panic coverage.
- Regression proof: removing only the session producer made QuestRewardWireTests
  fail (expected 1, actual 0); restored producer passes. Before bark wiring,
  QuestsTabLiveTests failed because reroll_daily was absent; wired version passes.
- A broader quest plus PanicSurfacesTests run hit this environment's known font
  creation error (Segoe UI Black, then Inter): 13 failed, 5 passed. The focused
  quest run above initializes through the app builder and passes. Full-suite font
  initialization remains outside this unit.
- Two full Core runs during builds: exit 1, 2825 passed, 5 skipped, 1 failed:
  GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch
  (ScheduleAt returned null). Its isolated class rerun: exit 0, 12 passed.
  Final full run with no concurrent builds/tests: exit 1, 2825 passed, 5 skipped,
  1 failed: BoardTilesTests.A_frame_fits_the_budget (17.14 ms frame). The clock
  test passed on this run. The complete Core gate is therefore not green here;
  neither unrelated timing test was changed, skipped or weakened.

Windows runtime, WPF tests and WPF side-by-side were not executed on Linux;
the WPF app/test project was compiled with Windows targeting. Vosk's five tests
self-skip without the model (download unavailable here); none were changed or
manually skipped. No production account, real desktop or user profile was used.

## Assumptions

- Keep WPF frozen; adapt its reward rules into Core without editing the reference.
- Keep the disabled punch-card panel hidden and the row stub until its live
  accrual and the remaining producer/recovery hosts exist.
- Reuse AchievementPopup timing/art and localized WPF copy for the bonus; panic
  explicitly closes the new notification path.
