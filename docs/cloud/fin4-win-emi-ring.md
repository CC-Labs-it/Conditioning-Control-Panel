# fin4-win-emi-ring

- Keep `win-emi-ring` at **stub**, with one current row replacing repeated historical claims.
- Restore desktop click-away/Escape using the existing options Windows listener and X11 30 ms poll. Snapshot cards plus body in physical pixels; only cards take input. Close removes listeners before folding, and generation checks reject stale callbacks after reopen. Direct close also tears down.
- Successful option pins permanently latch the learned gesture; clearing pins does not revive teaching. The focused picker regression failed before this fix (`PinGistGot` false). Its isolated font bootstrap now uses the shipped fonts.
- Restore WPF fan translation amplitude 0.30 using the existing shared easing; fade/scale retain monotonic easing and WPF timings.
- Connect existing DtRH, Intake and Just Drop hosts to the shared card table, preserving Lab/pass probes and account availability. Existing game/intake panic registrations cover those hosts; the desk fan now has its own panic entry.
- Focused regression tests cover dismissal, body/card hot areas, stale callbacks, direct close, polling, input regions, easing and live pinned-door composition/account availability. Two dismissal tests failed against the old implementation (listener callback remained null).
- Imports: fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. All changes adapt the pinned WPF reference and reuse base code.

## WPF gap audit at base 73b1ccf0d793bb3fde6091c18a9fa6b5c692964a

Already implemented despite stale ledger prose: Core fan solver, card art/font, ring sfx, drag-first-move folding, external tab/rack scoring, ring/pick counters. FYP already has a card and host. Right-click pinning on the fan is dead in WPF: the declaration is retained but never raised; options own pinning.

Remaining reactions: `ringOpen`, `ringDismissed`, third-ignore reset moment, `ringPick` (including top-slot context), `lockedCardTapped`, `arcademyFromRing`, and `pinAdded`. WPF NoteRingOpened counts the open then consults EmiNudgeMachine and schedules pin teaching after 900 ms. Successful pins now latch the gist permanently, as in WPF. Open/ignore/usage counters already work here; reactions do not.

These need a real output host, not inert Fire calls. The head service explicitly has no moment bus/nudge lifecycle; its desk `Say` and `PlayChain` immediately invoke continuations. WPF chain player is 161 lines, bubble host 944, React 410, service 2128 before adapting rendering, offers, audio, safety holds and tests. A faithful output host plus those dependencies does not fit this unit's 1000 added-line budget. Core line engine/nudge logic already exists and must be reused by that port. No P122 promotion: these have live WPF callers.

Still hidden: Arcademy, Back Room, Goon (no registered GameWindow hosts) and the six-second timed spiral overlay (no equivalent head entry point). Their missing hosts/bridges/art are separate remaining feature work. DtRH, Intake and Just Drop already have hosts and are connected here. Native Wayland global hooks and sub-30 ms X11 presses remain platform limitations. The Windows input shim also follows cursor movement on a 30 ms timer while the ring is open.

## Verification limits

Commands used .NET 10.0.100 / Release with `DiffEngine_Disabled=true`:

- `dotnet build` Core, Avalonia and all 12 Linux test projects named in `.github/workflows/build.yml`: exit 0, zero errors (existing warnings).
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-restore --filter 'FullyQualifiedName~EmiRing|FullyQualifiedName~EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered'`: exit 0, 16 passed, zero skipped. Includes account-availability composition, panic and pin-gist persistence coverage.
- The pin-gist regression also failed before the fix at its learned-gesture assertion; an initial isolated attempt first encountered the font limitation, resolved for this test by using the shipped font bootstrap.
- Before the fix, the two initial dismissal regressions failed at `Assert.NotNull(press)` (exit 1).
- Full `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3952 passed, 5 Vosk tests skipped by the existing missing-model fixture, 1 failed (`DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`, expected Loops, got Clips). Isolated `--no-build --filter FullyQualifiedName~Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`: exit 0, 1 passed. `git diff --exit-code` confirms all Core and Core-test sources match the pinned base. No tests altered or disabled.
- Broader head filter including `EmiExternalOpenTests`: exit 1; 16 passed, one failed creating the existing shell PremiumSpark font. This matches the documented cloud font limitation. The final ring-only run above is green.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. `test-plan-coverage.sh` does not exist on this base; numbered manual step 300 covers this row.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors; compiled only, cannot run WPF on Linux.

Windows input hooks and real desktop click-through compile on Linux but cannot be exercised here; tests inject input and input-region sinks and use the assembly's isolated profile. Real WebView launches, account/network gates and multi-monitor DPI require manual testing.

## Assumptions

- Preserve the existing X11 polling fallback and its timing instead of introducing another global-input service.
- Use the existing gated Intake launch handler; locked card refusal follows WPF ShowGate's Lab prompt.
- Leave the row stub until the reaction output host and all missing doors are live. The line cap takes precedence over speculative partial host plumbing.
