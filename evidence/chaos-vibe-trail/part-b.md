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

## Critical review follow-up

Both P2 findings accepted and fixed; no findings rejected.

- Enlarged hit disc: WPF BubbleService.cs:1616-1617,3277-3286,4338 and
  ChaosModeService.cs:371,378. Core AmbientBubbles.cs:61,164-173,244 now carries
  the spawn-sampled radius, including the 1.4 live magnet, 2.0 cap and precision
  pickup exclusions. Avalonia ChaosRunHost.cs:156-157 supplies the run settings;
  BubbleOverlay.cs:241-249,292-306 passes them to Core. The native input region
  encloses the same disc, including fractional edges (BubbleOverlay.cs:494-498).
- Sweep burst: WPF BubbleService.cs:4593,4669-4711. Core AmbientBubbles.cs:109-114
  now marks a sweep as popping without deflation. Held completion still sets
  Deflating, and its existing assertion is retained at ChaosLiveBubbleTests.cs:101.
- Focused tests: ChaosLiveBubbleTests.cs:30-56 covers radius bounds, clamping,
  precision exclusion and burst growth. ChaosVibeTrailOverlayTests.cs:164-168,187-226
  checks the corrected sweep animation and real field sweeps beyond the sprite,
  plus the enclosing input rectangle and spawn-time modifier sampling.
- Fail-proof 1 executed: temporarily set HitRadius to Size / 2. Core radius filter
  failed 3 cases, passed 2. Fail-proof 2 executed: temporarily restore
  Popping = Deflating = true in Snap. Burst filter failed 1, passed 0. Both mutants
  compiled and returned test exit 1; sources restored in finally. Restored Core
  ChaosLiveBubbleTests: 18 passed, 0 failed.
- Required builds: Core 50 warnings / 0 errors; Avalonia 201 / 0; Avalonia tests
  144 / 0. Full Core suite: 4641 passed, 6 skipped, 0 failed. Executable affected
  Avalonia subset: 16 passed, 0 failed. SDK: /tmp/ccp-dotnet/dotnet (10.0.403).
- No new UI copy. Both existing keys remain in all ten language files. No WPF
  changes. The prior font-blocked lifecycle/restacking and live platform gaps
  remain open; ledger stays wired on X11, never verified.

## PR 3506 CI repair

- Lifecycle failure: the fixture activated VibePopping but never unlocked it;
  ChaosUpgrades.cs:403-408 requires level >= 1. It now sets/restores level 1 and
  asserts IsBoonActive before starting the run. All lifecycle assertions remain.
- The bare test bootstrap also omitted bundled fonts. Using Program.BuildAvaloniaApp
  fixes standalone setup and supersedes the earlier font-blocked lifecycle result.
- Restacking used DispatcherTimer.RunOnce at 50 ms, triggering the unchanged frame
  timer ratchet. MandatoryVideoOverlay.Chaos.cs:12-29 now uses Task.Delay followed
  by UI-thread dispatch, matching WPF VideoService.cs:5812 and ChaosModeService.cs:823.
  The 60/50 ms kick timing and handled-click routing assertions remain intact.
- Executed fail-proofs: level 0 makes the lifecycle precondition fail; restoring
  DispatcherTimer.RunOnce makes NoNewFrameTimerLoop fail against the new partial.
  Both returned exit 1 and were restored. No test skips or ratchet exemptions added.
- Final build: 345 warnings, 0 errors. Filter: ChaosVibeTrailOverlayTests,
  AmbientBeatLoopsTests, ChaosRunHostTests, TestIsolationTests: 45 passed, 0 skipped.
  Core suite: 4641 passed, 6 existing skips, 0 failed. Guards and docs check passed.
