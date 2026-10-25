# fin5-shell-possession: dependency analysis (NOOP)

- No runtime, test, or ledger changes. `shell-possession` stays `stub`: there is no
  complete user path to the WPF behavior on this base. No surface was added, so the
  existing panic registration (`PanicSurfaces.cs`, `possession`) is unchanged.
- Required base verified: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`.
  The snapshot initially pointed at a different commit; it was aligned before analysis.
- Read-only import source inspected: `parity/trunk` at
  `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. **No code imported**.
- Dependency: the Avalonia Possession director host and attribution/effect/scene/warden
  stack. Core already owns the rules and director; the missing feature is their
  head implementation, not a replacement Core engine or a missing Lockdown timer.
- Apply the task's dependency-only exception: do not ship a target registry with no
  complete consumer, or expose un-attributed transform effects as a finished haunt.

## Pinned WPF reach and remaining gaps

All WPF references below are at the required base commit. The single ledger row's
historical absence claims were checked against current source rather than treated
as current evidence. `MainWindow/MainWindow.Possession.cs` is the primary spec;
`Services/Possession/POSSESSION.md` describes the linked subsystem. Paths in this
section are relative to `ConditioningControlPanel` unless explicitly qualified.

1. **Stage and lifecycle** (`MainWindow.Possession.cs:51-155`): initialize from
   `MainWindow.Lab.cs:42`; on load attach the pointer and director; handle already
   loaded windows and repeated loads; detach on close. Add non-hit-testable,
   unclipped ghost and 28-pixel bottom rubble canvases at Z 9000/8999, recreating them
   idempotently. Avalonia's corresponding shell partial has no executable members.
2. **Target registry and cache** (`MainWindow.Possession.cs:194-442`): stable weak-keyed
   target objects preserve live flags and cooldowns; keys use name then path hash.
   Walk only the visible shell, skip overlay trees, prune at depth 64, respect
   inherited Exclude, then explicit Role before the name blocklist. A hand-tagged
   timer may wobble but its value must remain true. Block the exit phrase, Emergency
   Exit subtree, premium gate, rung/pips, XP odometer and companion dock chip.
   Cache rebuild floor 750 ms, maximum age 10 s, layout invalidation throttle 250 ms;
   background reads return the last snapshot and mark dirty without blocking UI.
3. **Inference, geometry and names** (`MainWindow.Possession.cs:325-624`): toggle before
   button, slider, combo, textbox/password, progress, scrollable viewer, image at
   least 48x48, title font at least 18, labels 3-40 characters. Stop at leaves except
   cards/scrolls; suppress scrollbar repeat-button chrome. Cards are the innermost
   rounded background border at least 60x60 containing an interactive descendant.
   Auto targets must be enabled/hit-testable, at least 8x8 in transformed window
   pixels and intersect the window. Keep 24 auto-labels nearest the pointer (largest
   first without a pointer). Name priority: explicit Name, Content, Text, ToolTip,
   automation name, role noun; normalize whitespace and accept 2-32 characters.
4. **Coordinates and usability** (`MainWindow.Possession.cs:626-704`): translate sibling
   layers through Viewbox scaling, including transformed sizes. Reject hidden,
   unloaded and minimized shell; pause new picks behind DTRH, Loom, Arcademy, Bureau,
   Goon, JustDrop, FYP, Intake, Emergency Exit or a Lock Card. Ordinary video does
   not pause the haunt. Existing ghosts finish. A failing gate returns false.
5. **Director startup and head bridge** (`App.xaml.cs:3409-3418`,
   `Services/Possession/PossessionWpfHead.cs`): instantiate Core director with the full
   effect catalog, attribution factory, scenes, UI dispatch, settings, bounds,
   visibility, parent walk, pointer, barks and shake; install warden, audio and
   remember; dispose on app exit (`App.xaml.cs:6395`). Avalonia has no such caller.
   Core already implements rung/intensity caps, 20-second first wait, cadence,
   concurrency, 45-second cooldown, proximity picks, restart reset, tripwire and
   reassembly cancellation. These rules are not missing Core work, but remain
   unreachable from this head. Lockdown settings and escape producers already exist.
