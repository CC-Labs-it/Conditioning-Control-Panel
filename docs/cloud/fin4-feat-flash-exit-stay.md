# fin4-feat-flash-exit-stay

- Base verified: `35b3144821459a729320205cd2cb2bb11d7f2f21`. Initial snapshot differed; fetched and reset before creating the task branch.
- Implement WPF Glitch red/cyan modulation beneath seven strips: opposing offsets, split threshold and half-alpha. Cache two tinted frames for the short exit and dispose on close; no animated Effect.
- Keep black backing behind no-glow images and non-glow shatter pieces (FlashLayer.Draw/DrawShards).
- Add real disk-image hydra integration tests for normal 0/100 ms children and CloseAll dropping queued spawns. Return the existing load task so the tests can await the real operation; production keeps the existing fire-and-forget caller.
- Route lucky bubble rolls to the existing reused toast, with active mod label and 20x multiplier. Notify at roll time even with a dry daily bucket; repeated pops cannot notify twice.
- Focused visual regressions failed against the original renderer: 2 failed, 0 passed. Add deterministic dry-bucket lucky-roll and head notification-routing coverage. Correct the shatter assertion to require the WPF black backing explicitly.
- No trunk code imported. Read-only source inspected: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing base flash imports retained.
- No new surfaces: ghosts/backing belong to the existing PanicSurfaces `flash` lifetime; bubble notifications reuse the registered lucky-toast surface.

## WPF gap inventory

Reference pinned to the base SHA above. Live callers: FlashService.SpawnFlashWindow/OnFlashClicked, AvatarTubeWindow.Reactions.OnFlashClicked and BubbleService.AwardAmbientPop. No P122 dead-code claim applies.

Implemented: DrawGlitch red/cyan ghosts, Draw/DrawShards black backing, RollLuckyBubble notification.

Missing (ledger remains `stub`):

- Natasha: active-user/CanBook/clickable gates, exclusions for hydra/remix/preview/bubble-origin, red halo/wash/blink, four-second draining ring, boosted lifetime, click/fling dodge and delayed booking. Core has NatashasFavourite/ChasterService, but this head lacks input-idle tracking and flash drag/fling. Decoration alone would misrepresent the booking behavior.
- FlashClicked: WPF reacts one in three clicks, selects enabled/removed/custom phrases, resolves phrase audio overrides and mod event audio, and queues a preset giggle. Head preset speech/filtering exists, but the complete category/audio resolver is absent. WPF CompanionPhraseService alone is 656 lines before adaptation/tests.
- EmiDesk firstFlashEver: fires after the first tracker increment. Desk window exists, but no Fire moment/director host (also documented by intake and first-run callers). The complete moment director is another feature.
- FlashVoicePool/RemoteFlashSource: trunk helpers total 525 lines before integration/tests. WPF needs filtered voice pools, mute/volume/duck/unduck, caption/preannounce, sound-derived lifetime, remote selection generations/cache/prefetch/fallback. Unused helper imports would not close a gap. These services plus companion/desk/idle/input hosts exceed the 1000-added-line unit cap.
- Passive Flash XP: ProgressionBank still lacks input-idle credit.
- Native multi-monitor/Windows proof remains. P13 deduplication of frozen WPF FlashService/PerformanceProfile/SkillTreeService remains tracked debt.

## Verification limits

- SDK: .NET 10.0.100. The channel endpoint returned HTTP 403; explicit-version installation succeeded.
- `dotnet build -c Release`: Core, Avalonia and all eleven Linux test projects referenced by `.github/workflows/build.yml`: exit 0, zero errors (existing warnings remain). Projects: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler and Snapshot test projects.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2851 passed, 5 existing Vosk skips, 0 failed. Vosk model unavailable; no tests were changed to skip.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter` selecting FlashWireTests, FlashPopTests, FlashLuckyTests, FlashShatterWiringTests and FlashDriftTests: exit 0; 38 passed, 0 failed, 0 skipped. Headless font creation works in this checkout/runtime.
- Original-renderer regression run: exit 1, both new visual tests failed (no black card/no ghost layers). After the fix, all focused classes above pass.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. First read-only sandbox invocation could not create temporary files; rerunning with filesystem permission passed.
- `git diff --check`: exit 0.
- The base has neither `docs/avalonia-test-plan.md` nor `test-plan-coverage.sh`; added numbered tester coverage. Automatic review rejected an overwrite-style plan write; the append-only retry preserved any existing content.
- Windows overlay code was compiled in the Avalonia head but not run. No real desktop, multi-monitor/native checks, full Avalonia suite or Vosk recognition run. WPF app and test project cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 564 existing warnings, 0 errors. WPF tests were compiled but not run on Linux.

## Assumptions

- Preserve base architecture and gates; finish independently reachable visuals/notifications first, retain `stub` for the remaining hosts exceeding the cap.
- Use the WPF compositor as visual reference. Cache tinted frames only for a clicked Glitch exit.
