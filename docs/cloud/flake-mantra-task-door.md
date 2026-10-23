- Fix the Mantra Lab's completion timer lifetime: retain the existing five-second timer on the window and stop it in `Cleanup`, alongside the float and idle timers. Early Escape/owner close now cancels its pending callback.
- Root cause: `TypedMantraCompletesTheSessionWithTonesDroneAndMemorySignal` completes a lab and closes it early (`MantraWindowSessionTests.cs:69-98`). Previously `OnSessionComplete` left a local timer alive after `OnClosed`. Its callback calls `CleanupAndClose`; `Cleanup` ends the process-wide `App.Mantra` service even if another window has since started a new session. Serial tests still share the dispatcher and this singleton. The Programs door starts that same service (`MainShellWindow.PlayTab.cs:31`) and the old callback can make the new lab visible but inactive. No other G–R class starts a typed lab; the leaking predecessor is in the same test class. The other shard classes' delays and dispatcher pumping change when the orphan timer becomes due.
- Recorded failure evidence: [run 39867732123, avalonia-linux (2), job 120134070977](https://github.com/CC-Labs-it/Conditioning-Control-Panel/actions/runs/39867732123/job/120134070977), 2026-10-22 23:10:24 UTC: `ProgramTaskDoorOpensTheMantraLabAtTheTaskReps` fails at line 214 (`Assert.True(AvApp.Mantra.IsActive)`), after the single-window and visibility assertions pass.
- Add `ClosingCompletedLabDoesNotEndTheNextProgramTaskSession`: drive completion presentation, make the real close timer due with a zero test interval, close the old lab, open the four-rep Programs door, and drain the dispatcher. Assert the new lab stays visible and active at `/4`, then closes cleanly. No sleeps, polling, timeout increase, weakened assertion, or quarantine. Restore the audio hooks and stop the captured timer even on failure.
- Import credit: no trunk code imported. Fetched `parity/trunk` read-only and inspected tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; the fix uses the existing base window and timer.

## Verification limits

- SDK installed: .NET `10.0.402`.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, 0 errors (existing warnings).
- `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release`: exit 0, 0 errors (existing warnings).
- Every other Linux test project in `.github/workflows/build.yml` built with `dotnet build <project> -c Release`, exit 0, 0 errors:
  - `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`
  - `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj`
  - `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj`
  - `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj`
  - `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj`
  - `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj`
  - `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj`
  - `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj`
  - `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj`
  - `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj`
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed. The read-only sandbox initially prevented its temporary files; reran with workspace execution permission.
- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build`: exit 1; 3337 passed, 2 failed, 5 skipped. Unrelated failures: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, actual Clips), and `BoardTilesTests.A_frame_fits_the_budget` (46.15 ms). The existing Vosk tests skipped because the pinned model fetch returned HTTP 403 (fetch exit 22). Both failures passed a focused recheck after compilation finished (exit 0: 2 passed, 0 failed); a full rerun without overlapping builds then passed (exit 0: 3339 passed, 0 failed, 5 existing Vosk skips). No Core source changed.
- Focused Avalonia run, with `DiffEngine_Disabled=true`, `--filter 'FullyQualifiedName~CCP.Avalonia.Tests.MantraWindowSessionTests'`: exit 1; all six tests blocked during `Window.Show` by `InvalidOperationException: Could not create glyphTypeface` (missing Inter / Segoe fonts). The original five-test baseline and the pre-cleanup regression attempt hit the same environment failure. This is not a local behavior fail-proof or pass-proof.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 559 warnings, 0 errors. The WPF app and tests compiled; Windows behavior cannot be run on this Linux host.
- Full G–R shard, 20x repeat, and deliberately broken behavior execution are deferred to the supervisor's GitHub rcheck, per the environment limits. Other Avalonia test assemblies were compiled, not executed.

Supervisor repeat command (20 fresh testhosts; includes the original flaky test and the deterministic regression):

```bash
export DiffEngine_Disabled=true
for i in $(seq 1 20); do
  dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build \
    --filter 'FullyQualifiedName~CCP.Avalonia.Tests.MantraWindowSessionTests' || exit 1
done
```

Also repeat the real shard context with `bash .github/scripts/avalonia-tests-sharded.sh --shard 2`, or use its exact filter:

```text
FullyQualifiedName~CCP.Avalonia.Tests.G|FullyQualifiedName~CCP.Avalonia.Tests.H|FullyQualifiedName~CCP.Avalonia.Tests.M|FullyQualifiedName~CCP.Avalonia.Tests.N|FullyQualifiedName~CCP.Avalonia.Tests.O|FullyQualifiedName~CCP.Avalonia.Tests.P|FullyQualifiedName~CCP.Avalonia.Tests.Q|FullyQualifiedName~CCP.Avalonia.Tests.R
```

Deliberately break the BEHAVIOR: remove only `_closeTimer?.Stop();` from `CCP.Avalonia/Views/Windows/MantraWindow.axaml.cs` `Cleanup`, retaining the field, scheduling, and every test. Rebuild, then run:

```bash
export DiffEngine_Disabled=true
dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release \
  --filter 'FullyQualifiedName~CCP.Avalonia.Tests.MantraWindowSessionTests.ClosingCompletedLabDoesNotEndTheNextProgramTaskSession'
```

The stale timer then executes `CleanupAndClose` against the old window and ends the new singleton session; the new regression must fail its active-session assertion. Restore the cleanup line before the passing repeat proof.
