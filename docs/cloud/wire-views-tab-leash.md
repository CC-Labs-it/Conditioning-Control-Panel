# Leash own-card and page repaint slice

- Social > Leash now resolves the current service on each show, repaints live own-leash snapshots, stops listening while hidden, and switches back to the existing empty state after cutting.
- Added the own card's holder/day, immediate unpriced cut through the existing shell/task/safety route, pending tasks/pardons, intensity and DND controls, video cap, and video-assignment Watch using the existing `leash-task` panic surface. On-screen assignment receipts go through Core `NoteShown`.
- Adapted WPF's cap slider: blue/yellow/red bands, 1..90 minute range, pointer release commits, arrows by one minute and PageUp/PageDown by five. Only a left-button press changes it. The pointer regression demonstrated an unwanted right-click changing 30 to 90 before the guard.
- Moved the pending gate-task count into Core `LeashUiRules.PendingGateCount`; WPF and Avalonia both call it, excluding Chaster bookings from the task count.
- Only ledger row `views-tab-leash` changed. It remains **stub**: the full drawer/explainer/holder work exceeds this lane's 1000-added-line cap.
- Imports: none. WPF reference files on `avalonia-port/goongame-core-c` supplied the behavior. Read-only `parity/trunk` was inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files were copied or overwritten.

Named follow-up **wire-views-tab-leash-b**: offer rows, consent/ask/read-clock and answer errors; held cards, actions, sheets and receipts; the page/self-card “?” explainer; sticker shelf and holder preview; full own-card layout, avatar/chain artwork and LeashFx; panic-key hold-to-cut wiring before its hint is shown.

## Verification limits

Commands used SDK 10.0.402, default Debug configuration, `DiffEngine_Disabled=true`, and the test suites' sandbox profiles. Builds used `DOTNET_PROCESSOR_COUNT=2`, `-m:1 -p:UseSharedCompilation=false`, and SDK hardlink copy flags to control resource use.

- `dotnet build CCP.Core`, `dotnet build CCP.Avalonia`, and all 11 Linux test projects named in `.github/workflows/build.yml`: all 13 builds succeeded, 0 errors. Test projects: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, Avalonia.Consent.Tests, Avalonia.Language.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Scheduler.Tests, Avalonia.Report.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: succeeded, 509 warnings, 0 errors. Windows app/tests were compiled, **not run** on Linux. Existing build-only suites were compiled rather than executed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~LeashPageTests`: final result **5 passed, 0 failed, 0 skipped**. The live-snapshot empty-state test and slider right-click test each demonstrated their expected failure before the fixes. The new harness uses the real app's font configuration for headless rendering.
- `dotnet test Tests/CCP.Core.Tests` with the fetched Vosk model: final result **2816 passed, 0 failed, 1 skipped, 2817 total**. The existing skip is `BoardTilesTests.A_frame_fits_the_budget`. An earlier full run had an unrelated `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` failure and missing-model speech skips; the final full run above passed without changing those tests.
- `dotnet test Tests/CCP.Core.Tests --no-build --filter FullyQualifiedName~Leash`: **127 passed, 0 failed, 0 skipped**, including `LeashSelfCardRulesTests.ChasterBookingDoesNotBecomeAWaitingTask`.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed** after the final input guard.
- `CCP_USERDATA_DIR=/tmp/ccp-worker-2-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/ccp-worker-2-render`: **226/226 views rendered**. `LeashSelfCard.png` shows the new own-card controls and was visually inspected; `LeashCapSlider.png` shows the slider; `LeashTabView.png` shows the default empty snapshot. These are synthetic headless samples, not real account screenshots. The later mouse guard changes input handling only.

Real server operations, actual assignment video playback, Windows desktop/panic-hook behavior, and the named follow-up surfaces were not run here. No real user profile or secrets were used by the tests. No PR was opened.
