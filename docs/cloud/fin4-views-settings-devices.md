# fin4 device settings

- Adapted voice-mode controls, live voice-device apply, monitor selection, camera shortcut host and focused tests from read-only `parity/trunk` commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing base files won: retained Fluent icons, per-section reveal refresh, panic/pause suppression and status presentation. No PR opened.
- Added webcam diagnostic subscriptions for face, blink, mouth and gaze changes, detached with the section.
- Monitor selection now places calibration, quick recalibration and tracker-test windows; Primary survives reorder and missing displays. Saved missing choices are not overwritten during population.
- Voice enable preserves WPF gate order; OFF is ungated. Mic/phrase edits re-arm the existing voice host; device chips refresh. PTT capture ignores modifiers and restores its localized button binding.
- Camera shortcut editing uses the existing capture dialog. The shell binding and Windows hook reach the existing camera panic surface; no new surface is created.

## Gap analysis at the pinned base

WPF sources: DevicesSettingsSection XAML/code-behind, MainWindow.Autonomy.cs:609-745, MainWindow.LabTab.cs:657-750/1433-1510, MainWindow.SessionIO.cs:1403, App.xaml.cs:1485-1533, and Services/Tracking/GazeDebugCursorService.cs at base `e9533d4ec2a904673eb1d718d994976555551b46`.

- Ledger was stale about pause rebinding: the base already contains capture, Escape unbind, cancellation and grace-pause routing. Preserved it.
- Missing on the base and implemented here: monitor editor/placement; wake/PTT editor handlers, consent/confirmation and PTT capture; live microphone/phrase re-arm and headphone/device chip refresh; camera shortcut editor and shell/Windows route; face/blink/mouth/gaze diagnostic display.
- Still missing: full gaze cursor host including WPF spring/trail renderer and lifetime reasons; Linux global camera chord routing; tongue-out diagnostic events (no source on this tracker); click-based auto drift correction and calibrated-screen content-placement hosts (settings already save). These hosts and their meaningful tests exceed the remaining 1,000-added-line allowance, so the row stays `stub`. No dead-WPF/P122 claim.
- Native Wayland has the existing global-key backend limitations. Display-name fallback uses a neutral display ordinal when the backend has no name.

## Assumptions

- Use trunk's focused-window PTT capture with cancel-on-deactivation, matching existing panic/pause capture UX; WPF also captures the key through its global hook.
- Reuse the existing camera panic registration rather than registering a second surface. Windows global camera routing is compiled here; Linux global routing remains explicitly missing.
- Keep the full gaze cursor disabled rather than ship trunk's plain ring as full WPF spring/trail parity.

## Verification limits

- `dotnet build -c Release`: Core, Avalonia and all 13 test projects named in the workflow compiled with zero errors (exit 0). WPF test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; Windows code compiled but was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. This base has no test-plan-coverage script; numbered WPF tester steps were added manually.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 4,117 passed, 5 existing Vosk-dependent tests skipped because the model is unavailable; 4,122 total. No tests were changed to cause those skips.
- Focused Avalonia filter `FullyQualifiedName~DevicesVoiceModesTests|FullyQualifiedName~WebcamMonitorSettingsTests|FullyQualifiedName~CameraShortcutTests`: exit 1; 4 passed, 3 failed during font creation (`Could not create glyphTypeface`, Consolas), before UI assertions. Final sources were compiled again with zero errors. The supervisor must execute these UI tests on its font-capable runner. A before/after UI failure proof is blocked by the same font failure.
- Hardware camera/mic, real multi-display placement, Windows hooks and render-all were not run. No new surface: `PanicSurfaces` already registers `camera`, which calls `StopCameraForPanic`.
- `git diff --check`: exit 0. SDK 10.0.100 was installed by pinned version after channel discovery hit a 403.
