# fin-shell-webcam

- Port WPF mouth-open MAR and tongue-out HSV detection: closed-frame baseline, median smoothing, sensitivity, speech floor, hysteresis, minimum holds, edge firing, cooldowns and face-loss resets. The frame-free state lives in Core; pixel classification stays in the head and retains no frames.
- Wire both calibration mouth checks to real detector events, retaining WPF order, five-second best-effort timeout and the one-second gap.
- Feed Core LongStareDetector with the final follower point, clear its samples on restart/prolonged face loss, and expose gesture/face/gaze diagnostics while Devices settings is visible. Detaching removes subscriptions.
- Share camera indices between picker and capture. Adapt DirectShow enumeration and the Windows DSHOW / DSHOW-MJPG / MSMF / MSMF-MJPG warmup ladder from read-only `parity/trunk`: **336724c0b9e8ab570e29453105890550230a367c**. The enumerator was inspected at **3156683d041a18f718e72d37119b3b64f16b6ded** (its quest strict-probe additions were excluded). Source tip: **a4ee52d40cd347d288dcf7fdaa8445b79f8e5038**. Existing tracker consent, panic generations, wedged-thread guard and event seams were retained.
- Add focused Core and synthetic-frame/headless tests. No new window or overlay was introduced; the existing shell panic stop still stops the capture and closes tracking-dependent windows through StateChanged.
- Reconcile the single `shell-webcam` ledger row; keep **stub** because the remaining calibration and monitor flows are real WPF user paths.

## WPF audit at the pinned base

Reference SHA: **6948b6de0a6126fc40bc510d39f7c10338c5f445**. Reviewed the four files cited by the ledger plus quick recal, loading splash, calibration validation/bubble test, and shell device/debug callers.

The ledger's old calibration, device-enumeration and loading-splash gaps were stale: this base already has 16-point grid fitting, a Linux device picker, splash subscription, tracker visualization and quick recal with real callers in Settings/Devices and Blink Trainer. The actual gaps found were:

1. Mouth/tongue inference and real calibration mouth-event subscriptions: implemented here.
2. Long-stare emission: implemented here using the existing Core detector, including its unchanged buffer duration and cooldown. Gaze lock-on, attractor targets and the pre-lock accuracy snapshot remain absent.
3. Calibration bubble accuracy test: only a placed bubble exists. WPF has multiple target locations, elliptical dwell zones, freshness/rolling-average gates, timeout/near-miss samples, correction fitting/persistence, pop animation/audio and a returned summary. These remain missing.
4. Windows camera enumeration/backend fallback: DirectShow and the four capture attempts implemented here. WinRT/MF-only camera enumeration, the bounded open timeout, flat/frozen-feed probes, saved-name mismatch diagnostics and granular CameraDenied/CameraInUse states remain missing. The imported warmup accepts the first nonempty frame (explicit ponytail comment).
5. Monitor picker, display hotplug revision/fallback resolution and associated calibrated-screen consumers remain incomplete. Existing window placement alone does not implement the service's monitor behavior.
6. Desktop gaze-debug cursor/trail and Quick Recal global hotkey/discoverability hint remain absent. The current in-window verify cursor is retained.
7. Device event counters/logging: wired here with attach/detach cleanup. Full startup state transitions, counter reset per start, detailed performance/threshold diagnostics and the sensitivity control remain incomplete.
8. WPF opens the camera before loading models; this head still loads models first. WPF also has a permanent model-init failure latch, capture pacing and optional high-resolution watchdog plumbing that this head lacks. `rg -n 'HighResCaptureOptIn\s*=' ConditioningControlPanel --glob '*.cs'` returns no live assignment at the pinned SHA; no P122 promotion is claimed for the row.
9. Downstream gesture consumers in companion bark subscriptions, Deeper enhancement and FYP eyes-closed playback remain outside this slice. Detector events and visible diagnostics alone do not claim those features are wired.

WPF explicitly leaves tongue validation out of the calibration sequence (WebcamCalibrationWindow, validation-phase comment); its tongue waiter/retry helpers have no live sequence caller. They were not imported.

Remaining calibration/monitor work cannot fit in the remaining 1000-added-line allowance. This is a partial completion, not a pure dependency NOOP.

## Verification limits

- Exact base commit verified in the fallback clone; the supplied checkout had a different HEAD and read-only Git metadata. SDK 10.0.402 installed.
- Core gesture tests: 8 focused cases. The restart-cooldown regression was first run red (Assert.False received true), then corrected to preserve WPF cooldowns across restart; final full-suite result below.
- Full Core suite: 3549 passed, 0 failed, 5 existing Vosk tests skipped (model unavailable; download blocked in this environment).
- Synthetic Avalonia webcam tests: 12 passed, 0 failed, no windows, fonts or real camera. Filter: `(FullyQualifiedName~WebcamGestureTests&FullyQualifiedName!~DevicesDiagnostics&FullyQualifiedName!~CalibrationMouth)|FullyQualifiedName~WebcamCameraListTests`.
- Window/font-dependent Devices lifecycle and calibration validation tests are compiled, not run here, per the supplied cloud font limitation. Existing WebcamCalibrationTests/WebcamGazeTests/WebcamDevicePickerTests require the supervisor's GitHub run as well. No test was disabled or weakened.
- Core guards: passed (all guards; 429 XAML references checked).
- Builds: Core, Avalonia and all 13 workflow test projects succeeded with **0 errors**. Test projects: `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Snapshot.Tests`, `CCP.Avalonia.Tests`, `CCP.Core.Content.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Tests`, `ConditioningControlPanel.Tests`. The WPF test cross-build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; compiled only, not run on Linux.
- Build command: `dotnet build <project> -c Release` (exit 0 for each). Required tests: `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release` and the filtered Avalonia command above. Guard command: `bash .github/scripts/core-guards.sh` (exit 0). Generated test binaries were removed between builds after an initial disk-full attempt; no source or test was removed.
- Windows-specific COM enumeration and OpenCV backends compile as part of the Linux head build but cannot be exercised on this Linux host. No physical camera, desktop session, audio device or real user profile was used for tests.
- No render-all or visual comparison was run; no verified/P122 claim is made.

## Assumptions

- Prioritize reachable gesture validation and cross-platform camera selection within the line cap; retain the honest stub for the larger bubble/monitor flows.
- Keep WPF thresholds, timing and detection limitations, including the HSV heuristic's potential false positives from inner-lip lipstick. Do not add the unused tongue-validation sequence.
- Use the existing IFrameSource camera seam and shared Core detectors. No extra interface, configurable constant, new surface or desktop animation is needed.
