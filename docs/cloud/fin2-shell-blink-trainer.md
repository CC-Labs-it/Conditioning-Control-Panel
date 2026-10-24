# fin2-shell-blink-trainer

- Added the WPF camera shortcut path: one consent-aware tracker toggle shared with the Blink Trainer button, shell restoration before consent, and two-second started/stopped notifications.
- Devices and Companion open the existing shortcut capture dialog, persist/reset the camera binding and refresh both labels. Global registration uses independent X11/XWayland grabs and Windows RegisterHotKey; refusal or a disabled global flag preserves the in-window shortcut. Registrations are released on shell close.
- Enabled the Devices tracking-monitor picker. Seeding does not overwrite a disconnected display; user selection persists the display name and appends the WPF debug message. An explicit display overrides dual-monitor placement; a disconnected explicit display falls back to Primary. Calibration and quick recalibration follow the selected display.
- Added focused headless behavior tests for display precedence, disconnected displays, picker persistence, WPF modifier parsing/defaults, global registration and consent restoration. Existing session/camera panic registrations remain in PanicSurfaces; no new visual surface was introduced (the existing consent/capture dialogs are reused).
- Kept the ledger row `stub`: the native Wayland global shortcut depends on extending PortalPanicShortcut to support a second chord, and FaceLost/CameraInUse status-chip text depends on tracker states this head does not expose.
- No parity/trunk code imported. Read-only source branch fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; behavior was adapted from the WPF files already present at the required base `ca15351f65930e2e37dcb2c10e04268feab2aee6`.

## WPF gap audit

The base row explicitly deferred the global camera shortcut and tracking-monitor selection. Reading MainWindow.BlinkTrainer.cs, MainWindow.SessionIO.cs (camera shortcut), MainWindow.LabTab.cs (monitor picker/status chips), App.xaml.cs (screen placement), and Services/Tracking/GazeContentScreenPolicy.cs identified these missing reachable behaviors:

1. Shared hotkey toggle, restore/activate before stale-consent dialog, and successful start/stop announcements.
2. Saved/default shortcut parsing, local fallback, system-wide registration, rebinding/reset from Devices and Companion, and label refresh.
3. Primary plus named monitor choices, guarded population, persisted selection, disconnected-display fallback, and explicit-pick precedence over global dual-monitor placement.
4. Calibration/recalibration placement on the user-selected monitor rather than the previous calibration record.
5. Rich webcam chip states (FaceLost and CameraInUse), unavailable from the current tracker; retained as a dependency gap.

Items 1–4 are implemented for Windows and X11/XWayland. Native Wayland global capture remains a dependency gap; local input remains available. The already-live demo, premium/status state machine, media pool/live stage, folder/settings controls, tracker/consent/calibration actions, session lifetime, Deeper twins, splash, animation and progression paths were retained.

## Verification limits

Final checks are recorded below. All tests use the repository's isolated CCP_USERDATA_DIR test setup. No real camera, desktop or user profile was used.

- Core guards: exit 0; all guards passed.
- Core tests after clearing generated-output disk pressure: exit 0, 2,813 passed, 5 Vosk tests skipped, 0 failed. The first run while the disk was full failed three persistence/media tests; the clean rerun passed without source changes.
- The initial overlapping Avalonia builds caused a generated-file lock error; subsequent sequential builds succeeded. Generated test outputs were removed between builds to fit the temporary disk.
- Avalonia UI execution is limited by missing fonts in this environment. The focused pre-final run compiled with 0 errors and ran 28 tests: 14 passed, 13 failed before assertions on glyphTypeface creation, and one picker test had an unseeded capability provider. The provider setup was corrected; the next new-test run passed 7 and failed only the shell test on font creation. These are not green UI verification claims; GitHub must execute the shell tests.
- Windows RegisterHotKey code is compiled but cannot execute on Linux. Real camera, multiple physical displays, Windows hotkey registration and X11 global delivery were not exercised. Native Wayland global capture is not implemented.

Final command results:

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 0 errors |
| `dotnet build CCP.Avalonia -c Release` | 0 | Build succeeded; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests -c Release` | 0 | 222 warnings, 0 errors |
| `dotnet build` for every Linux workflow test project, Release | 0 each | 0 errors in each project |
| `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 2 warnings, 0 errors; compiled, not run |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed |
| `git diff --check` | 0 | No whitespace errors |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 0 | Passed 2813, skipped 5, failed 0 |
| `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~BlinkTrainerShellCompletionTests\|FullyQualifiedName~BlinkTrainerSessionTests\|FullyQualifiedName~BlinkTrainerTabLiveTests\|FullyQualifiedName~DeeperWebcamSetupTests'` | 1 | Passed 18, failed 13; all failures are glyphTypeface creation (Inter / Segoe UI Black), before UI assertions |

All test invocations set `DiffEngine_Disabled=true`. Linux workflow test projects built: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests. The final focused run includes ten passing new tests (monitor cases, picker and modifier parsing); the new shell flow remains blocked by fonts. No test was weakened or skipped to obtain a pass.

- Existing `EmiDeskChordTests` regression check: exit 1, one passed and one failed during shell font creation; no native desktop delivery exercised.

## Assumptions

- Avalonia DisplayName is this head's equivalent of WPF Screen.DeviceName for persisted monitor choices. Primary and case-insensitive matching follow WPF, including missing-display fallback.
- Native Wayland's portal limitation and the tracker's absent FaceLost/CameraInUse states warrant retaining `stub`, rather than declaring complete parity.
- Reusing the existing consent and shortcut capture dialogs preserves their established UI and avoids another surface.