6. **Attribution** (`Services/Possession/EmberAttribution.cs`, `Ghost.cs`): charge before
   motion (~400 ms ripple or 600 ms static photosafe/reduced-motion tint), following
   ember outline/tint, reference-counted cursor ring, edge pulses, cancellation and
   immediate release. Big-effect barks name the target; micro-tics remain silent.
   The four Avalonia transform effects have no attribution consumer. Wiring only
   those into the shell would violate the WPF clarity rule, not finish this row.
7. **Pointer and reactive adapter** (`PossessionPointer.cs`, `PossessionEvents.cs`):
   preview move/press, smoothed velocity (0.65 previous + 0.35 raw, 1-500 ms sample
   window), hovered/last-clicked target, idempotent attach/detach and bounded ancestor
   search. Card press breathes at R0; settings change types the nearest label at R1;
   Start/Stop hover dodges at R1; nav press drops a letter at R3. Consent settings
   are exempt; settings events marshal to UI and re-check haunting after dispatch.
   Core supplies the six-second reactive throttle and gates, but no head adapter exists.
8. **Effects** (`Effects/PossessionEffectCatalog*.cs`): only nudge, drift, breathe and
   wobble have Avalonia runtime implementations. Missing: crack, deletedialog,
   dissolve, dodge, dokidialog, drop, fall, ghostcursor, glitchportrait, glyphrot,
   melt, misroute, relabel, reorderdoors, retitle, rewrite, roomwarp, scrollhijack,
   slidercreep, stealcard, swap, toast, togglelie, typo, xpdrain. Preserve real values,
   commands and clickability, exact undo, photosafe/intensity gates, targetless
   effects, cancellation and per-effect behavior. Avatar portrait glitch/note APIs
   already exist in Avalonia; their existence is not a director/catalog connection.
9. **Scenes and warden** (`Scenes/*.cs`, `Warden.cs`): R2+ one-in-three scene selection,
   shared booking/cooldowns, rail sweep, where-you-are and the-count choreographies;
   charge each beat and name once. Warden availability/busy/cooldown checks, knock,
   stare, leave/note and return with cancellation/epoch protection. Avatar glide and
   note APIs can be reused, but no Avalonia warden or scene implementation exists.
10. **Adjacent lifecycle UI** (`MainWindow.Lab.cs` (readout and `ShowPossessionRulesIfFirstTime`)): rung readout/pips, first-run
    rules bark/card and cleanup; head hooks for remembered next-launch door charge
    and line, plus audio installation. Core Remember/Audio and the rules-card copy
    already exist; they need the live director/attribution path. This is dependency
    context, not an assertion that those separate ledger rows are finished here.

Missing effect behavior is not interchangeable with the four transform tics:

| Group | WPF behavior still without a head consumer |
| --- | --- |
| Text | typo, mod-voiced rewrite, glyph rot, falling title letters, Start/Stop “Stay” relabel, Full Doki retitle; restore original text |
| Geometry | predictive clickable dodge, swap visible button seats, hover melt, dissolve/ash, fall/drop/crack and rubble; restore transforms, opacity and hit testing |
| False readouts | ghost slider thumb, toggle appearance lie, XP/level lie; never mutate the actual setting or balance |
| Navigation | temporary misroute and reordered doors, with original routes/order restored |
| Room/companion | room tilt/sag/escape shrink, active-card theft/reassembly, portrait glitch, warden leave/note/return |
| Other surfaces | ghost cursor that never clicks, scroll hijack, crimson toasts, obviously themed fake crash/deletion dialogs (Full Doki gates) |

`Ghost.cs` additionally captures the real control into a DPI-aware bitmap, hides
and restores the source, crops tiles and tracks extra debris. Those drawing and
restore semantics need an Avalonia implementation; they are not supplied by Core's
live-ghost bookkeeping. A future port must publish immutable images across threads.

