- Fix the repeated Exclusives tab refresh: `ShowTab` reaches `RefreshVault`, which previously decoded all shelf images, group signs and spotlight art and replaced `ItemsSource` on every visit. Replacing it also discarded realized card controls and templates. WPF's `EnsureExclusivesBuilt` retains cards and `ModResourceResolver` caches decoded art.
- Snapshot the visible-roster and entitlement inputs before loading any art. Unchanged visits retain the rows, realized controls and spotlight bitmap. Live gate, free-day and visibility probes still run on every refresh; tier plates still refresh independently.
- Rebuild when gate/roster, language or active mod changes. A `ModChanged` event explicitly invalidates the snapshot even when the mod ID stays the same. Publish the snapshot only after a successful repaint, and use the sampled gates for both shelf and spotlight.
- Add focused guards for retained controls/art and refreshes after entitlement, daily-free, roster, language and same-ID mod changes. No existing test assertions changed.
- No code imported from trunk. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; the WPF reference inspected is the required base `aa2dcc1f0413794d7e36abfcaa7272288c4fe6fe`.

## Verification limits

Linux, .NET SDK 10.0.402, Release builds; `DiffEngine_Disabled=true` for tests.

| Probe | Before | After |
| --- | ---: | ---: |
| Headless refresh + layout median, ms | 579.459 | 0.918 |
| Headless refresh + layout p95, ms | 596.290 | 1.765 |

Each probe uses 20 hide/show visits of the real Exclusives view in a headless window at 1400 x 900, timing visibility, `RefreshVault` and `UpdateLayout`. Construction/initial layout is outside the sample. The new test uses the production bundled-font configuration and the existing temporary user-data sandbox. These numbers isolate the changed hot path: they do **not** measure the entire `MainShellWindow.ShowTab`, compositor completion, desktop startup or working set. No display/Xvfb was available, so desktop Linux `--perf-bench` was not run. Windows WPF/Avalonia comparison and memory thresholds remain unverified and need the paired Windows job; no claim of passing the Windows limits is made.

Commands/results:

- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ExclusivesRefreshTests --logger 'console;verbosity=normal'` against unchanged production code: exit 1, both tests fail on `Assert.Same` after the redundant refresh; the baseline timing above is printed before that assertion.
- Same filter after the fix, with detailed console logging: exit 0, 2 passed. Retained shelf containers and spotlight bitmap are asserted without a flaky wall-clock threshold.
- `dotnet build <project> -c Release` for `CCP.Core`, `CCP.Avalonia` and every Linux test project named in `.github/workflows/build.yml`: all exit 0, zero errors. Test projects: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Snapshot.Tests`.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (559 existing warnings). WPF app/tests and Windows-specific code compiled, not executed on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. The initial sandboxed invocation could not create scratch files; rerunning with writable scratch space passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~Exclusives|FullyQualifiedName~PerfBenchTests'`: exit 1, 4 passed / 5 failed at font creation (`Could not create glyphTypeface`, Segoe UI Black / Inter), matching the documented cloud limitation. The existing suites initialize the process without bundled fonts; when they initialize first, the new tests inherit that setup too. The existing UI behavior tests could not be validated here and must run in the supervisor's supported font environment. No assertions or shared font setup were changed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: first run exit 1, 3338 passed / 1 failed / 5 skipped. `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` expected Loops but got Clips. Core and its tests have no diff from base. Isolated `--filter FullyQualifiedName~DtrhRemoteMediaTests` rerun: exit 0, all 9 passed.
- Full Core suite repeated after the isolated check: exit 0, **3339 passed / 0 failed / 5 skipped** (3344 total). The first-run remote-media failure did not reproduce.
- The five Vosk tests use their existing missing-model skip; the environment reports model downloads blocked. No tests were newly skipped or weakened.

Assumptions: keep the existing full repaint for changed inputs and optimize ordinary unchanged visits; this is the smallest local correction to the measured repeated-tab path. Art edited in place is refreshed through the existing `ModChanged` notification, including same-ID reloads. The paired Windows job remains the authority for performance parity.
