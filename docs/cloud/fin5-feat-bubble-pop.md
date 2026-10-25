# fin5-feat-bubble-pop

- Ported owned Rain / Spiral In / Mix selection and the existing Get it purchase path. Core owns the WPF motion rules/path; the head resolves its existing prize snapshots. Rain enters above the working area; Spiral In ends quietly at the core. Reduced halves v2 speed/turns; Off resolves Float up. Changes apply on next spawn.
- Ported paid-lucky gold glow and 16 particles, plus Sparkle Boost's eight pink particles, with WPF velocity/gravity/decay. Reused the field clock; no animated Effect or new frame loop.
- Ambient and Chaos fallback pops now resolve mod audio, including lucky chimes. Perk opt-out or a missing chime falls back to a normal pop; retained the volume curve and 0.35 chime scale.
- Added lifetime pop credit, Pop the Thought at 1000, one Sparkle Point and milestone toast every 100, and persistence every 50 pops. A repeated pop cannot duplicate credit. Quest/haptic paths remain in the head. Registered achievement popups for panic teardown.
- Replaced the repeated historical ledger prose with current evidence and an explicit Missing list; status remains stub. Added numbered WPF tester steps and focused Core/headless tests.
- Source adapted from WPF at base commit `560c0d0a10255b1d339121a96b3d3d80bb54c9d6`. Fetched `parity/trunk` read-only; no code imported from trunk, so there are no trunk import hashes to credit.

## Gap inventory at the pinned base

The cited WPF feature control and BubbleService have these behaviors absent in the starting head:

1. Owned motion picker, v2 pills/Get it, Rain and Spiral In paths, Mix, reduced-motion rules. Implemented here using the existing purchase host. Profile-sync ownership remains the separate `svc-prize-ownership` gap.
2. Lucky glow/particles and Sparkle Boost particles. Implemented; zero-payment lucky rolls remain ordinary pops.
3. Mod-resolved pop/chime, suppressed perk chime fallback and missing chime fallback. Implemented.
4. Lifetime achievement credit, 100-pop point/toast milestones and 50-pop flush. Implemented.
5. Ambient trigger roll, selected variant pool/chance, four-trigger concurrent cap, benign ambient reward plus payload, seven effect types, motion override/lifetime/tint/label and heavy payload suppression/defer with rechecks at fire time. Still missing. Existing Chaos specs/dispatch are run-owned; reusing BeginChaos would replace the current run callbacks and is not a correct ambient host. This needs a separate ambient dispatch path, not just a toggle hook.
6. Brain Drain's owned/enabled pool addition, special melt payload/art and toggle. Still missing; depends on the ambient trigger host.
7. Companion egg: after four seconds, one 10% roll per eligible effect bubble, claim/freeze, exclude video/htlink, glide/narrate, pop 1.5x louder, wait 2500 ms, return, cancel/release on teardown and 60-second cooldown. Still missing; requires the ambient effect host and avatar glide/claim choreography.
8. Natasha's red bubble: eligible-booking spawn roll, red cue, press/hold resist ring, early-release pop versus completed resist, cause-sensitive booking. Still missing; Core Chaster rules alone do not provide the bubble interaction surface.
9. Gaze-pop targets/dwell render scale and pop path filtered to the calibrated screen; card toggle and consent/calibration/running-camera hint. Still missing.
10. OS animation-effects/Gentle Visuals motion cap beyond the stored motion level; mod hero/side art repaint, localized persona egg hint parity. Still missing (current head uses static wash and the earlier literal persona hint).
11. Magnet was listed in the ledger but is retired in live WPF: `RollTriggerSpec` removes `MagnetBubble.VariantId` and `BuildTriggerSpec` returns null for it (BubbleService.cs:1161,1217 at the pinned base). Its old motion helper has no new ambient spawn path. This does not justify promoting the whole row: items 5–10 have live callers.

## Assumptions and scope

- Keep the row stub and prioritize independent visible motion/pop gaps. The remaining ambient effect dispatch, bubble input/gaze adapters and avatar choreography together require more than this unit's 1000-added-line allowance with meaningful tests. No dependency-only NOOP applies: independent gaps were implementable.
- Reuse counter snapshots and the existing purchase host, matching the current Flashes card; do not introduce a second ownership service.
- ponytail: translucent concentric gold discs approximate WPF's blur glow on the existing draw layer; avoid animated Effects. Glow honors the performance tier.
- The milestone toast reuses the existing Fluent achievement popup and localized milestone header. It is now covered by panic registration.

## Verification limits

Windows-specific overlay behavior cannot be executed in this Linux environment. No real desktop or real profile was used; headless tests use the suite's CCP_USERDATA_DIR sandbox. No PR was opened.

- SDK: .NET 10.0.402 (channel installation completed); no system packages installed.
- `dotnet build -c Release`: CCP.Core, CCP.Avalonia and all 13 test projects named by build.yml compiled with zero errors. The WPF test project used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; it compiled, but Windows code/tests were not run. Existing compiler warnings remain.
- CatalogueStartup's initial build exited 1 with MSB4166 (worker crash during overlapping builds). Serial retry `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests -c Release -m:1 -p:BuildProjectReferences=false` exited 0 with zero errors; referenced Core/head builds had succeeded.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release -m:1`: exit 0, 4303 passed, five existing Vosk tests skipped, 4308 total. Focused BubblePopParityTests: exit 0, 11 passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~BubblePopCompletionTests|FullyQualifiedName~EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered'`: exit 0, five passed (four behavior tests plus panic registry).
- Fail-before-fix proof: temporarily removed the repeated-pop guard and ran `--filter FullyQualifiedName~Repeated_pop_does_not_repeat_the_reward_signal`: exit 1, expected one signal, actual two. Restored the guard before the final green run.
- BubbleOverlayTests plus the initial three completion tests passed together once (six passed). Other combined runs reproduced the documented environment font failure (`Could not create glyphTypeface`, Inter) in the pre-existing BubbleOverlayTests bootstrap and were cancelled after the fatal font error; no tests were skipped/changed to hide this. The new class initializes the shipped fonts and passed independently. Full Avalonia suite was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed, including after final edits. Initial sandbox-only invocation could not create temporary files; rerun with workspace write permission passed.
- `test-plan-coverage.sh` is absent on this pinned base (attempt exited 127); numbered WPF/Rows steps were added manually.
- `git diff --check`: exit 0. No real-desktop rendering, audio playback, haptic hardware or Windows overlay runtime verification was attempted.
