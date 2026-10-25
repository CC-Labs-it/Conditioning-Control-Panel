# fin5-shell-webcam

- Base: `1fa9cc18d226d18884c72c31b785d898e14087e5`. Independent cloud snapshot; no local edits imported.
- Adapted WPF camera-open watchdog: 90-second bound, CameraInUse on timeout, worker-owned late cleanup, no restart while an abandoned open still holds the driver. Panic invalidates publication. Synthetic blocked-source tests cover timeout, cancellation and ownership transfer without opening hardware.
- Adapted WPF warmup acceptance: 3 seconds, 200 ms polls, spatial standard deviation >= 3 OR inter-frame change >= 2. Flat/static frames no longer win a backend attempt; dim moving feeds can pass. Applied to Linux capture as well as the Windows backend ladder; synthetic Mat tests cover both acceptance paths and rejection.
- Connected MON picker to persisted WebcamCalibrationScreen and all three full-screen webcam entry points. Primary/missing displays resolve against the current topology on each open, never overwriting the saved choice. Extracted the small WPF monitor-selection rule to Core; tests cover unplug/replug, reordering, case-insensitive identity and empty topology.
- Reset diagnostic counters on each user Start, after consent, retaining the event log (WPF order). Added headless UI checks for reset and monitor selection.
- Ledger remains **stub** with explicit remaining gaps; added numbered manual tester steps. No new window/surface was introduced; existing webcam windows close through the tracker-stop state path used by panic.
- Import source fetched read-only: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported. Behaviour adapted from WPF at the pinned base, retaining existing head files.

## WPF gap audit and assumptions

Read the ledger and its cited WPF tracking, enumerator, calibration and tracker-test files, plus App placement helpers, Lab debug handlers and the cursor host.

The initial ledger's remaining gaps were monitor selection/display changes, desktop cursor, WinRT-only cameras, bounded opening, flat/static probing, debug reset, sensitivity control, and tongue consumers. This slice ports the bounded opening/probing, debug reset and monitor picker/placement. Runtime gaze geometry is intentionally not rewritten when a display disappears.

Remaining work (kept visible in the ledger):

- Desktop GazeDebugCursorService: reference-counted host, pink/gold lock state, face-lost hiding, trail and desktop coordinate/DPI placement, display-change refresh. The existing calibration-local trail is not a desktop cursor. Its lifecycle and compositor/native click-through checks need a separate host; no partial topmost overlay added here.
- Live display-change gaze resolution/repositioning for already-open windows (opening after an unplug/replug resolves afresh).
- WinRT-only enumeration and corresponding capture bridge: WPF WebcamWinRtEnumerator relies on Windows projections; DirectShow/V4L2 enumeration remains the current head path.
- Tongue consumers in Companion BarkService depend on its unported webcam event wiring. Calibration tongue validation is dead WPF code: `git grep -n 'ValidateTongueOutStepAsync\|WaitForTongueOutsAsync' 1fa9cc18d226d18884c72c31b785d898e14087e5 -- ConditioningControlPanel/Windows/WebcamCalibrationWindow.xaml.cs` shows declarations/internal plumbing; RunValidationPhaseAsync explicitly excludes tongue-out. No new validation prompt added.
- Optional WPF high-resolution capture/fps fallback, Windows permission/error differentiation and detailed capture diagnostics remain beyond this slice.
- Sensitivity: `git grep -n WebcamSensitivity 1fa9cc18d226d18884c72c31b785d898e14087e5 -- ConditioningControlPanel` finds only the detector setting read. Avalonia already performs that same read on each mouth frame. There is no live WPF sensitivity control to port; do not invent one. This is not a claim that the entire row meets P122.
- Monitor identity uses Avalonia DisplayName (already saved by calibration); platforms with non-unique display names retain that limitation. WPF native DeviceName is not available cross-platform.
- The portable no-camera message remains generic; timeout now has a specific message and CameraInUse state.

## Verification limits

 Windows capture is compiled but cannot be exercised on this Linux worker. No real camera or real desktop is used by tests. Tests use the repository's existing isolated CCP_USERDATA_DIR harness.

