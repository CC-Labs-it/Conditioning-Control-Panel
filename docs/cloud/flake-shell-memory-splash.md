# Shell memory, splash and resize test reliability

- Replace three fixed GC/render passes with a bounded condition pump that collects until the actual weak references die. Keep all six shells, active and parked loops, hide/show/close coverage, and all liveness assertions. Deferred compositor/frame/finalizer callbacks need UI turns; three synchronous passes are not a completion fence.
- Await actual startup completion and splash closure instead of counting render ticks. `ForceRenderTimerTick(16)` requests sixteen frames; it does not advance the animation clock sixteen milliseconds. The old tight loop could finish before the real 200 ms fade. Check the second startup status and bar as well as the first and final states.
- Explicitly complete layout around dispatcher work before inspecting canvas coordinates and resize hit zones. `RunJobs` alone does not guarantee measure/arrange has completed. Close both resize-test shells in `finally`, including assertion failures.
- Reuse the existing `AmbientBeatLoopsTests.Setup` portable font configuration (bundled fonts and Windows-family mappings) rather than making these classes depend on which class initialized the shared application first. Keep real Skia rendering.
- Use the existing `RunsAloneCollection` for these three classes. Explicit collection-parallel execution reproduced a fatal dispatcher thread-ownership error: `AvaloniaTestDispatcher.RunAsync` can start multiple bootstrap threads before its background thread publishes `_dispatcher`. In addition, `TestIsolation.Current` is one process-wide snapshot; async lifecycle turns must not overlap another class's Before/After. The assembly default disables parallelism, but runner overrides must preserve this contract. No product behavior changes, assertion weakening, tolerance changes or skips.
- Reference branch fetched read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Verification limits

- Required base verified exactly; the published snapshot had a different commit and a read-only Git directory, so the authorized writable fallback clone was used.
- .NET SDK 10.0.402. Core, Avalonia and all Linux workflow test projects built with zero errors. WPF tests cross-built with Windows targeting and executable-reference validation disabled, plus MSBuild hardlinks for native/content copies to fit the available disk. Windows binaries were not run on Linux.
- `bash .github/scripts/core-guards.sh`: passed. This base has no `test-plan-coverage.sh`; no parity ledger rows changed.
- `dotnet test Tests/CCP.Core.Tests`: 3952 passed, 6 pre-existing skips, 0 failures. No skips were added or modified.
- Original splash test with portable fonts: one run passed; the intermittent failure was not reproduced in that single baseline run.
- Negative control: temporarily replace product `SplashScreen.NextFrame` with `Task.CompletedTask`. The fixed startup test failed (exit 1): expected only `settings`, observed `settings`, `audio`, `shell built`. Product source restored before rebuilding.
- Parallel negative control: all three full classes with `-parallel collections -maxThreads 4` failed fatally with dispatcher thread-ownership errors before applying `RunsAloneCollection`.
- Stress results: all four target tests passed 20/20 each (80 passed, 0 failed, 0 skipped), with seeds 1 through 20. Final full-class parallel-runner check (seed 27): 21 passed, 0 errors, 0 failures, 0 skips, exit 0 (117.225 s). Stress uses four independent processes, seeds 1 through 20, each invoking `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter "FullyQualifiedName~ClosedShellsReleaseMemory|FullyQualifiedName~AnAmbientLoopDoesNotKeepItsClosedWindow|FullyQualifiedName~EachStepPaintsBeforeTheNextThenTheShellTakesOver|FullyQualifiedName~LayoutFollowsThePolicyAndEdgesStayAtTheWindowEdge"`, with `DiffEngine_Disabled=true`. The full-class check uses the xUnit executable with `-parallel collections -maxThreads 4` and all three `-class` filters. Every class uses the existing exclusive collection; no process-state snapshots overlap.
- Initial build attempts exhausted disk while duplicating native dependencies. Generated output cleanup and sequential builds resolved this; no project/dependency changes were needed. Interrupted builds also left stale compiled XAML; a clean head rebuild resolved those setup failures before the final stress runs.

## Assumptions

- These lifecycle tests should assert eventual collection/closure, rather than a particular number of compositor or finalizer turns. The ten-second monotonic deadline is a failure guard, not a requested sleep or a relaxed value assertion.
- Prefer the existing exclusive collection over redesigning the shared dispatcher or process-state snapshot for this unit. This also preserves isolation when the runner explicitly enables collection parallelism.
