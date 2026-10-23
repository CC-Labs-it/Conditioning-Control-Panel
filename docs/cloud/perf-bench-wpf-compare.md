- Add WPF `--perf-bench <out.json>` startup instrumentation using the real service startup, main window and `ShowTab` path, with `CompositionTarget.Rendering` frame timing and the existing `ccp-perf-bench/1` schema. Record startup, 20 measured cycles after a discarded warm-up, per-tab median/p95, and working set after two GC/finalizer passes. Missing tabs exit 3; frame timeouts exit 4. The flag requires an absolute `CCP_USERDATA_DIR` sandbox.
- Add a report-only `windows-latest` job publishing Avalonia win-x64 and building WPF Release, collecting five sequential Avalonia/WPF pairs, checking the real WPF missing-tab failure, and uploading JSON, logs and a Markdown comparison. Keep it advisory until three consecutive stable runs are reviewed.
- Add the stdlib-only comparison CLI: medians across runs, time limits of WPF × 1.10 + 20 ms, memory limit of WPF × 1.10, and failure on missing/extra tab keys or invalid reports. The workflow runs its regression self-test.
- Add focused WPF tests for statistics, the shared 24-tab list/order, and sandbox onboarding preparation. Document the pasteable comparison table with an explicit WPF baseline column.
- Imports: `git fetch origin parity/trunk` completed read-only; no trunk code was imported, so there are no imported trunk commit hashes to credit. Reused the benchmark contract already present at base `bc186f21fceb489b1167b1b9c0509c8404ded255`.

## Verification limits

- SDK: .NET 10.0.402 installed through dotnet-install; the installer's `ci.dot.net` version probes returned 403 before the supported builds.dotnet.microsoft.com download succeeded.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build <project> -c Release` for every Linux-compatible test project listed in build.yml: all exit 0, zero errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests.
- WPF cross-build: `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: initial build exit 0, zero errors. A later rebuild hit MSB4166 (worker exited); final retry with `-m:1 -nr:false` exited 0, 559 warnings, 0 errors (2m11s), including the new WPF tests.
- `bash .github/scripts/core-guards.sh`: exit 0, both guards passed. The first read-only sandbox attempt could not create scratch files; rerunning with temporary-file write access passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; 3339 passed, 5 skipped, 0 failed. The existing five Vosk tests skip without the unavailable model; no tests were changed or skipped by this branch.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~PerfBenchTests.MedianInterpolatesAndP95IsNearestRank`: exit 0; 1 passed, 0 failed.
- The Avalonia UI benchmark/navigation test was compiled but not run because this cloud snapshot has the documented font-creation limitation. Other Avalonia test projects were compiled, not executed.
- `python3 .github/scripts/perf-compare.py --self-test`: exit 0; 50% regressions in every metric, inclusive thresholds, medians, tab order and missing tabs checked. A separate CLI fixture with Avalonia startup 150 ms versus WPF 100 ms printed FAIL against a 130 ms limit and exited 1, as required.
- `python3 -c "import yaml; yaml.safe_load(open('.github/workflows/build.yml'))"`: exit 0. `git diff --check`: exit 0.
- WPF code and its new tests compile here but cannot execute on Linux. Real Windows rendering, process exit codes, five-run paired measurements and the PowerShell job await GitHub CI. No Windows performance numbers are claimed; the documentation labels them pending.

## Assumptions

- WPF's real main window needs its normal service initialization. Its benchmark includes that initialization, while Avalonia retains its existing direct-shell benchmark contract. Splash/launcher routing and onboarding prompts are bypassed only under the flag; the startup ladder is held and the sandbox is prepared as an onboarded offline user.
- Preserve shared `ExclusivesTab` in JSON and drive WPF's existing `ShowTab("exclusives")` redirect to its lazy Premium page, checking that page's visibility.
- Stability promotion is a manual review after three consecutive stable Windows runs; the new job remains report-only.