A follow-up slice plan is: (1) stage/registry plus director bridge and complete
attribution for the existing four effects; (2) pointer/reactive adapter and the
text/interaction catalog it invokes; (3) snapshot/debris effects and scenes;
(4) warden, room/dialog effects and full catalog parity. Each slice needs focused
headless tests, a real Lockdown entry path, panic cleanup and tester-plan coverage;
keep this row stub until all live WPF behavior is reached. This is a plan, not
implementation or verification evidence for any slice.

## Why the dependency cannot fit this unit

The WPF shell file alone is 704 lines. The still head-specific Possession service,
scene and effect sources total 9,370 lines (including comments and the four effects
already adapted). This is a scope measure, not a claim that all must be copied.
Even excluding all 25 missing effects, attribution (484), pointer (160), reactive
adapter (172), warden (490), and scene sources (441) total 1,747 reference lines,
before the shell, contract adaptation, startup integration or focused tests.
A correct port should reuse Core and avatar APIs, but cannot honestly finish all of
this in 1,000 added lines. A partial host would leave the same unported feature as
its consumer. The task explicitly permits a documentation-only dependency result.

Trunk is not a drop-in solution: its shell is 504 lines, `PossessionEffects.cs` 640,
and `PossessionReachEffects.cs` 435 (1,579 combined), with additional tree, text,
scene and toast files. It constructs `PossessionHost`, uses `PossessionDirector.Current`
and mutable `Scenes`, and constructs `PossessionRemember`; this base instead uses
`IPossessionHost`, `PossessionDirectorHead`, and static Remember hooks. Trunk's own
catalog is partial, and its hand-tag/safety restrictions differ from this WPF
reference's auto-tag walk. Importing it wholesale would overwrite base behavior and
still not complete parity. Future slices must adapt it and credit the pinned SHA.

P122 does not apply: the WPF host has live startup/load callers, the catalog creates
all effects, and the director consumes them. No dead-code promotion or render-only
claim is made. Keep the single existing ledger row unchanged at `stub`.

## Verification limits

No new behavior was introduced, so no new behavior test, tester step, or panic
surface is required. SDK: .NET 10.0.402. Commands ran in Release configuration.

- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.”
  The first attempt lacked permission to create guard scratch files; rerunning with
  temporary-file access passed. No source or guard baseline was changed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on this base.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`
  and sandboxed `CCP_USERDATA_DIR`: exit 1; 3,951 passed, 2 failed, 5 skipped,
  3,958 total. Failures: `BoardTilesTests.A_frame_fits_the_budget` (13.25 ms)
  and `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`
  (expected Loops, got Clips). Runtime source and tests are unchanged from base.
- Focused `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter
  'FullyQualifiedName~DtrhRemoteMediaTests|FullyQualifiedName~Possession'`:
  exit 0; 144 passed, none failed/skipped. The media assertion did not reproduce.
- Isolated `--filter 'FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget'`:
  exit 1; timing failure reproduced (16.57 ms). No thresholds were changed.
- Five speech tests skip through their existing missing-Vosk-model logic. The
  model is unavailable in this environment; `CCP_REQUIRE_VOSK` was not enabled.
- Relevant headless `PossessionRuntimeTests` were **not run**, per the supplied
  cloud font-creation limitation: their setup uses Skia without the Inter font
  initialization. Compilation results are listed below. No real desktop or user
  profile was used for tests. No Windows tests or manual WPF comparison ran.
- `git diff --cached --check` and the report privacy scan passed.

All required builds completed with exit 0 and zero errors (existing warnings
remain). Command for each project: `dotnet build <project> -c Release`. The WPF
test build additionally used `-p:EnableWindowsTargeting=true
-p:ValidateExecutableReferencesMatchSelfContained=false`; its referenced Windows
head was compiled but not run.

| Project | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |

## Assumptions

- The explicit dependency-only NOOP rule governs this oversized host stack; the
  1,000-line cap is not permission to weaken WPF attribution or label a partial
  effect deck wired.
- Existing Core tests cover portable rules; headless runtime tests cover the four
  existing effects, not the missing shell lifecycle or WPF visual parity.