- SDK: explicit .NET 10.0.100 installed successfully; channel discovery returned HTTP 403.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all 12 Linux workflow test projects compiled with **0 errors**: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. Exit 0 for each. Existing warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (also rerun after implementation).
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script does not exist on this base. Numbered tester steps were added regardless.
- All test commands set `DiffEngine_Disabled=true`.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; **4401 passed, 2 failed, 5 skipped**. Unmodified failures: PrivacyMigrationSeedTests.AnotherAccountSendsNone (settings-backup file sharing race), BoardTilesTests.A_frame_fits_the_budget (12.80 ms). Existing Vosk tests self-skipped without the unavailable model; no test was changed to skip.
- Core focused retry (`PrivacyMigrationSeedTests`, board timing, `WebcamMonitorSelectionTests`, `--no-build`): exit 1; **11 passed, 1 failed**. Privacy migration and all new monitor cases passed; board timing still failed (25.10 ms). No changes made to unrelated tests.
- Broad Avalonia webcam-name filter, `--no-build`: exit 1; **28 passed, 36 failed**. 29 glyphTypeface/font failures, one existing help-loop pixel assertion (the filter also matches theory data), one outdated placement expectation, and five subsequent session-state failures. Font failures prevent meaningful UI validation on this worker. Updated the old placement test to assert WPF selected-monitor/primary-fallback behaviour and saved-choice preservation; it passes in isolation. No tests weakened or disabled.
- Final `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~CCP.Avalonia.Tests.WebcamOpenSafetyTests|FullyQualifiedName~QuickRecal_UsesSelectedMonitor'`: exit 0; **4 passed**, including new watchdog/probe cases and the strengthened placement test. The test project compiled again with zero errors.
- Isolated lifecycle filter (`WebcamTrackerTests` excluding the UI-only LiveCapture test, `--no-build`): exit 0; **7 passed**, no skips. Resolves the broad run's session-state contamination concern.
- The first overlapping Avalonia build/test attempt hit a PDB file lock (AVLN9999). Subsequent sequential build and `--no-build` test runs above completed; this was orchestration contention, not a source error.
- `git diff --check`: exit 0. No real camera, Windows runtime, desktop cursor, display hotplug hardware check, render-all or GitHub rcheck run here. The two new UI checks compile but hit the documented font failure before their assertions; supervisor validation remains needed. Fail-before runtime proof for those checks is therefore unavailable here.

- Windows-targeted build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, **0 errors**, 557 existing warnings. Windows code compiled but not run.
- Final Core retry after all builds went idle (`BoardTilesTests.A_frame_fits_the_budget|WebcamMonitorSelectionTests`, `--no-build`): exit 0, **5 passed**, no skips. Both initially failing Core tests therefore pass in focused reruns; the original full-suite result is retained above.

## Changed files

- `CCP.Avalonia/Platform/CameraOpenAttempt.cs`
- `CCP.Avalonia/Platform/WebcamScreens.cs`
- `CCP.Avalonia/Platform/WebcamTracker.cs`
- `CCP.Avalonia/Views/Controls/AppSettings/DevicesSettingsSection.WebcamEvents.cs`
- `CCP.Avalonia/Views/Controls/AppSettings/DevicesSettingsSection.WebcamMonitors.cs`
- `CCP.Avalonia/Views/Controls/AppSettings/DevicesSettingsSection.axaml.cs`
- `CCP.Avalonia/Views/Windows/WebcamCalibrationWindow.axaml.cs`
- `CCP.Core/Services/Webcam/WebcamMonitorSelection.cs`
- `Tests/CCP.Avalonia.Tests/WebcamCalibrationTests.cs`
- `Tests/CCP.Avalonia.Tests/WebcamDebugResetTests.cs`
- `Tests/CCP.Avalonia.Tests/WebcamOpenSafetyTests.cs`
- `Tests/CCP.Core.Tests/WebcamMonitorSelectionTests.cs`
- `docs/avalonia-parity.md`
- `docs/avalonia-test-plan.md`
- `docs/cloud/fin5-shell-webcam.md`
