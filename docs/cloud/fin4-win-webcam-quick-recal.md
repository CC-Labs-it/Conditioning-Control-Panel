# fin4-win-webcam-quick-recal

- Finish `win-webcam-quick-recal` as wired, with numbered Windows/X11/Wayland tester steps.
- Reuse the existing GlobalShortcuts D-Bus host for independent panic, quick-recal and camera sessions. Preserve the panic API, Windows registrations, X11 grabs, camera fallback key, and existing panic surface registration; no new window is introduced.
- Select the compositor portal in Wayland sessions so both webcam chords reach native applications. Keep consent pending/refused/failed bindings unarmed; filter activation by session/id and suppress callbacks after teardown or compositor session closure. Release pending sessions when settings change or the shell closes.
- Display compositor-assigned triggers in the existing localized two-chord hint, only after both bindings are armed and their descriptions are known.
- Add fake-transport lifecycle tests and a shell integration test; tests never register real desktop shortcuts.
- Imports: fetched and inspected `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported. Existing base files win.

## WPF comparison and assumptions

The base already implements the reference window's text, 600 ms ready / 2 s sample / 350 ms finish sequence, minimum 15 samples, settle-drop median, persisted offset, cancel restoration, sounds, errors, calibrated-monitor placement, Escape, tracking-loss close, and panic teardown. Its live shortcut paths already preserve consent/calibration refusals, no stacked calibration, tracker ownership, hidden/minimized shell ownership, camera consent/toasts and the global setting. No remaining WPF behavior gap was found for this row; the ledger's explicit remaining gap was native Wayland shortcut reach.

- Use the existing portal rather than add a new dependency or host. Wayland chooses the compositor portal; X11 keeps root grabs. A compositor that lacks GlobalShortcuts or refuses consent cannot supply these global chords; existing setup buttons and the camera in-window shortcut remain available.
- The compositor owns authorization and may assign a different chord. Teach its returned trigger description; omit the hint if it reports none. External rebinding after the initial portal response is not monitored.
- The full-calibration monitor picker belongs to the full-calibration feature; quick recal preserves the saved calibrated-monitor placement.

## Verification limits

Verification results are recorded below after the final checks. Windows registration compiles as part of the cross-platform head but cannot be invoked on Linux. No live Wayland compositor or X11 display is available, so compositor consent, real global delivery and OS conflict handling require the numbered desktop tester steps. No PR was opened.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`, `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build -c Release` for all eleven Linux-compatible test projects named by `.github/workflows/build.yml`: exit 0, zero errors (including the headless test project). Existing compiler/analyzer warnings remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 2,846 passed, five pre-existing Vosk tests skipped, zero failed. No tests were weakened or disabled.
- Filtered `dotnet test Tests/CCP.Avalonia.Tests -c Release` for `WaylandWebcamShortcutTests`, `QuickRecalHotkeyTests`, `WebcamGazeTests`, `WebcamCalibrationTests`: exit 0; 40 passed, zero skipped/failed, with sandbox user data. The documented font failure did not occur on this checkout.
- Fail proof: temporarily replacing assigned compositor hints with the former hard-coded requested chords made the new shell test fail (`Meta+G` missing), exit 1. Restored implementation, then reran the new Wayland class and shell test: exit 0; 14 passed.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. `git diff --check`: exit 0. The test-plan coverage script is absent on this pinned base; numbered coverage was added manually.
- Initial read-only fetch failed; used the explicitly authorized fallback clone and verified the exact required base SHA. Initial parallel build copies exhausted disk; removed generated outputs and rebuilt sequentially. The initial disk-related calibration failures disappeared on retry. Generated outputs were removed between large builds; no tracked source or tests were removed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; two existing package warnings, zero errors. Compiled but not run on Linux.
