# fin-shell-lab-tab

- Extend the live Focus Gaze path from bubbles to flash images and video attention targets. Flash pop and linger follow their own switches, independently of mouse clickability: 600 ms dwell, 1.10 inflate, 250 ms cooldown and linger throttle, lifetime extension without shortening or reviving an expiring flash. Gaze pops suppress hydra multiplication; linked mouse-pop children inherit the extended remaining lifetime.
- Reuse the existing video attention hit path for both pointer and gaze, preserving batch scoring, sound, fade and idempotence. Blinks do not click video targets.
- Apply the calibrated-monitor gaze-read restriction to all three target types. Use connected display identity and scaling; missing or unplugged calibration fails open as WPF does. Ordinary flash/bubble/video spawn placement remains unchanged, matching WPF's current read-time filtering policy.
- Enable the Settings → Devices monitor picker and use the saved choice, primary fallback, then first display for calibration, Quick Recal and tracker test. Re-enumerate camera/monitor lists through the existing Settings per-open interface; seeding never overwrites a missing saved monitor.
- Wire face, blink and gaze diagnostics, with a 12-line log, duplicate gaze/state suppression and detach cleanup. Route bubble gaze pops to the existing bark engine. Distinguish failed camera opens (CameraDenied) from generic engine errors.
- Add focused tests in ShellLabTabTests, extend MandatoryVideoOverlayTests and strengthen WebcamTrackerTests. Existing overlay and camera panic registrations cover these paths; this change creates no new surface.
- Source credit: WPF reference at `182dfae0f9773e87a91e20e94832d2bf357c385d`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported or existing head file overwritten.

## Reference gap audit

The base ledger row is `stub`. Its named WPF partial is live through Play, Settings → Devices, Blink Trainer and Deeper; its historical filename does not make it dead code. The additional live implementations below are reached by that partial's Focus Gaze switch and camera engine.

| WPF behavior absent on the base | Reference at the pinned base | Outcome |
| --- | --- | --- |
| Flash pop/linger targets, dwell feedback, blink pop and gaze-driven hydra guard | GazeFocusService.FindBestTarget/AdvanceFlashDwell/HandleBlink; FlashService.GetGazeTargets/GazePop/FlashWindow.BoostLifetime | Implemented using the existing flash pop/exit path |
| Video attention gaze click | GazeFocusService.AdvanceFloatingTextDwell; VideoService.GetGazeTargets/GazeClick | Implemented using the existing batch hit path |
| Two-stage zoom refinement: ambiguity, enlarged choices, 550 ms second dwell, 6 s timeout, 700 ms look-away, 1200 ms face-loss grace, 4 s cancel suppression | GazeFocusService.TryRaiseRefine/TickRefine/CloseRefine; GazeRefineOverlay | Missing; requires the refinement surface and lifecycle tests |
| Gaze-lock attraction and cursor visibility/locked feedback | GazeFocusService.SetCursorLock/Start/Stop; GazeDebugCursorService | Missing |
| Click-based auto drift correction | LabTab.ChkWebcamDriftCorrection_Changed; GazeDriftCorrectionService | Saved setting existed; runtime service remains missing |
| Restrict gaze interaction to the calibrated display | GazeFocusService.FindBestTarget/TargetOnCalScreen | Implemented; no blanket restriction of ordinary content spawning |
| Bark on bubble gaze pop | BarkService.WireSubscriptions, GazePopped | Implemented through the existing bark engine |
| Face/blink/gaze counters, event log and subscription teardown | LabTab.EnsureWebcamDebugSubscribed/UpdateWebcamDebugCounters/UnsubscribeWebcamDebug | Implemented; mouth/tongue logging still depends on unported MAR/HSV detectors |
| Monitor enumeration, saved selection, primary/unplugged fallback and gaze-window placement | LabTab.RefreshWebcamMonitorList/FillMonitorCombo/CmbWebcamMonitor_SelectionChanged; App.GetWebcamCalibrationScreen/ApplyCalibrationScreenPlacement | Implemented; uses Avalonia display identity |
| Quick Recal global chord and discoverability hint | LabTab.RefreshQuickRecalHotkeyHint; MainWindow.QuickRecalHotkeyHint; GlobalHotkeyService | Missing; depends on unported global camera/recal shortcut service |
| Capture failure distinctions and native open/probe retries | WebcamTrackingService.OpenCaptureCore | CameraDenied on failed Open implemented; CameraInUse probe/backend distinctions remain missing |

Keep the row `stub`: refinement, cursor/lock/drift and native capture work remain, beyond this bounded slice. The dependency-only NOOP rule and P122 do not apply because independent live user paths were missing and have been implemented.

## Assumptions

- Avalonia DisplayName is the portable monitor identity; when a backend omits it, use a stable pixel-origin label. A missing saved display falls back to primary without rewriting the preference.
- Failed Open follows WPF's CameraDenied state. Do not infer CameraInUse without WPF's successful-open/failed-probe evidence.
- Leave the Quick Recal hint absent until its global shortcuts work. Keep the gaze cursor disabled until its surface exists.
- Diagnostic counters retain their values across navigation but observe events only while Devices is attached, following this head's existing view subscription lifetime. WPF keeps its debug subscription on MainWindow until closing.

## Verification limits

- SDK: .NET 10.0.402. Core and Avalonia build successfully. All 11 Linux test projects listed in `.github/workflows/build.yml` built with zero errors, including the final CCP.Avalonia.Tests build (exit 0). The language project's first concurrent build lost an MSBuild child process; its serial retry succeeded with zero errors.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (424 XAML type references checked). The first sandboxed invocation could not create temporary files; the normal writable execution passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: initial concurrent run had one timing failure in GoonClockAndRoundsTests. Serial rerun: exit 0, 2818 passed, 5 existing Vosk tests skipped because the model is unavailable, 2823 total. No test was weakened or skipped by this change.
- Camera-state regression: temporarily restored WebcamTracker from the pinned base while retaining the strengthened test. The filtered MissingCamera test failed with `Expected: CameraDenied; Actual: Error` (exit 1), proving the regression assertion catches the original behavior. With the implementation restored, the same filter passed: exit 0, 1 passed, 0 failed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors; includes the frozen WPF app. Windows code was compiled but not run.
- The UI/window tests in ShellLabTabTests, FocusGazeTests, LabWebcamStatusTests, FlashPopTests, FlashWireTests, MandatoryVideoOverlayTests and PanicSurfacesTests were not run here, following the supplied cloud font limitation. They require the supervisor's font-capable rcheck runner. The main test project compiled with zero errors before push.
- Render-all was not run because of the same font limitation. No real camera, user profile, desktop notification, Windows desktop, or physical multi-monitor setup was used. Test profiles remain sandboxed by CCP_USERDATA_DIR.

## Changed files

- CCP.Avalonia/Platform/GazeFocus.cs, WebcamTracker.cs, WebcamScreens.cs
- CCP.Avalonia/Views/Controls/AppSettings/DevicesSettingsSection.axaml.cs
- CCP.Avalonia/Views/Overlays/FlashOverlay.cs, FlashOverlayWindow.cs, MandatoryVideoOverlay.cs
- CCP.Avalonia/Views/Windows/MainShellWindow.LabTab.cs, WebcamCalibrationWindow.axaml.cs, WebcamGazeTrackerWindow.axaml.cs
- Tests/CCP.Avalonia.Tests/ShellLabTabTests.cs, MandatoryVideoOverlayTests.cs, WebcamTrackerTests.cs
- docs/avalonia-parity.md and this review draft
