# fin-feat-fyp-host

- Reached the feed's eye-control path: stored toggles, current-consent prompt, asynchronous camera start, status/refusal frames, blink and held-eyes events, mixed-DPI gaze with a 100 ms throttle, and native calibration through the existing dialog.
- Adapted eye control and fake-camera tests from trunk commits `5bd8be47f88735b8398fb665d283d239a6942c09` and `e3618a7d31e5c726b42cadfd465c38a840c502ce`, inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Kept the base host, Core rules, metadata, file menu, remote bridge and feed deferral implementation; existing files were merged, never overwritten from trunk.
- Added exact throttle-boundary, reload/subscription, consent/start cancellation, owned-dialog panic, calibration reentry and camera ownership tests. Core gaze conversion has mixed-DPI, edge, invalid-size and nonfinite-input tests.
- The existing games panic registration closes the feed and its owned consent/calibration dialogs; closing releases eye subscriptions and any camera started by this feed. Tests use an isolated profile and fake camera; no real desktop/camera or user profile is accessed.
- Kept the single feat-fyp-host ledger row stub. This is a partial finish under the 1,000-added-line cap, not a dependency-only NOOP or a P122 dead-caller claim.

## WPF gap inventory

Compared against WPF at base `cf5923f5b022e9d1f2c8e81fea5a1379508cb052` (paths below relative to ConditioningControlPanel):

- `Services/Fyp/FypHostService.cs:906-1146`: consent/lifecycle, stored-toggle rearm, start/refusal status, blink/held-eyes subscriptions, gaze gating, both DPI scales, 100 ms coalescing, and calibration were absent. Implemented, with duplicate-enable and pending-consent/start teardown guards. Held-eyes detection retains the shared BlinkDetector's two-second threshold.
- `Services/Fyp/FypHostService.cs:421-904` and `Services/Fyp/FypGhostOverlay.cs`: live native ghost, normalize/park/restore, session-only reset, live opacity, gear/options and mute controls, composition diagnostics/refusal, five-second health/topmost watchdog, Show Desktop/minimize/hide healing, reload/calibration exit, and panic's unghost/1500 ms grace remain absent. The head still returns clickThrough false then ghost-unavailable. Trunk ghost files exceed 850 lines before contracts/tests and differ from WPF panic/minimize behavior; importing them with tested eye control exceeds the cap. Eye control is the prioritized user-visible slice.
- `Services/Fyp/FypHostService.cs:78-80,145-153`: usage scoring was already wired; fypOpened and idempotent fypClosed (whole nonnegative minutes) remain dependent on the unported EMI Desk event bus. Evidence: `rg -n 'App.EmiDesk.Fire|own event bus' CCP.Avalonia/Views/Windows/EmiDesk/EmiDeskWindow.axaml.cs`; its own event reactions still have ponytail comments. No speculative event bus was introduced.
- `Services/Fyp/FypFileMenu.cs`: safe IDs, menu order/text, open and folder actions already exist. Selecting the file in the OS folder remains a cross-platform deviation.
- Existing stand-down deviation remains: an expired video hold re-arms the Core schedule rather than dropping WPF's continuation. Autonomy WebVideo and possession stand-downs are dependent paths: the Avalonia head cannot perform WebVideo; MainShellWindow.Possession.cs still has no IPossessionHost implementation. Existing mandatory-video/bubble behavior was preserved.

## Verification limits

- SDK: installed .NET 10.0.402 with dotnet-install; its latest-version probe returned 403, then the supported SDK archive downloaded and installed successfully.
- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia and all 11 Linux-compatible test projects named by `.github/workflows/build.yml` (Avalonia: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Tests; Core: Content, Settings, Tests). The final headless test sources were compiled again after review.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors (existing warnings). Windows/WPF code compiled but was not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. An initial read-only sandbox invocation could not create its temporary files; it was rerun with the required write access.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: initial exit 1, 2842 passed / 1 failed / 5 skipped. The existing BoardTilesTests.A_frame_fits_the_budget measured 15.58 ms against its 12 ms limit during concurrent builds; its code and test were unchanged. After builds ended, the same full suite with `--no-build` exited 0: 2843 passed / 0 failed / 5 skipped (2848 total).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~Fyp'`: exit 0, 40 passed / 0 failed / 0 skipped, including the nine new gaze cases.
- The five existing Vosk skips require the unavailable model; the environment reports its download is blocked with 403. No tests were weakened, removed, or newly skipped.
- Avalonia test execution and render-all were NOT run, following the task's explicit known-font-environment exception; this base lacks the cloud-test-fonts setup. The headless suite compiles with zero errors. Pending supervisor/rcheck execution: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~FypEyeControlTests|FullyQualifiedName~GameWindowFypTests|FullyQualifiedName~WebcamTrackerTests|FullyQualifiedName~WebAssetServerTests'` with `DiffEngine_Disabled=true`.
- Live WebKitGTK, physical webcam/mixed-monitor gaze and native Windows behavior were not exercised. Headless lifecycle/gesture/dialog tests are compiled evidence pending execution, not a claim of runtime proof.
- `git diff --check`: exit 0. Final diff: 899 added lines versus the pinned base (cap 1000); one ledger row for this ID. Staged additions were scanned for personal paths and email addresses; none found.

## Assumptions

- The published snapshot is the base; branch name work has no parity significance. The initial clean snapshot differed and was fetched/reset to the exact required commit before implementation.
- Reuse the existing versioned consent/calibration UX. As WPF FYP does, calibration is a separate page action; consent does not automatically launch it.
- Use the existing games panic surface and Avalonia ownership for feed dialogs. Preserve stored eye toggles when closing a successful feed so reopening re-arms through consent.
- Prioritize tested eye control within the cap and report ghost/EMI work as remaining; the row cannot be marked wired.
