- Fix the Chaster pop fixture's mixed clocks: the shell's 400 ms coalescing/throttle clock now uses the same stepped time as pop lifetimes, restored in `finally`. Previously a slow dispatcher drain could exceed 400 ms while the pop remained frozen, causing `Live.Single()` to see multiple figures.
- Preserve the existing identity, sum, lifetime and fallback assertions; additionally assert a separate figure after crossing the stepped coalescing deadline. No product timing, tolerance or parallelism changes.
- Start QuestsTabLiveTests from a fresh AppSettings object rather than loading unspecified reward/reroll inputs from the shared profile; seal its SettingsService during cleanup. This removes a fixture dependency; the reported gate failure cannot be reproduced through the font blocker here.
- No trunk code imported. Inspected read-only parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.

## Verification limits

- Required base verified: `39260f198fad67d90bc988c63a00480830e5fefc`; clean checkout before changes.
- .NET 10.0.100 installed explicitly after channel discovery returned HTTP 403.
- Release builds: CCP.Core, CCP.Avalonia and all 12 Linux test projects referenced by build.yml succeeded with zero errors.
- Core suite: 3953 passed, 5 existing Vosk-dependent skips, 0 failed (3958 total). No tests changed or skipped to obtain this result.
- Core guards passed. test-plan-coverage.sh is absent from this base; no ledger rows changed.
- Baseline Avalonia run (ChasterBookedPopTests and QuestsTabLiveTests): 1 passed, 5 failed creating fonts before behaviour assertions.
- Final complete-class Avalonia run (ChasterBookedPopTests, QuestsTabLiveTests, ModChoiceTests): 1 passed, 17 failed creating fonts before behaviour assertions. Missing typefaces include Segoe UI Black and Inter. Tests compile with zero errors.
- The required 20 UI repetitions and UI mutation proof remain blocked by those font failures. No claim of a passing UI reliability gate. The existing assembly-wide DisableTestParallelization remains intact, so the complete-class invocation uses the repository's existing scheduling policy.
- MakeHerYoursCommunityTests.CommunityLinkFromTheShellBrowsesCancelsInstallsAndReadoutUsesInstalledName, WorkshopCommunityCellTests.CommunityCellIsReachableThroughTheCompanionWorkshopDrawer, and ModChoiceTests.CompanionChipPicksAreRememberedAndRestoredByTheHeaderModSwitch are absent from both the required base and inspected trunk. Their root causes and fixes remain outstanding; do not infer fixes from other tests in the same area.
- Supporting Core coalescing stress: BookedFlashPlanTests passed 20/20 invocations (18 tests each; 360 passes). These are supporting tests, not the requested UI repetitions.
- Supporting negative control: temporarily setting BookedFlashPlan.CoalesceMs to zero produced 5 failures and 13 passes (exit 1). The original source was restored immediately; no product change is committed.
- WPF test project cross-compiled successfully with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false: 557 warnings, zero errors. Windows behaviour was compiled but cannot run on this Linux host.

## Assumptions

- Keep the exact required base despite the three missing test methods; do not import unrelated companion features to manufacture them.
- Fix the demonstrated mixed-clock fixture dependency while retaining WPF-equal product behaviour. Treat quest settings isolation as unverified hardening, not a confirmed reproduction of the reported CI failure.
