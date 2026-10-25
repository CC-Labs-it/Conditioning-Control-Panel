# fin4-shell-profile-spiral

- Wire the profile doors and Spiral Room to the same live Core reader, including persisted migration withholding and mid-session block withdrawal. Repeated navigation re-reads the gates. Register browser teardown in the shared panic registry.
- Add the accepted-sync refresh behind WPF's HasSeenBlock gate and reuse ProfileAdopt for curve-first, clean-ledger profile-poll adoption. Dispatch head updates to the UI thread and fence queued adopts against account/token changes and window closure.
- Add focused Core and real headless profile-door regression coverage and numbered WPF-linked tester steps. Keep shell-profile-spiral **stub**, with an explicit Missing list.
- References: expected base `208c753919b400bd70aea174c62722c7f3425309`. Fetched read-only parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported or overwritten. Behavior adapted from the existing WPF reference at the expected base.

## Gap audit and assumptions

The two native doors already matched WPF's copy, own-card gate, menu closure, day/stage summary, cache reader, motion gate, polling and withdrawal behavior. The room still passed false for block presence and withholding; the reader had no progression-adopt callback, and accepted sync never refreshed it. Those gaps are addressed here.

Remaining live dependencies: the silent migration offer/apply host (including pending submit, ack ordering, stage-drip bookkeeping and live surface notifications), plus the complete map browser state/ready/failure protocol and navigation policy, room sound cues and companion greeting. These remain explicitly unported. The complete WPF host/adapter/service set and its behavioral tests exceed this unit's 1,000-added-line budget; this change prioritizes the real doors and shared-reader behavior without claiming map parity. Persisted withhold remains the source until live offers are hosted; no client-local rollout flag is added.

The row's old ceremony wording is stale. At the pinned base, WPF DescentMigrationService says offers restore silently (2026-10-06). DescentShowDirector's ignition section says no live ignition path remains, and the retained first-light method documents the capture rig as its only caller. Check with `git grep -n -E 'BeginSpiralFirstLight|BeginFirstLight|PlayFirstLight' 208c753919b400bd70aea174c62722c7f3425309 -- ConditioningControlPanel` and inspect the director's ignition/first-light section. No fullscreen ceremony, plate pulse or automatic reveal is introduced. This is not a P122 promotion: live dependencies remain, so the row stays stub.

No new window or desktop notification is created. The existing test sandboxes and fake HTTP handlers are retained. Windows runtime cannot be exercised on this Linux worker.

## Verification limits

Results are recorded below after validation. A failed pre-fix headless assertion at the new IsShowingSpiral check demonstrated the hard-coded false room gate before the shell wiring changed.

- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (424 XAML references checked).
- `dotnet test Tests/CCP.Core.Tests -c Release`, with `DiffEngine_Disabled=true`: final exit 0; 2,826 passed, 0 failed, 5 existing Vosk skips. No test was weakened or disabled. The model is unavailable on this worker.
- The initial parallel validation exhausted the worker's disk while copying generated test outputs. Removed only generated test bin directories, then rebuilt sequentially with `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true`, cleaning completed specialized test outputs between builds. The full Core suite was rerun after recovery. The new sync fixture also required the real signed-in precondition; that fixture was corrected before the final passing suite.
- Core and Avalonia builds: exit 0, 0 errors. All Linux test projects from build.yml compiled with 0 errors: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- Combined headless filter `ProfileSpiralTests|SpiralRoomShellTests|PanicSurfacesTests`: exit 1, 4 passed and 4 failed at shell construction on the known Segoe UI Black glyphTypeface environment limitation. Profile tests are also run in a separate testhost with their Program font setup; result follows below. The pre-fix profile regression reached and failed the new room-state assertion; the isolated post-fix run subsequently exposed and fixed the repeated-entry ordering gap.
- `git diff --check`: exit 0. This base has no `.github/scripts/test-plan-coverage.sh`; numbered coverage is supplied in docs/avalonia-test-plan.md.
- No Windows UI/runtime tests, real-account/network integration, full browser map, ceremony capture-rig reveal, render-all, or full Avalonia test suite was run. The row remains stub; no visual verification claim is made.
- Final isolated `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~ProfileSpiralTests` (with hardlink copy property and DiffEngine_Disabled): exit 0; 2 passed, 0 failed, 0 skipped. This rebuilt the final Avalonia source with 0 errors. Moving the room's entry refresh before unrelated decoration fixed the observed same-tab regression.
- Final WPF test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, Build succeeded, 2 warnings, 0 errors. Both output and additional-content hardlink properties were used on the recovered retry. Windows code compiled but was not run.
