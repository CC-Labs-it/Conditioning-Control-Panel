- Base verified at a65dbbe2f4935a4797b32efd87513054dd39e1d2; independent snapshot, initially a different commit, clean after checkout.
- Implemented VibePopping held sweep, capstone hover, one Afterglow per activation, paid live snaps/treat pops, effect banner/trail, cooldown-window glow and teardown.
- Implemented Snap Field's paid live defuses, capstone paid treat clearing, one-second active glow, flash, sound and event text.
- Award first_play once after an implemented effect fires; denied/paused/inert uses do not earn it.
- Apply WPF tooltip chrome and 250 ms delay to the hero button. Reuse existing tooltip content/order.
- Add focused tests for normal/capstone snap and sweep, pause denial, first-play idempotence, effect timing, afterglow, cleanup and tooltip composition.
- Replace the repeated stale ledger history with one honest stub row. FALL IN, the bubble field and run payout are already reachable on the pinned base.
- Source: frozen WPF ChaosToyButtonWindow.cs, ChaosTips.cs, ChaosModeService.cs UseToy/TickActiveToys at base a65dbbe2f4935a4797b32efd87513054dd39e1d2. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no trunk code imported.

## WPF gap inventory and remaining scope

At this base the hero visuals, equipment construction, click/key gate, cooldown/charge arithmetic, countdown and cleanup existed. WPF effects, first_play, tooltip chrome and mandatory-video stacking were missing. The ledger's no-run/no-field/no-payout claims were stale.

This unit closes sweep/snap, their timing and first_play/chrome. It remains stub:
- Freeze Trigger needs freeze field motion/fuse hold separate from input lock, shudder, duration multiplier, icy burst/edge hold and release cue, plus capstone defuse.
- Porn DVD has an overlay/model but no contact pop, Spanker and rabbit-split integration with the field; no live run toy caller added here.
- Rabbit Caller needs next-press aiming (ignore arming press, hold during pause), scatter, ten-second storm at 1.25-second cadence and darter motion/catch effects.
- E-Stim needs charged good-pop propagation, staggered arcs, capstone chains, Overload multiplier and spent-charge UI/cues.
- Mandatory run video is not integrated, so WPF's live raise-above-video callers cannot yet be reproduced.
- Desktop key/pointer behavior is not live-verified. The existing X11 pointer seam cannot read native Wayland or Windows desktop input. No new native API is introduced.

## Assumptions

- Keep the ledger stub while any user-visible toy behavior is absent; do not treat existing decorative overlays as working toy mechanics.
- Use the existing X11 pointer reader and field coordinate convention for sweep, with an injectable reader for tests.
- Award first_play only for implemented effects; preserve existing inert toy gate bookkeeping until the remaining mechanics land.
- No new surface: existing chaos and chaos-vibe-trail panic registrations cover the host, buttons, sweep timer and trail.

## Verification limits

- .NET 10.0.100 installed; channel-based discovery failed with HTTP 403, pinned SDK installation succeeded.
- Release builds: CCP.Core, CCP.Avalonia, and all twelve Linux-compatible test projects referenced by build.yml succeeded (exit 0, zero errors). The WPF reference test project also built successfully with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false (exit 0; existing warnings).
- Core guards: exit 0, "All Core guards passed."
- Core tests with DiffEngine_Disabled=true: exit 0; 3953 passed, 5 existing Vosk-dependent skips, zero failed. No tests were weakened or removed; Vosk model access remains unavailable in this environment.
- Final headless test compilation: exit 0, zero errors using -m:1 --disable-build-servers after a parallel MSBuild child-node crash (MSB4166, exit 1). Intended filter: FullyQualifiedName~ChaosToyEffectsTests|FullyQualifiedName~ChaosRunHostTests.
- Avalonia tests were not executed, per the task's measured cloud font-creation limitation (font-test lane absent). New regressions have not been fail-proven here and need the supervisor's GitHub run.
- No Windows native code, real desktop keys, native Wayland pointer, compositor stacking, Vosk recognition or render-all was run. Windows code was compiled only.
- git diff --check passed. Added lines remain below the 1000-line cap.
