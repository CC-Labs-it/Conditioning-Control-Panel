# wire-win-webcam-calibration: calibration bubble test (row `win-webcam-calibration`, stays stub)

- **Bubble accuracy test ported** to `CCP.Avalonia/Views/Windows/WebcamCalibrationWindow.axaml.cs` from WPF `RunBubbleTestAsync`: four bubbles in WPF's mirror-pair layout and visit order, the dwell ring filling on the rolling-mean ellipse (700 ms, leaky decay), the live pink cursor, the gaze attractor per bubble, the pop sound plus a scale 1 → 1.7 / fade pop (180 ms, held to 320 ms) stepped on the window clock (no Animation on a Transform), near-miss samples on timeout, "Moving on…", the auto-fix trim applied and saved, and WPF's summary text on the verify panel.
- **Shared rules moved to Core** in `CCP.Core/Services/Webcam/BubbleAccuracyTest.cs` (probe layout, `Dwell`, `FitTrim`/`FitAxisTrim` with the half-strength slope, `Summary`, `MedianPoint`) and `GazeTrimData.Compose` (trim composition and clamps). The WPF window and `WebcamTrackingService.ApplyGazeTrim` now call the Core copy, so only one copy exists (net −392 lines in WPF).
- **WebcamTracker** (Avalonia) now runs Core `GazeLock` in the gaze chain: `SetGazeAttractor` / `ClearGazeAttractor`, `LastPreLockGaze` (the point before the lock, for the residual math), and `ApplyGazeTrim`. With no target set, the lock passes points through unchanged.
- **Panic / Escape / tracker stop** during the bubble test close the window and clear the attractor and the gaze subscription straight away, not on the loop's next tick. The window's existing close path already restores the old calibration and stops the tracker, so no new PanicSurfaces entry is needed.
- **Ledger:** only row `win-webcam-calibration` is updated. It stays **stub**, and the remaining work is listed under follow-up lane `wire-win-webcam-calibration-b`: the MAR mouth-open detector (into Core, then WPF calls it and WebcamTracker raises OnMouthOpen) and the cursor motion trail.
- No trunk commits imported: `origin/parity/trunk` has the same stub window (`ponytail: the bubble test ... is not ported`), so this is a fresh port from the WPF reference.

## Verification limits

- `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~WebcamCalibrationTests`: **14 passed, 0 failed**. Three new tests:
  - `BubbleTest_PopsFourBubbles_AndSavesTheTrimFromTheAim`
  - `BubbleTest_NoGaze_TimesOutEveryBubble_AndAppliesNothing`
  - `BubbleTest_PanicClosesTheWindow_AndClearsTheAttractor`
- **Fail-proven:** all 3 new tests are red against the base (stub) window. The panic test was also red when the clear was left to the loop's `finally`.
- `dotnet test Tests/CCP.Core.Tests`: **2787 passed, 2 failed, 6 skipped**. The 2 failures are environmental and in files this change does not touch:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted…` (the container runs as root, so the folder can still be deleted)
  - `LoopbackOAuthTests…(host: "[::1]")` (the container has no IPv6 interface)
- New `BubbleAccuracyTestTests` (layout, dwell, near-miss gate, fit, summary, trim compose) and the existing GazeLock/GazeEngine tests: **22/22 passed**.
- `bash .github/scripts/core-guards.sh`: **all Core guards passed**.
- **Builds clean:** CCP.Core, CCP.Avalonia (via Tests/CCP.Avalonia.Tests), CCP.LinuxSmoke, CCP.VR, and the Core Settings/Content, Avalonia Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot test projects.
- `Tests/ConditioningControlPanel.Tests` builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, so the WPF edits compile.
- **Compiled but not run:**
  - The WPF bubble test and `ApplyGazeTrim` (Windows only).
  - WebcamTracker's lock-on wiring in `ProcessFrame`: it only runs on camera frames with a face, and the tests feed gaze points directly. The GazeLock math itself is covered by `GazeLockTests`.
- `--render-all` was not run: the bubble test is only reachable after a camera calibration, which the render states cannot reach (the calibration render shows the no-camera message).
