- Tutorial pointer tests now wait for the composition batch containing the live host to render, using the existing content hit-test barrier. Draining dispatcher jobs alone does not publish a pointer hit-test scene. Both tests use the target's measured centre and assert that the rendered scene contains it before injecting input.
- ContentHitTestTests now initializes through the production app builder. Its previous bare AppBuilder omitted the bundled Inter registration and font mappings; when this class initialized the process first, subsequent classes reused that incomplete Application.Current. The initial class run reproduced glyphTypeface failures; the corrected initialization passes both classes.
- Extracted the existing render barrier into HeadlessRender and close the content-test shell in finally, including on assertion failure. Assertions, input routing, tutorial range checks, and product behavior are retained.
- ContentHitTestTests now uses the existing RunsAlone collection, matching TutorialAdvanceTests. Although normal assembly runs disable parallelization, explicitly enabling parallel collections reproduced a null composition visual and dispatcher ownership failure when another class ran lifecycle cleanup during the render barrier. This is an isolation boundary around shared process state, not a timer or assertion change.
- Import source: fetched parity/trunk read-only at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no trunk code imported, so no imported commit credits apply.

## Verification limits

- .NET 10.0.402; DiffEngine_Disabled=true for test runs.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors.
- Built every Linux test project named in `.github/workflows/build.yml`: CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, and Core.Tests. All exit 0, 0 errors. Generated per-suite binaries were cleaned after compilation to conserve disk.
- WPF test project cross-target build with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors. Initial runtime-file copying filled temporary storage; retrying with output on the larger filesystem succeeded. WPF tests were not run.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3953 passed, 5 existing Vosk skips, 0 failed.
- Initial target full-class run after font/render changes: exit 0; 9 passed, 0 failed.
- Unchanged-base source reproduction in separate output: exit 1; all three targets failed during Inter glyphTypeface creation. Original tutorial source alone passed once font initialization was correct, including with a slower natural render clock; the stale-render path is intermittent, not a deterministic failure reproduced here.
- Forced-parallel full-class reproduction before collection isolation (also included AvaloniaTestDispatcherTests): exit 1; 10 passed, ContentHitTestTests failed because its composition visual disappeared during dispatcher reentrancy.
- Deliberate broken-behavior run in separate output: exit 1; all three targets failed, 0 skipped. Temporarily dropped button/slider tutorial release handling and disabled content-panel hit testing. Tutorial assertions caught the missing advance; content assertions reported wrong hits and pressed False. All temporary edits were restored; no product change remains.
- Final full-class run with collection parallelization enabled and MaxParallelThreads=2: exit 0; 11 passed, 0 failed (both entire target classes plus AvaloniaTestDispatcherTests). Tutorial and content tests use the existing exclusive collection.
- Repeat command: `for i in $(seq 1 20); do dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter "FullyQualifiedName~SliderAdvancesOnlyOnReleaseInsideRequestedRange|FullyQualifiedName~PointerReleaseAdvancesEvenWhenTheButtonNeverRaisesClick|FullyQualifiedName~ContentButtonsReceiveThePointerAtTheirCentre"; done`. All 20 invocations exited 0, each with 3 passed, 0 failed, 0 skipped: 20/20 per target, 60/60 total. This loop verified the font/render fixes under the assembly's normal serialization; the final parallel run additionally verified the collection attribute.
- `.github/scripts/test-plan-coverage.sh` is absent on this base (attempt exit 127). No ledger rows were added or promoted.

- Windows runtime behavior cannot be executed on Linux.
- Core's existing five Vosk tests require the optional downloaded speech model; this environment has no model. No test skips were added or changed.

Assumptions: test infrastructure is the unit's scope, so no parity ledger row is promoted and no WPF behavior changes. Use the production font configuration rather than introduce a test-only fallback.
