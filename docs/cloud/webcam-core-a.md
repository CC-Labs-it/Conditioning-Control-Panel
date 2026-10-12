# webcam-core-a: gaze lock-on and long-stare rules move to CCP.Core

Base: `avalonia-port/first-show`. Unit A of moving the WPF-independent rules of
`ConditioningControlPanel/Services/Webcam` into `CCP.Core/Services/Webcam`.

## Inventory (what was already in Core)

| WPF `Services/Webcam` content | State before this unit |
| --- | --- |
| Blink/EAR, One-Euro, median, detectors (`WebcamPipeline`, `FaceModels`) | already in Core (6afe5a07c, 156c7160d); WPF delegates |
| Gaze chain: smoothing, two-eye gate, side stability, projection, follower, head pose (`GazeEngine`) | already in Core (ea1738562); WPF delegates |
| Calibration model + fit (`WebcamCalibrationData`, `WebcamCalibrationFit`) | already in Core (6afe5a07c, ab1595abd); WPF delegates |
| Consent version (`WebcamConsent`) | already in Core |
| **Gaze lock-on** (`ApplyGazeLock` + 6 constants + engagement state) | WPF-only, **moved here** |
| **Long-stare heuristic** (`UpdateLongStareHeuristic` + 4 constants + buffer) | WPF-only, **moved here** |
| Mouth-open (MAR) state machine, tongue HSV ratio | WPF-only, next unit |
| `WebcamDeviceEnumerator` (DirectShow COM), `WebcamWinRtEnumerator` (WinRT) | Windows-only platform code, stays in WPF |
| Capture loop, ONNX sessions, OpenCV open/backends, hi-res latch | engine/platform, stays in the head |

`WebcamTrackingService` is one 2.7k-line file, so the moved rules are extracted into new Core
files rather than `git mv`'d; nothing in the folder was a whole pure file left to rename.

## What changed

- `CCP.Core/Services/Webcam/GazeLock.cs` (new): the stateful target lock-on, moved verbatim
  from `WebcamTrackingService.ApplyGazeLock` (same constants, same EMA sway estimate, same
  target-switch reset, same 4x-radius taper). `SetTarget`/`Clear` are UI-thread safe (volatile
  target), `Apply` is capture-thread only, as before.
- `CCP.Core/Services/Webcam/LongStareDetector.cs` (new): the long-stare heuristic moved verbatim
  from `UpdateLongStareHeuristic` (30-point buffer, 3 s span, 60 px radius, 5 s cooldown). Takes
  `now` as a parameter so it is testable; WPF passes `DateTime.UtcNow` as before.
- `ConditioningControlPanel/Services/Webcam/WebcamTrackingService.cs`: the private copies are
  deleted and the service routes to the Core classes. `SetGazeAttractor`/`ClearGazeAttractor`,
  `LastPreLockGaze` and `OnLongStare` keep their public shape; the buffer is still cleared on
  `ResetHeuristicState` and after a long face loss, and the stare cooldown still survives both.
  The tuning narrative stays in the WPF file next to the call site.
- `Tests/CCP.Core.Tests/GazeLockTests.cs` (new, 8 tests): pass-through without a target,
  engagement build and 0.93 contraction, no pull past 4x radius, drifting target keeps the lock
  and a jumped target restarts it, `Clear` releases; long stare fires at the centroid after the
  full span, respects the cooldown, needs every point inside the radius, `Clear` drops the buffer.
  There were no WPF tests for these rules to move.
- No App.* seam was needed (the rules are numbers in, numbers out); no embedded resources moved,
  so no core-guards baseline entry.
- Trunk: `parity/trunk` has no gaze lock / long-stare code (its `WebcamTracker` changes,
  336724c0b / e3618a7d3 / 86f7b164b, are device listing and capture); nothing imported.

## Next units

- **webcam-core-b**: mouth-open MAR state machine (`ComputeMAR`, `UpdateMouthState`, baseline /
  hysteresis / median buffers) into Core; tongue HSV thresholds + ratio decision, leaving only
  the `Mat` pixel loop in the head.
- **webcam-core-c**: device-index/name rules shared by both heads (the "(unnamed device)"
  fallback, DirectShow-empty -> WinRT fallback order) behind a small enumerator seam; the COM and
  WinRT bodies stay Windows-only.
- **webcam-avalonia-lock**: Avalonia `WebcamTracker` consumes `GazeLock` + `LongStareDetector`
  (Set/ClearGazeAttractor, LastPreLockGaze, OnLongStare) so the calibration bubble test and
  gaze-pop bubbles get lock-on on Avalonia; removes the `ponytail:` note there.

## Verification limits

- .NET SDK 10.0.112 (Ubuntu archive; `builds.dotnet.microsoft.com` is blocked by the sandbox
  egress policy).
- `dotnet test Tests/CCP.Core.Tests`: 1166 passed, 5 skipped, 2 failed. The 8 new
  `GazeLockTests` pass (two of them had wrong expectations on the first run and were corrected
  to the moved rule, not the other way round). The 2 failures don't touch webcam code and come
  from the container: `LoopbackOAuthTests ... (host: "[::1]")` (no IPv6 loopback) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the
  folder can be deleted). They were not re-run on the base branch.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "Webcam|GazeMinigame"`: 24/24 passed
  (the Avalonia tracker is unchanged; this is a regression check).
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build` succeeded for CCP.Core, CCP.Avalonia, CCP.Core.Settings.Tests,
  CCP.Core.Content.Tests, CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,Report,
  GeneralSettings,Snapshot}.Tests and Tests/CCP.Avalonia.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false` succeeded, so the WPF head with the
  edited `WebcamTrackingService` **compiles**. It was **not run**: WPF, the OpenCvSharp Windows
  runtime and DirectShow/WinRT need Windows. No camera was used on any OS.
