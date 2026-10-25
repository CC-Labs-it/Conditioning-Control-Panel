# fin4-feat-fyp-host

- Connected For You eye control to the existing webcam tracker, consent dialog and calibration window. Consent refusal and startup failure reset the saved toggle and send the WPF status reason; camera startup stays off the UI thread.
- Added blink and held-eye frames, calibrated-monitor to feed coordinate conversion (mixed DPI and legacy-origin fallback), clamped outside gaze, and 100 ms gaze coalescing. The saved eye setting re-arms on page ready.
- Unsubscribed on toggle-off/close/panic and stopped only the camera acquired by the feed. Closing or toggling off during startup releases a late camera. Consent and calibration windows are owned by the existing games panic surface's feed window.
- Added focused headless lifecycle/gesture/calibration/gaze tests and a Core coordinate-conversion test; replaced the old unsupported-eye expectation with a consent refusal expectation.
- Kept `feat-fyp-host` at `stub`, replaced stale duplicated ledger history with current behavior and remaining gaps, and added numbered manual tester steps.
- Source: WPF reference at base `df5a59167c58ab3735c2a5295db3b3b7243af475`. Fetched and inspected read-only trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code imported. Existing base files retained.

## WPF gap analysis

Before this change the live feed lacked all camera lifecycle/consent/startup/failure paths, persisted-eye re-enable, blink and held-eye frames, calibrated gaze coordinates and throttling, and native calibration routing. Those are connected in this slice.

Remaining (the row deliberately stays stub):

- Ghost mode: native translucent click-through mirror, real-window parking/restore, opacity updates, gear/mute controls, minimize veto/recovery, health checks, page-reinit/calibration unghost, and ghost-first panic with the 1500 ms recently-unghosted grace. WPF `Services/Fyp/FypHostService.cs` window mechanics plus `FypGhostOverlay.cs` are live, not P122 dead code. The WPF mirror alone is 557 lines; trunk's platform implementation is 556 lines plus 99 lines of contracts and a separate host state machine and tests. A correct ghost port plus its tests and the eye work exceeds the unit's 1000-added-line budget. No opaque/translucent approximation substituted.
- Video and bubble-count feed stand-down: bounded coalesced deferred replay, manual-trigger notice, and the earned-video bypass. WPF `Services/Video/VideoService.cs` and `Services/BubbleCountService.cs` contain the live feed guards; these scheduling integrations remain unported.
- Autonomy web-video and possession exclusions: WPF `Services/AutonomyService.cs` and `MainWindow/MainWindow.Possession.cs` call `FypHostService.IsActive`. This base has no autonomy browser-media service or possession target host. Their full hosts cannot fit alongside ghost and eye work here.
- EMI `fypOpened` and once-only `fypClosed { minutes }` (floor elapsed minutes, nonnegative): WPF Launch/Close fire them, but this head has no EMI moment bus (`EmiDeskWindow.Ring.cs`, `EmiTargets.cs` document that dependency). A no-consumer event would not close the parity gap.
- Existing file-menu difference: reveal opens the containing directory without selecting the file.

## Assumptions

- Prioritize the usable camera/calibration path under the cap; retain honest missing behavior rather than marking the row wired.
- Reuse existing owned modal windows and games panic registration. No new independent surface is introduced.
- Keep one gaze subscription per enabled visit, with mode/calibration gates inside the handler; this preserves frames and timing without a second subscription state machine (`ponytail` comment in code).

## Verification limits

Camera hardware, native WebKit input, mixed-monitor desktop rendering, and Windows behavior cannot be exercised in this headless Linux environment. No WPF reference files were changed.

- `dotnet build -c Release`: Core, Avalonia, and all 12 Linux workflow test projects passed with zero errors (existing warnings remain).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (run twice, including the coordinate helper).
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1, 4117 passed / 1 failed / 5 skipped. A second full run with `DiffEngine_Disabled=true` produced the same counts. The sole failure was unchanged `BoardTilesTests.A_frame_fits_the_budget` (15.00 ms first run, 19.35 ms second). Its isolated retry passed (exit 0, 1/1). No board code or test was changed. Vosk-dependent tests were among the environment skips; no model was downloaded.
- Focused Core test filter `FullyQualifiedName~FypRulesTests|FullyQualifiedName~FypFileRulesTests`, with `DiffEngine_Disabled=true`: exit 0, 28 passed / 0 failed / 0 skipped.
- `git diff --check`: exit 0. `test-plan-coverage.sh` is absent from this base (attempt exit 127); numbered tester coverage was added manually.
- Windows-targeted build: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 559 warnings / 0 errors. Compiled only; WPF/Windows code was not run on Linux.
- Headless filter `FullyQualifiedName~GameWindowFypTests` (`--no-build`, `DiffEngine_Disabled=true`): attempted, exit 1, 10 failed / 0 passed. Every failure is the known environment font blocker, `Could not create glyphTypeface` for `Segoe UI Black`, in shell construction before the test body. The behavior assertions remain unexecuted here and require the supervisor's font-capable runner. No tests were skipped or weakened to mask it.
- Native camera, actual blink timing, mixed-monitor desktop rendering, native web input and manual tester steps were not run. The tests use the existing sandbox profile and replace camera/modal boundaries; no real desktop or user profile was used.
