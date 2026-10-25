# fin5-shell-login

- Base verified at `3aa8c24e6ec9c3c268fd527c6c99bfa88d1d342b`; task branch `avalonia-port/cloud-fin5-shell-login`.
- Adapted WPF's silent cloud achievement union into the existing Core achievement engine; wired both profile GET and sync responses. Unknown server ids and patron unlocks survive; restoration never emits an unlock event.
- Added WPF's 500 ms post-login token settle and popup suppression with finally cleanup. Both full-user and older bare device-code login paths use the continuation.
- Connected existing Core quest ownership, deferred generation, and logout stamping. Plain logout keeps local active quest progress; deletion still wipes it.
- Drop a late profile GET if its account departed while the request was in flight.
- Added focused Core/head tests and numbered tester steps. No new surface, so no new panic registration is required; the existing login surface remains registered.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Behaviour was adapted from the pinned base's frozen WPF reference.

## Audit and remaining scope

At the pinned base, the row's explicit Missing list was confirmed against `MainWindow.Login.cs`, `Services/Settings/ProfileSyncService.cs`, and the Avalonia account/login paths:

- Achievement restore from sync and cloud load: implemented (WPF ProfileSyncService:2103-2124 and :2878-2890). Direct progress union matches WPF, rather than TryUnlock's catalogue gate.
- Token settle and suppression: implemented (MainWindow.Login:163-204).
- Quest account ownership, generation after cloud restoration, account-switch wipe without early generation, and logout preservation: implemented using the already ported QuestService.
- Lifetime counters, login-streak/date reconciliation and deferred break settlement: still missing from cloud sync. Local trackers exist; the missing piece is their cloud merge and read-before-write payload.
- Quest totals, completion dates/log union, daily/weekly streaks and reset/force-streak acknowledgements: still missing from cloud reconciliation. Mobile totals must remain separate from desktop counters to avoid double counting (WPF ProfileSyncService:2154 onward).
- Feature day-log payload and cloud lifetime rebaseline beyond conditioning minutes: still missing.
- Full account cache cleanup: still missing parity for the WPF Descent reset, prize-counter invalidation and explicit profile viewer clearing (MainWindow.Login:314-345).
- WPF attempts a sync even after failed load; this head retains its established read-before-write guard. No speculative write of unread counters was added.

The row stays `stub`; this unit does not claim complete WPF parity. Remaining cloud reconciliation requires cohesive schema/adoption/ack/payload tests; it is recorded explicitly rather than marking the row wired after completing only the visible login lifecycle.

## Assumptions

- Keep existing V2 read-before-write safety on failed GET rather than sending defaults.
- Calling the existing quest generation check after every completed sign-in is safe and idempotent; it also covers a stamped different account discovered only during load.
- No changes to the frozen WPF reference, new platform seam, or new UI are needed.

## Verification limits

- SDK: .NET 10.0.100. Channel installer encountered HTTP 403 for its latest-version probe; the pinned SDK installer succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors (existing warnings).
- All Linux test projects named in `.github/workflows/build.yml` built with `-c Release`: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot. All reached zero errors. The first Avalonia test build exposed a duplicate fixture member name; renamed and rebuilt successfully.
- Focused Core tests (`CloudRestoreUnionsUnknown` and `ShellLoginSyncTests`): exit 0, 6 passed. Negative control with restoration disabled: exit 1, the new restoration test failed its persisted-unlock assertion; restored implementation then passed. No negative-control code remains.
- Window-free head lifecycle tests (`LoginSettlesThenRestores` and `PlainLogoutStamps`): exit 0, 4 passed. These cover 499/500 ms request ordering, suppression during HTTP and release afterwards (success and failure), quest ownership, logout preservation and a late response after an account switch.
- Relevant head classes (`AccountSeedTests`, `LoginAchievementOfferTests`, `LoginDeviceCodeTests`, `--no-build`): exit 1, 44 passed / 19 failed. The failures are glyphTypeface/font creation in existing shell controls (Inter and Segoe UI Black). No font workaround or test weakening was added. UI tests need the supervisor's font-capable runner.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: unavailable on this base (exit 127); numbered tester steps were added nonetheless.
- Initial full Core suite: exit 1, 2823 passed / 1 failed / 5 skipped. The single failure was the board frame timing budget (19.38 ms under concurrent builds). A rerun without compilation load is recorded below. The five Vosk model cases were skipped by the existing suite; no model download was attempted because this environment documents its 403 limitation.
- Windows runtime behaviour and real OAuth/provider consent were not run. The existing shell rendering/font failures also prevent meaningful render-all evidence here.

- WPF test cross-build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 512 warnings / zero errors. Both WPF app and test assembly compiled; neither was executed on Linux.
- Wire-contract regression: WPF V2SyncUser places achievements under `user.achievements`. The test with that real shape first failed (only the load id restored); fixing the reader made the focused Core set pass 6/6 again. This evidence supersedes the first top-level-field fixture.
- Full Core rerun after the build matrix: exit 1, 2823 passed / 1 failed / 5 skipped. The board timing test passed, but `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` failed because ScheduleAt returned null. No suite tests were weakened, skipped, or deleted. Isolated checks of these timing failures are recorded below.

- Isolated unchanged timing checks: `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget|FullyQualifiedName~GoonClockAndRoundsTests'`: exit 0, 13 passed. This does not turn either failing full-suite run into a pass.
- Head regression controls: removing the stale-GET guard and logout quest-owner stamp produced exactly the two expected assertion failures (2 failed / 2 passed). Both fixes were restored before the final head build/test run.
- Final restored head run: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~LoginSettlesThenRestores|FullyQualifiedName~PlainLogoutStamps'`: exit 0, 4 passed; Core, Avalonia and test assembly rebuilt with zero errors. No temporary regression-control edits remain.

- Final full Core run with compilation finished: `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, exit 0. Tail: `Passed!  - Failed:     0, Passed:  2824, Skipped:     5, Total:  2829, Duration: 1 m 5 s - CCP.Core.Tests.dll (net10.0)`.
