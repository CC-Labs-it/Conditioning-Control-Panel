# New enhancement interactive tutorials

- Base verified at `624a67a648bff22af6985815b7ffad948e242541`; fallback clone used because the supplied snapshot was read-only. Only the task branch is modified.
- WPF inventory: three Part 1 launches with media selection; HT known-video lookup and seen flag; skip/replace prior tour; picker overlay hide/restore; cancel/close subscription cleanup; empty-source warning; success-only matching Part 2; editor loaded event, consume-once 800 ms continuation and closing cancellation. The base already had media/picker/known-video behavior, Core tutorial seeding/events, WPF-derived steps, native spotlight input regions and automatic advancement. The dialog launcher, lifecycle and continuation were still no-ops.
- Implemented remaining dialog behavior using the existing head tutorial service and Core event bus. Shared message dialog replaces the inline validation error. Part 1 detaches on successful close; Part 2 gets a fresh overlay after layout settles.
- Wired the editor's existing effect/rule event sites and successful-save path. Metadata prep now executes before measuring a spotlight. Inspector target names are assigned before visual attachment. Closing a host closes its overlay; panic cancels pending editor continuation through the surface registry.
- Added focused headless tests for all three launch/handoff mappings, invalid Create, Cancel/X, picker cancellation/failure restore, delayed start/close/panic, inspector edits, effect/rule advancement and successful save. Extended the tour target audit and panic registration test. Added numbered WPF comparison steps and promoted the single parity row to wired (not verified).
- Import credit: fetched `origin/parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` read-only; no files imported. Reused the base overlay advancement adaptation, already credited there to trunk `e5b3976dd6ab2bf215b89c6e0719e7489dd153ed`.

## Verification limits

- Full Core suite: 4,120 passed, 5 existing Vosk skips, zero failures (`DiffEngine_Disabled=true`). Core guards passed.
- Builds: CCP.Core, CCP.Avalonia and all Linux test projects listed by the workflow passed with zero errors. The WPF app/test cross-target build also passed with zero errors using the required targeting flags. Generated WPF outputs shared dependencies with hard links to fit disk capacity. The first overlapping Avalonia build encountered an output-file collision; sequential compilation replaced that attempt. An initial broad sweep exhausted disk copying native dependencies; generated test outputs were removed and remaining builds retried individually.
- Initial bare-builder dialog run: 3 failed before behavior assertions on glyph typefaces (Segoe UI Black / Inter). The touched tests now use the production app builder, including bundled fonts, and disable ambient loops while draining dispatcher jobs. An isolated run reached behavior assertions: 17 passed and one caught the closed-overlay restore race; the implementation was then guarded against closed windows. The initial combined tutorial run hit the same initialization failure; the related input-region harness now uses the production font setup too. No tests were weakened, skipped or deleted.
- Isolated final new-enhancement run: 18 passed, zero failures. Inspector regression also run with the original base RuleEditor file: failed because the target TextBox was absent; restored fix passed in the 18-test run. Closed-overlay restore race: 17 passed/1 failed before the lifecycle guard, then 18 passed.
- Final combined headless suite: **41 passed, zero failures**, exit 0, with `DiffEngine_Disabled=true`. Filter: `NewEnhancement`, `TutorialAdvanceTests`, `TutorialInputRegionTests`, `TutorialTourTests`, `PanicSurfacesTests`. This covers the reused automatic advancement and native-region geometry as well as the new lifecycle tests. `test-plan-coverage.sh` is absent on this base; numbered row-linked steps were added in the established document.
- Native Windows input-region code is compiled, not run. Native picker, desktop overlay hit testing, Windows runtime and render-all visual inspection are not exercised in this headless environment.

Build commands (all final exit codes 0; existing warnings remain):

- `dotnet build -c Release`: CCP.Core, CCP.Avalonia; test projects CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` (also builds the WPF application).
- `dotnet test Tests/CCP.Core.Tests -c Release`; `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter <classes above>`; `bash .github/scripts/core-guards.sh`; `git diff --check`.

## Assumptions

- This row owns dialog-launched interactive tutorials and their editor handoff; the separate editor help/first-run intro remains outside this unit.
- Closing Part 1 immediately on success preserves the split-overlay flow while avoiding stale subscriptions during the 800 ms gap. General cross-window tutorial retargeting remains a separate overlay concern.
- The shared message dialog supplies the WPF validation text/title and OK behavior; its existing head styling replaces the native WPF warning icon.
