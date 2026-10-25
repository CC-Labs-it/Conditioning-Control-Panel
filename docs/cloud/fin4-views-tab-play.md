# fin4-views-tab-play

- Wire the existing webcam tracker lifecycle into Play's read-only status chip. Tracking and FaceLost keep Gaze/Focus Gaze bright; all other states dim them to 0.62 and show the needs-tracker notices. Tier repaint never changes that opacity. Repaint on attachment and language changes; unsubscribe on detach.
- Port the WPF wall entrance: first eight visible opaque sections, 40 ms delay capped at six slots, 220 ms quadratic fade and 260 ms 10 px rise. Cancel on hide/detach; skip at motion Off. Animate Visuals, never Transform.RunAsync; no ambient loop or Effect added.
- Add focused headless tests for tracker presentation, tier repaint independence, attachment lifecycle, entrance cancellation and motion gating. Add numbered manual tester steps. No new window or notification surface: existing panic registration remains sufficient.
- Keep `views-tab-play` **stub**, with an explicit remaining list; no claim of verified parity.
- Reference source is WPF at `572433897d4652358b235330dc2fab72df0342e6`. Fetched and inspected read-only trunk `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Its Focus Gaze implementation still lacks WPF zoom refinement/cursor behavior, so importing it would not finish the row.

## Initial WPF gap inventory

1. Full Breakout launch gate, full entitlement and live access updates through the Back Room bridge; the base only registers the explicit demo.
2. Goon account check, host, game frames, media/transfer, scoring and refocus lifecycle.
3. Back Room account/host/bridge, station selection and game transitions.
4. DtRH and Arcademy launcher destinations, entitlement/account rules and game-specific host protocols.
5. Focus Gaze switch: ungated OFF; Tier 2 before consent on ON; async webcam start; calibration failure copy; active/waiting status; persisted intent and external stop synchronization. Its service also owns dwell/blink targets, target scoring, calibration-space conversion, cursor/refinement and overlay adapters.
6. Webcam chip state/color, needs-tracker notices and card dimming (now implemented for states the existing tracker distinguishes). Distinct camera-denied/in-use states remain unavailable from the current boolean camera-open seam; failures say Error.
7. ChromeFx entrance stagger (now implemented). The ledger's alleged Play hover lift is not a live WPF behavior: `rg -n 'HoverLift|MouseEnter|MouseLeave' ConditioningControlPanel/Views/Tabs/PlayTabView*` has no matches at the pinned base. Its border hover styles were already ported. This is not a P122 promotion: real dependencies remain.

## Remaining dependencies and scope

The five game hosts/page bridges and the Focus Gaze target/refinement host remain unported on this base. Registering game URLs or making the Focus Gaze checkbox look active would bypass required behavior. WPF `GoonHostService.cs` is 2,167 lines, `BackRoomHostService.cs` 770, and `GazeFocusService.cs` 1,222, before DtRH/Arcademy and tests. The trunk's smaller Focus Gaze core/head alone are 205 + 268 lines but need overlay adapters absent on this base, and explicitly omit refinement. Completing all those live dependencies with behavior tests exceeds this unit's 1,000-added-line budget. This unit delivers independent presentation gaps and leaves the launch controls honestly disabled. No WPF source was changed.

## Assumptions

- Preserve WPF's actual Play hover-border behavior; do not invent a hover scale from the stale ledger note.
- Keep platform lifecycle presentation in the Avalonia head; no new business logic is needed in Core.
- Preserve the existing tracker API's general Error result where it cannot distinguish native camera failure causes.

## Verification limits

Verification results are recorded below after the build sweep. Native camera/browser execution, Windows runtime execution and visual timing inspection are unavailable here. Per the supplied environment limitation, Avalonia headless tests are compiled but not run because font creation is known to fail in this image; no before/after behavioral failure is claimed. The new tests require supervisor execution. This base has no test-plan coverage script; numbered tester steps were added anyway.

- `dotnet build` (Release) for CCP.Core, CCP.Avalonia and all 12 test csproj files referenced by `.github/workflows/build.yml`: all exit 0, 0 errors. The Windows-targeted test build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` and finished with 2 warnings, 0 errors. Existing warnings were not suppressed. Final focused headless assembly build: 50 warnings, 0 errors.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; Failed 0, Passed 2811, Skipped 5, Total 2816 (59 seconds). Five existing Vosk tests skip without the unavailable model; no tests were weakened or changed.
- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed (424 XAML type references checked).
- `git diff --check`: exit 0.
- Initial test build copies exhausted temporary disk space. Removed only generated build outputs between projects and retried; all required builds then succeeded.
- Not run: `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~PlayWallTests` (documented font-creation environment failure), Windows tests/runtime, native camera/browser execution, render-all. Required follow-up: run PlayWallTests and manual tester steps in the supervisor environment. No successful fail-before-fix demonstration is claimed here.
