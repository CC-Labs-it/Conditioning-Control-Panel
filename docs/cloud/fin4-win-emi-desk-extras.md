# fin4-win-emi-desk-extras

- Base verified at `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`; the initial snapshot differed and its Git directory was read-only, so the authorized independent clone fallback was used.
- Adapted WPF `Services/EmiDesk/EmiChainPlayer.cs` at that base into Core `EmiChainPlayback`: supplied clock and dispatcher callback, no UI references, generation check against already queued cancelled ticks. The frozen WPF player remains unchanged.
- Wired the Avalonia chain hooks and real `ChainLive` gate. Land now holds the celebration body through the canonical 600/160 ms frames; dizzy uses all canonical frames and holds, then resets face/pose and restarts idle. Removed the separate land-face timer. Existing squash curves, cues and fling accounting remain unchanged.
- Render inspection exposed the existing SetPose name-only stub. Added WPF BodyPath/FrameKey lookup, cached Avalonia bitmaps, placeholder suppression and disposal at window close; missing art keeps the previous image. Headless landing assertions now require actual bitmap changes.
- Registered chain cancellation with the panic surface registry; no new window is introduced. Added Core timing/cancellation tests and headless pointer-drag pose/sequence assertions.
- Replaced the repeated historical ledger text with one honest stub row and numbered manual tester steps.
- `parity/trunk` fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Adaptation credit is the pinned WPF base above.

## WPF gap inventory and scope

1. Presentation already has a real FirstShowService caller and position/bob/turn/pose/face/entrance/restoration implementation. WPF additionally stops alive, ask, channel, hold and voice hosts and refreshes outfit on entry, restoring alive on exit. Those hosts remain unported on this head; this unit does not claim their cleanup.
2. The land celebration pose and thrown dizzy chain were absent. Both are implemented here, including busy-chain gating for gentle drops, final rest-frame hold, cancellation, and settlement.
3. WPF fires `dragged` on pickup, `dropped` after land (or immediately when another chain is busy), and `flung` after dizzy, carrying monitor-third `zoneRow` and thresholds `flings20`/`flings50`. These words remain absent: `EmiDeskService.Fire`/`Speak`, `SpeakLine`, and the speech/bubble host are unported. The reference Bubble partial alone is 944 lines and includes placement, voice, offers, holds and acknowledgement; the service is 2128 lines. Porting that host with its required behavior tests alongside this player exceeds this unit's 1000-added-line budget. Core's engine already exists; no duplicate engine or event bus was invented.
4. Tube is unreachable in WPF. Pinned evidence command: `git grep -n -E 'SetTubeEmiVisible|SuspendForTube|ResumeFromTube' 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a -- ConditioningControlPanel`. Results: Tube window partial contains only the two definitions; service Tube partial contains the SetTubeEmiVisible definition, its dispatcher self-call, and the two window calls. There is no external caller. No speculative tube route was added. P122 cannot promote the whole row because live moment-line dependencies remain (and render-all must still be checked).

## Verification limits

- `dotnet build` Release: Core, Avalonia and all 12 Linux test projects named by build.yml passed (exit 0, zero errors). After the final body-art change, Avalonia and its test project were rebuilt again: zero errors.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3955 passed, 5 existing Vosk tests skipped, 3960 total. Vosk download is unavailable in this environment; no test was weakened.
- Core playback focused tests: 2 passed. Removing body-pose delivery made LandHoldsPoseAndFinishesOnlyAfterLastFrame fail (expected celebration, actual happy face); restoring it returned both tests to green.
- Final headless filter `FullyQualifiedName~EmiDeskTossTests|FullyQualifiedName~PanicSurfacesTests`: exit 0; 7 passed. Isolated toss also passed (1/1) with actual cached body PNG assertions.
- Earlier broader desk/panic attempts encountered missing-font exceptions (Consolas/Inter/Segoe UI Black); one rerun stopped progressing after disk exhaustion and was terminated. They are not claimed green. Initial concurrent builds also caused a missing-precompiled-XAML startup failure; the sequential final build and focused run passed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. test-plan-coverage.sh is absent from this base (attempt returned 127); numbered row coverage was added manually.
- Sandboxed `--render-all`: 203 images produced before disk exhaustion (exit 134), so full render-all is not claimed. Final isolated desk render exited 0; inspected image shows the real idle body instead of the placeholder.
- Windows desktop behavior cannot run on Linux. No real desktop or real profile was used; the headless test module initializer supplies a sandbox profile.
- Initial broad build attempts hit the environment's disk cap while copying content. Generated test outputs were removed between project builds; every Linux project subsequently built successfully. Final `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 2 warnings, zero errors after reclaiming generated headless test binaries. WPF tests were compiled, not executed on Linux.

## Assumptions

- Keep the row stub rather than misrepresent the remaining speech and presentation dependencies as wired.
- Reuse the existing pose table, chain hooks, idle clock and panic registry. Body moves, particles, voice and bubble hooks that still lack implementations remain outside this row's completed landing sequences (land/dizzy request none of those effects).
- Avalonia cannot reassign an already shown window's owner; the existing presentation raise-above-stage behavior is retained.
