# wire-shell-subscribestar

- Restore SubscribeStar's missing shell dependents: repaint effective AI-effects permissions and their Lab lock band, Takeover availability (including its free day), and the Programs view after each tier-change event.
- Post background validation events to the UI thread; paint once on shell open; unsubscribe the captured provider on close and discard a refresh queued before close.
- Reuse `AiPermissionsGrid.SyncFromSettings` / `ApplyTierGate`, the existing deferred Programs refresh, and Core `AiEffectControlGate` / `TierGate`. WPF's Takeover availability now delegates to the same Core verdict. Entitlement repaint never clears the saved AI-effects preference.
- Add `SubscribeStarRefreshTests.TierChangesRepaintPermissionsTakeoverAndProgramsWithoutLosingSavedChoice`, covering grant, lapse, free day, Programs card locks, preference preservation and close cleanup. Correct the related account/celebration test setup to register the shipped app fonts; no assertions were removed or loosened.
- Source: WPF base `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`. Fetched read-only parity/trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Verification limits

- The regression failed before the implementation: the denied AI-effects lock band remained hidden on shell open. After the fix, the regression passed (1 passed, 0 skipped).
- Required Release builds of CCP.Core, CCP.Avalonia and all 11 Linux test projects listed in `.github/workflows/build.yml` passed, zero errors. The WPF head and Tests/ConditioningControlPanel.Tests also compiled successfully with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (existing warnings); Windows code was compiled but not run.
- `dotnet test Tests/CCP.Avalonia.Tests --no-build -c Release --filter 'FullyQualifiedName~SubscribeStarRefreshTests|FullyQualifiedName~AccountSettingsSectionTests|FullyQualifiedName~PremiumCelebrationTests'`: 9 passed, 0 failed, 0 skipped.
- First `dotnet test Tests/CCP.Core.Tests --no-build -c Release`: 2,815 passed, 1 failed, 0 skipped. The unchanged `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` received a null scheduled-fire handle. The isolated class retry passed all 12 tests; the final full-suite retry passed all 2,816 tests (0 failures, 0 skips). No test was weakened or skipped.
- `bash .github/scripts/core-guards.sh`: passed, including 424 XAML type references.
- `CCP_USERDATA_DIR=/tmp/ccp-3-substar-render-profile dotnet run --project CCP.Avalonia --no-build -c Release -- --render-all /tmp/ccp-3-substar-render-all`: 224/224 views rendered, exit 0. `MainShellWindow.png` shows the shell initial paint; `AiPermissionsGrid.png`, `BambiTakeoverTabView.png` and `ProgramsTabView.png` show the affected existing surfaces. Dynamic grant/lapse state is proved by the regression test.
- Provider tests use fake events, in-memory secrets and the test assembly's `CCP_USERDATA_DIR` sandbox. No live SubscribeStar login/server integration or Windows runtime test was run.
- Checks use `DOTNET_PROCESSOR_COUNT=2`, `DiffEngine_Disabled=true`, `-m:1 -p:UseSharedCompilation=false` and all four MSBuild hard-link copy properties to keep dependency copies within the shared disk budget.
