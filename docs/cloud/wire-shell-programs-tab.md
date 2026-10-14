# wire-shell-programs-tab: live session progress slice

- Base: `avalonia-port/goongame-core-c` (`0e1aa0473`); remote claim `23c7dd4e33c50be21c3de85e7eb0fcda314e3107`.
- Wire the existing Programs session clock and bar to the shell's existing session tick and pause/resume path. Only the live strip changes on a tick: the task list, lifecycle buttons and ignition scenery retain their instances.
- Move WPF's nonnegative m:ss / total h:mm:ss formatter and bounded elapsed/total calculation into Core `ProgramSessionProgress`; WPF delegates to that copy. The WPF progress bar retains its existing engine-provided percent.
- Run the existing session sheen through `BeatLoop`, using WPF's heat-dependent cadence and -160 -> width+60 sweep. It stops on pause, foreign/stopped session, panic, tab hide, detach, minimisation and disabled ambient motion; it retries once the bar measures at least 80 DIP.
- Focused headless tests start an actual program session, step its paused elapsed clock, prove the task-list instance stays unchanged, check pause/resume and hide/reveal, and stop via the real panic registry. Core tests cover negative/overflow elapsed, invalid duration and hour/day boundaries.
- Existing Programs lifecycle and ignition test harnesses now use the shipped font builder, matching RenderProof and the new headless fixture. No assertions were removed or weakened.
- `parity/trunk` was fetched read-only. No trunk code was imported; existing `BeatLoop` and ignition helpers are reused unchanged.

## Follow-up lane: wire-shell-programs-tab-b

Keep the ledger row stub. Remaining work crosses the line cap when implemented and tested together: locked premium browse cards must route to App Info; Dashboard Today-card content/art/idle FX and its navigation caller; tab pulse with rail/door escalation; FireBurstAt task/seal/graduation sparks, rail-node ignite, today's pip breathe and the banner spring. The base already has most ignition scenery and day/chapter/graduation moments, despite the historical ledger notes saying FX is entirely absent. This slice completes the live progress strip without claiming those remaining paths.

## Verification limits

Commands used .NET 10.0.402 (`PATH`/`DOTNET_ROOT=/tmp/ccp-dotnet`) and `DiffEngine_Disabled=true`. All builds used `-m:1` and all three hardlink flags: `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, and `CreateHardLinksForCopyLocalIfPossible`.

- `dotnet build` of CCP.Core, CCP.Avalonia and every Linux test project listed in `.github/workflows/build.yml`: all 13 projects passed with 0 errors. After Core/head built, auxiliary test builds used `-p:BuildProjectReferences=false`; only their generated bin directories were removed afterward. Auxiliary suites were compiled, not run.
- `bash .github/scripts/core-guards.sh`: all Core guards passed (424 XAML type references checked).
- `dotnet test Tests/CCP.Core.Tests --no-build --no-restore`: 2821 passed, 6 existing skips, 0 failed; 2827 total. Five Vosk speech cases lack the model; one is the existing frame-budget skip. The 11 new clock/progress cases are included.
- `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~ProgramsSessionProgressTests|FullyQualifiedName~ProgramsRunLifecycleTests|FullyQualifiedName~ProgramsRunControlsTests|FullyQualifiedName~ProgramIgnitionTests'`: final run 8 passed, 0 failed, 0 skipped. The two new user-path cases failed before the strip was wired. Early fixture retries corrected the expected one-hour clock and restored the real elapsed value before resuming after an injected paused timestamp; no production behavior was relaxed to satisfy those expectations.
- The focused headless run also saved `/tmp/programs-progress-run.png`, inspected: First Week day 7, paused 2:00 / 1:00:00, a 3.33% bar, and the existing in-progress button. This is a real shell frame, not a mock.
- Optional WPF cross-target build (`Tests/ConditioningControlPanel.Tests`, `EnableWindowsTargeting=true`, `ValidateExecutableReferencesMatchSelfContained=false`, `UseSharedCompilation=false`): reached the explicit 180-second compile limit (exit 124) after compiling Core and emitting existing WPF warnings; no successful WPF build or runtime result is claimed.
- `CCP_USERDATA_DIR=/tmp/programs-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/programs-render`: passed (224/224 controls rendered). Inspected `ProgramsTabView.png` (browse state) as well as the real paused-session frame above.

Windows runtime and signed-in account integration are unavailable in this Linux headless environment. No new panic surface is introduced: the existing program-session surface ends the session and its normal refresh clears the strip and stops the sheen.
