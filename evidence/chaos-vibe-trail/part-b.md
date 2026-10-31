# Vibe trail part B

Base: f87695f9121ee369fbaea6dacc2e5291e2a35565. WPF files unchanged.
The base had no callers for trail EnsureCreated/Start/Stop or RaiseActive.
The bubble field had no VibePopping mode. The port now has an X11 user path.

## Specification and implementation

- Run birth: WPF Services/Chaos/ChaosModeService.cs:455; Avalonia Views/Chaos/ChaosRunHost.cs:144.
- Buzz, capstone, sound and banner: WPF ChaosModeService.cs:2696-2709; Avalonia ChaosRunHost.cs:691-699.
- One Afterglow, stop and toy glow: WPF ChaosModeService.cs:2925-2946,3017-3028; Avalonia ChaosRunHost.cs:640-665.
- Held/hover sweep and freeze exclusion: WPF Services/BubbleService.cs:263-270,4329-4341; Avalonia Views/Overlays/BubbleOverlay.cs:205-231,358.
- Sweep immunity: WPF BubbleService.cs:4652-4668; Avalonia BubbleOverlay.cs:216.
- Restack: WPF ChaosModeService.cs:745-762,816-827 and Services/Video/VideoService.cs:5810-5822; Avalonia ChaosRunHost.cs:485-488, MandatoryVideoOverlay.cs:270-279,345.
- Existing trail math, drawing and follow: WPF Chaos/ChaosVibeTrailOverlay.cs:24-29,147-257; Core Services/Chaos/ChaosVibeTrail.cs:14-66, Avalonia ChaosVibeTrailOverlay.cs:113-206.
- Existing teardown: WPF ChaosModeService.cs:3181,3234,3317; Avalonia ChaosRunHost.cs:586,738 and Windows/PanicSurfaces.cs:61.

All WPF paths above are under ConditioningControlPanel. All Avalonia paths are under CCP.Avalonia.
Feed copy adds two short keys in all ten Core language files. No Core UI dependencies added.

## Focused tests and fail-proofs

Tests/CCP.Avalonia.Tests/ChaosVibeTrailOverlayTests.cs:

- `Toy_path_starts_buzz_lingers_once_and_stops_then_teardown_closes`: real toy key and button entry points, precreation, follow timer, capstone, one Afterglow, expiry and cleanup. It also drives video opening and pointer routing with a captured delayed scheduler and verifies 60 ms and 50 ms kicks plus pin gating.
- `Sweep_pops_treats_snaps_lives_and_ignores_freeze_ambient_and_paused`: actual field and callbacks, hit-disc miss, held and hover paths, free snap, no double credit, pause and exclusions.

Mutation recipes, applied separately to the final tree and restored afterwards:

1. Remove `ChaosVibeTrailOverlay.Start()` from UseToy. The toy test must fail at `Assert.True(trail.IsFollowing)`.
2. Suppress the Afterglow branch. The toy test must fail at `Assert.True(BubbleOverlay.VibeHoverPops)` after four ticks.
3. Remove the opening or click delayed kick. The toy test must fail its scheduler count/delay assertions.
4. Make SweepVibe's initial guard always return. The sweep test must fail its `(popped, snapped) == (1, 1)` assertion.

Recipes 1-3 are compiled test coverage, not runtime-validated fail-proofs here: font creation aborts run-window setup before those assertions. Do not treat an environmental failure as a successful mutation kill.

Recipe 4 executed: mutant build succeeded, 0 errors; filtered test returned 1 with
`Assert.Equal() Failure` at the pop/snap count assertion (expected `(1, 1)`, actual `(0, 0)`).
The original source was restored in `finally` before rebuilding.

## Validation

SDK installed with dotnet-install --channel 10.0, release feed, version 10.0.403.
Commands use /tmp/ccp-dotnet/dotnet.

- `build CCP.Core`: 51 warnings, 0 errors (one transient concurrent-copy retry warning).
- `build CCP.Avalonia --no-restore -t:Rebuild`: 251 warnings, 0 errors (includes Core).
- `build Tests/CCP.Avalonia.Tests --no-restore`: 144 warnings, 0 errors.
- `test Tests/CCP.Core.Tests`: 4635 passed, 6 skipped, 0 failed.
- Affected Avalonia filter: ChaosVibeTrailOverlayTests, ChaosRunHostTests, BubbleOverlayTests, TestIsolationTests. Attempted 36: 18 passed, 18 failed. Inter/Segoe glyph typeface creation fails in this environment; run-window setup is caught by StartRun, producing subsequent null references. UI lifecycle/restacking: compiled, not runtime validated. Sweep test passed.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `python3 .github/scripts/avalonia_docs.py`: avalonia_docs self-check OK.

Remaining part B: desktop pointer on Windows and native Wayland; live composition,
input, and stacking verification. Full gameplay-band restacking belongs to the
broader host port; this change only wires the trail. Ledger status is wired on X11,
never verified. No live desktop or video was exercised.

After restoring the mutant: a full build of Tests/CCP.Avalonia.Tests and both
references passed with 395 warnings and 0 errors. The executable filter (sweep,
glow/fade, panic and TestIsolationTests) passed: 15 passed, 0 failed, 0 skipped.
The final pointer-availability guard skips the trail when no desktop-wide reader
is available; Windows/native Wayland pointer support is still not implemented.

A final parallel rebuild hit MSB4166 (child node exited prematurely). Retried
with `-m:1 -p:UseSharedCompilation=false`; no test or assertion was weakened.
