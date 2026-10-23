- Replace the Goon dispatch test's unsynchronized PopQuiz fake-clock advancement with a local driver that waits for each scheduled delay to be registered before advancing monotonic time. Timer callbacks run on workers, and all rendezvous share the original five-second fire budget.
- Keep the exact minimum scheduling buffer and explicitly reject one millisecond less. Queue both Core dispatch callbacks and prove neither sync notification nor scheduled action runs before the queued callback is drained; retain the exact hop and single-fire assertions.
- Production clock, rounds, dispatch, assembly parallelism, and other test fixtures are unchanged. No source was imported. The read-only `parity/trunk` reference was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; the earlier clock fix `69c78c7a4dc1af4451d5f1f0954afbcee12e7ee3` was reviewed.

## Verification limits

The reproduced defect is in the old test driver: `ScheduledFire.RunAsync` reads a remaining delay before creating its relative timer. While the old fake advances through several timer slices, a background continuation can read time at 800 ms, calculate 200 ms remaining, and register that timer after fake time has already reached 999 ms. Its due time becomes 1199 ms, beyond the test's final advance to 1000 ms. Waiting in real time cannot complete that fake timer.

An isolated copy of the original test and fake forced exactly that interleaving with worker/barrier rendezvous, leaving production code unchanged. The original fire assertion failed (exit 1, one failure); the replacement driver passed (exit 0). The replacement waits for the next registration before advancing again, so it cannot advance between that continuation's time read and timer registration.

The quoted historical `Assert.NotNull(handle)` failure was **not reproduced**. It precedes all fake-time advancement and cannot be explained by the demonstrated timer-registration race. At the supplied base, the action is non-null, the local clock is undisposed and synced, and its injected timestamp remains fixed across `SafeFireAt` and `ScheduleAt`. The assembly already disables xUnit collection parallelization in `RoadmapServicePersistenceTests.cs`. The supplied run reports 2816 tests; this base discovers 3748. GitHub API access returned HTTP 403, so the failing run's commit/source provenance could not be checked. This change does not claim to establish that historical failure's root cause.

Valid unmodified-baseline runs (SDK 10.0.402, Release, `DiffEngine_Disabled=true`):

| Command | Repetitions | Result |
| --- | ---: | --- |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~GoonClockAndRoundsTests` | 50 | 0 failed runs; 12 passed per run |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 5 | 0 failed runs; 3743 passed, 5 skipped per run |

The after-fix focused command also completed 50 repetitions with 0 failed runs, 12 passed per run.

The first after-fix full batch had 1 failed run out of 5: `BoardTilesTests.A_frame_fits_the_budget` reported an 18.50 ms frame while the WPF cross-build was compiling concurrently. The other four runs passed, and none failed a Goon clock test. Five further full runs without concurrent compilation all passed (exit 0 each, 3743 passed and 5 existing Vosk skips per run). Across the ten after-fix full runs there was one board-budget failure and zero Goon clock failures; the original failure is retained in this report.

Mutation proof: in an isolated copy of tracked Core sources, replace `GoonDispatch.Post(() => Synced?.Invoke(this, EventArgs.Empty));` with a direct `Synced?.Invoke(this, EventArgs.Empty);`. Build the mutated Core library and execute the corrected focused test against it. Exit 1, one failed test, `Assert.Equal`: expected 0 notifications before draining dispatch, actual 1. Restoring the original production binary gives exit 0, one passed test. The mutation never touched the production checkout or the binaries used by the repetition batches.

Builds: `dotnet build <project> -c Release` completed with zero errors for `CCP.Core`, `CCP.Avalonia`, and all Linux test projects named by `.github/workflows/build.yml`: `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and `CCP.Avalonia.Snapshot.Tests`.

`bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0.

The WPF test project also cross-built successfully (exit 0, zero errors) with `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Windows/WPF code was compiled but not executed on Linux.

Avalonia test assemblies were compiled but not executed, as instructed for this environment's known font-creation failure; GitHub must run the relevant dispatcher tests. The five Vosk-dependent Core tests use their existing missing-model skips; no skip, assertion relaxation, timeout increase, or quarantine was added. The model download is blocked in this environment, so full Core results are not Vosk-enabled gate coverage.

The initial read-only checkout could not fetch, so the explicitly authorized temporary-clone fallback was used and its expected base commit was verified. Initial concurrent builds exhausted disk space by duplicating native assets and invalidated an earlier partial baseline; those runs were discarded. Generated binaries were removed between subsequent builds, and both baseline batches above were restarted with sufficient free space. No generated artifacts or local profile data are committed.

## Assumptions

- Preserve the production minimum-buffer rejection and dispatch contract. Repair the demonstrable harness race without changing application behavior or claiming the unavailable historical failure was solved.
- Keep the existing assembly serialization; a new collection or quarantine cannot address the timer interleaving and is unnecessary at this base.
