- Base verified: 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a (local checkout branch was work). Unit branch: avalonia-port/cloud-fin4-views-tab-lockdown.
- Restore mod-aware Lockdown side art and matching hero strip through existing ModArt; restore the built-in Emergency Exit face plate. Reuse HoverPop on the image.
- Wire the existing Core LockdownPulse curve through the shared FrameClock. Stop while hidden, detached, minimized or ambient motion is disabled; use a sibling BoxShadow border.
- Match WPF Emergency Exit press timing/easing: 60 ms quadratic-out down, 140 ms quadratic-in-out home.
- Add focused artwork, pulse lifecycle and real pointer press tests; retain all existing Lockdown tests. Artwork regression failed before implementation with Assert.NotNull at the absent image.
- Replace duplicated, stale ledger text (including private evidence paths) with one honest stub row and numbered tester steps.
- Imports: none. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; existing base implementations win.

## WPF comparison and remaining dependencies

Reference is pinned to the base SHA above. Read LockdownTabView.xaml/.cs, MainWindow.Lab.cs, MainWindow.Animations.cs, LockdownDoseKeeper.cs and both heads' Possession hosts.

Gaps found before implementation:
1. Side image, hero strip linked to the same mod-resolved source, side-image hover, and the Emergency Exit inset art were absent. Closed here.
2. Side border/glow's 1.5-second linear auto-reverse pulse was absent. Closed here using the existing Core curve, including WPF motion/performance gates.
3. Emergency Exit used 140 ms in both directions instead of WPF's separate 60/140 ms easing. Closed here.
4. Possession haunt, five-rung readout (Settle seeded at activation, updates on rung changes, reset on restart, hide/reset/unsubscribe on exit) and first-run rules remain absent. Rules must use PresentOrInbox, spend the intro flag only when opened, and bark immediately. Core has PossessionDirector; this head has only four isolated effects and a runtime. MainShellWindow.Possession.cs is still a stub: no live IPossessionHost, target walk, attribution/layers, pointer/events, Warden, scenes or director lifecycle. Showing fake rung progress would misrepresent that state.
5. Dose Keeper remains absent: two-second kickoff, session-lock stand-down, panic aftermath engine hold, engine restart grace, empty-dose edge tripwire, asset-aware feature conscription/escalation, bark/edge pulse, restoration of only conscripted changes and crash recovery. WPF's 585-line service depends on shell feature controls and Possession; there is no Core/Avalonia equivalent to reuse.
6. Emergency Exit games differ by the existing explicit decision in avalonia-decisions.md: keep the phrase notice, safety hold and tripwire. No games host is added.

The ledger's system-key hook and CC Labs greying gaps were stale: base MainShellWindow.Lockdown.InitializeLockdownGreys already calls Win32Input.ApplyLockdown and disables BtnBackToLauncher. They require Windows runtime verification, not duplicate implementations.

The two remaining hosts cannot both fit this unit's 1000-added-line cap: the WPF shell target host alone is 673 lines and Dose Keeper is 585, before adaptation, lifecycle/effects and focused tests. Prioritize the visible gaps above and keep the row stub. This is a partial implementation, not a NOOP or a verified parity claim.

No new window/overlay surface is introduced; the existing Lockdown panic/refusal registration remains in place. The side glow belongs to the existing tab and stops on detach/hide. Lockdown intentionally refuses panic per the existing safety policy.

## Verification limits

SDK used: .NET 10.0.402 (10.0.100 also installed while the channel lookup retried).

- `dotnet build CCP.Core -c Release` and `dotnet build CCP.Avalonia -c Release`: exit 0, zero errors.
- All 12 Linux test projects named in build.yml built with `dotnet build <project> -c Release`: exit 0, zero errors each. Includes Core, Core.Settings, Core.Content; Avalonia main, CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler and Snapshot.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 554 warnings, zero errors. Compiled, never executed.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3953 passed, 5 existing Vosk-dependent skips, 0 failed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~LockdownTests|FullyQualifiedName~LockdownVeilTests'` (also repeated after final changes with `--no-build`): exit 1; 4 passed, 14 failed at environment font creation (10 Segoe UI Black, 4 Inter Bold). Window-based pulse and pointer assertions could not run through to completion. They compile; supervisor/GitHub execution is still required.
- Isolated `--filter FullyQualifiedName~LockdownArtworkUsesTheShippedSideHeroAndEmergencyPlates`: exit 0, 1 passed. Before implementation: exit 1, absent image assertion failed. Final regression also checks hover opt-in and WPF's 10px margin.
- `git diff --check`: exit 0. Test-plan coverage script does not exist on this base; numbered manual steps added regardless.
- Full Avalonia suite, Windows runtime hooks, desktop render-all, and Windows tests were not run.
- All test runs used `DiffEngine_Disabled=true`; existing test assembly isolation supplies sandbox profiles. No new surface, desktop notifications or real-profile writes.

An initial overlapping build/test invocation hit an Avalonia PDB file lock; subsequent builds were serialized and all finished with zero errors. Initial compile errors in the new code/tests were corrected before the final builds.

Assumptions: retain the explicit phrase-notice decision for Emergency Exit; do not activate a partial Possession director or promise Dose behavior without their full hosts. A faithful Dose port needs shared logic extraction, an Avalonia lifecycle seam, recovery and tests in addition to its 585-line reference; it does not fit the remaining budget alongside the UI fixes and full Possession host.
Windows hooks compile in the Avalonia head but cannot execute on this Linux worker. WPF desktop behavior is read as a reference, never run here.
Vosk-dependent speech tests require a model unavailable in this environment (known download HTTP 403). No tests are weakened or skipped by this change.
