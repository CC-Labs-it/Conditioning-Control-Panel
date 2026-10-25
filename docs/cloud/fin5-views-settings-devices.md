# Settings Devices completion

- Base verified at `084317e7b28af03faf519de89314cfb73e3a16d8`; local snapshot branch name was `work`.
- WPF gap inventory: calibration monitor picker/placement; debug cursor host; voice-mode toggles with premium, consent and always-on warning; PTT capture; live microphone/wake-phrase restart; device/headphone chip refresh; camera shortcut editor and global route. Pause capture and Windows grace-pause already existed; X11 pause routing was missing.
- Reused existing voice-mode, consent, speech, chip, grace-pause, trail, screen and keyboard hosts. Added the debug visualization with panic registration and a live camera shortcut route. No Core dependency changes.
- Added focused Devices regression tests and numbered manual tester steps. Kept one ledger row.
- Fetched `parity/trunk` read-only for source availability; no trunk code imported, so no imported commit credits apply. WPF reference is pinned to the base above.

## Verification limits

- SDK 10.0.100 installed after channel discovery returned HTTP 403.
- `dotnet build -c Release -m:1`: CCP.Core, CCP.Avalonia and all 12 Linux test projects named in build.yml passed (exit 0, zero errors; existing warnings retained).
- WPF test project built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Windows code was compiled, not executed.
- `bash .github/scripts/core-guards.sh`: exit 0.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; this script does not exist on the required base. Numbered tester coverage was added manually.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 4,117 passed, five existing Vosk skips, zero failures.
- Focused Avalonia filter: DevicesSettingsCompletionTests, PauseKeyCaptureTests, WebcamDevicePickerTests, PanicSurfacesTests and PanicLeftoversTests: final exit 0; 19 passed, zero skipped/failed. New Devices class independently: six passed. Updated Quick Recal placement test independently: one passed.
- Regression proof: substituting the pinned base Devices control (all other implementation files retained) compiled successfully; the new mode-off, PTT-capture and monitor tests all failed on behavior assertions (exit 1, three failures). Restoring the implementation produced the passing focused results above.
- Broader filter including WebcamCalibrationTests: exit 1; 15 passed, 24 failed at glyphTypeface creation (Consolas/Inter/Segoe UI Black), reproducing the documented process-wide font bootstrap limitation. No tests were weakened or skipped. The pre-existing placement assertion was updated to WPF's selected-monitor contract and strengthened with disconnected-monitor fallback/settings-preservation assertions.
- An initial overlapping build/test attempt produced missing precompiled-XAML errors; the sequential build and focused rerun resolved them.
- `git diff --check`: exit 0.

Physical camera, microphone, multi-monitor DPI, native Wayland global input and compositor click-through behavior require target-platform testing. The full Avalonia suite and WPF runtime tests were not run. No PR was opened.

## Assumptions

- Avalonia display names replace WPF device names; `Primary` remains the stable sentinel and a disconnected selection falls back without rewriting settings.
- Global camera input follows the existing non-consuming X11/Windows listener policy. Native Wayland global reach remains the established platform limitation.
- Debug cursor rendering reuses the calibration comet; unsupported transparency/click-through refuses the overlay instead of intercepting input.
