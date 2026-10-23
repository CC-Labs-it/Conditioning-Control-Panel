- Replace the fake player's queued `Thread.Sleep(20)` report with a report controlled by each test. Snapshot recorded calls under the same lock used by writers.
- Add an optional wait delegate only to the existing internal `WaveOutTeardown.Run` test entry and its private implementation. Public `Release` keeps its dedicated background thread, real wait handle, 2,000 ms playing deadline, 50 ms stopped grace, and abandonment policy. This is the minimum WPF change; no Core or Avalonia behavior changes.
- Prove Stop precedes the report and disposal: at the wait boundary the player has only received Stop, its source is alive, and the real report event is unset. Raise `PlaybackStopped`, observe the real wait signal, and require exactly `stop, report, dispose` plus one source disposal.
- Drive a missing report and stopped-device grace deterministically. Assert the chosen deadline, abandonment without disposal, and use of the grace rather than the playing deadline. Retain the duplicate-teardown and source-only checks.
- Prove nonblocking Release with a dedicated caller and a gate holding the teardown inside Stop. Release must return on a different thread while that gate is closed, before any report or disposal. Open the gate in `finally`, then join the actual teardown thread and verify the never-reporting device and source remain undisposed. The 10-second watchdog detects a deadlock; elapsed latency is never asserted and no product timeout was increased.
- Trunk imports: none. Fetched `parity/trunk` read-only as required; existing base code wins and no trunk code was needed.

## Root cause

At base `61a934110bdba5d4e4a1c4442f4cbd38ee6f2190`, `Tests/ConditioningControlPanel.Tests/WaveOutTeardownTests.cs:44` queues the fake report on the thread pool and sleeps 20 ms there. The synchronous test calls Run at line 62. In `ConditioningControlPanel/Services/Audio/WaveOutTeardown.cs:171-176` (base line numbers), Stop queues that callback, then the test thread waits 2,000 ms. If the loaded pool does not run the callback before that wait expires, Run correctly returns Abandoned. The assertion at test line 64 expects Disposed and fails; the late report cannot repair that outcome. The pool, the Sleep, and the test runner's own scheduling all contribute to the race.

The other failure is independent: test lines 126-130 count first-call JIT, OS thread creation/scheduling, and runner descheduling against a 500 ms DateTime budget. Release itself starts a dedicated Thread (`WaveOutTeardown.cs:82-91` on the base), not a pool teardown. An observed 857 ms wall-clock duration does not prove it waited for the driver. The already-stopped test had the same wall-clock weakness at test line 92. No product stop/dispose ordering race was found in this path.

## Windows behavior proofs for the supervisor

Run the following on Windows, including the full WPF suite afterwards:

```powershell
$env:DiffEngine_Disabled = 'true'
dotnet test Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:ValidateExecutableReferencesMatchSelfContained=false --filter FullyQualifiedName~WaveOutTeardownTests
```

Deliberately break behavior in a disposable checkout, one mutation at a time, and require the filtered run to fail before reverting:

1. Move `output.Dispose()` before the report wait in RunClaimed. The playing test must fail its pre-report `stop`-only assertion (or its exact final call sequence).
2. Change the report handler to omit `reported.Set()`. The playing test must get Abandoned instead of Disposed; it uses the real signal rather than trusting a fake successful wait.
3. Ignore the false wait result and dispose unconditionally. The never-reporting test must fail its Abandoned/no-disposal assertions.
4. Use `waitMs` for an already-stopped device. The grace test must fail its exact `10` deadline assertion.
5. Add `t.Join()` immediately after `t.Start()` in Release, or execute RunClaimed inline. Release can no longer complete with the Stop gate closed, so the structural test must fail its deadlock watchdog. Cleanup opens the gate even on failure.
6. Remove TryClaim's duplicate protection. The duplicate test must fail its Duplicate outcome and exact one-teardown sequence.

For a controlled reproduction of the old flake, hold every available thread-pool worker behind a test-controlled gate before running the original playing-device test on a dedicated thread. Keep the report callback queued until after Run's 2,000 ms wait returns: the original test must see Abandoned and fail at line 64. Release the pool gate in cleanup. The new playing test needs no pool worker or real timeout. This reproduction and the mutation runs require the supervisor's Windows runner; they were not executed in this Linux job.

## Verification limits

SDK: .NET 10.0.402 (installed with the requested dotnet-install channel command). No test was weakened, quarantined, or newly skipped.

Commands and result tails:

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release --nologo` | 0 | `48 Warning(s), 0 Error(s)` |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --nologo` | 0 | `186 Warning(s), 0 Error(s)` |
| `dotnet build Tests/<project>/<project>.csproj -c Release --nologo` for every Linux test project below | 0 each | `0 Error(s)` each |
| `dotnet build CCP.LinuxSmoke/CCP.LinuxSmoke.csproj -c Release --nologo` | 0 | `1 Warning(s), 0 Error(s)` |
| `dotnet build CCP.VR/CCP.VR.csproj -c Release --nologo` | 0 | `1 Warning(s), 0 Error(s)` |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false --nologo` | 0 | `563 Warning(s), 0 Error(s)` |
| `bash .github/scripts/core-guards.sh` | 0 | `All Core guards passed.` |
| `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests -c Release --no-build --nologo` | 0 | `Failed: 0, Passed: 3339, Skipped: 5, Total: 3344` |

The 11 test projects compiled are `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and `CCP.Avalonia.Snapshot.Tests`. Build/test logs are in `/tmp/flake-waveout-verification/` in this cloud workspace.

The first guard attempt could not create temporary files in the read-only sandbox and did not constitute a valid check. Rerunning with write permission produced the passing result above.

The WPF application and Windows-targeted tests compiled successfully with the two supplied cross-targeting flags. NuGet retried one package-download timeout successfully during that build. Windows tests and their red/green mutation proofs cannot execute on Linux. Avalonia tests were compiled but not run under the supplied cloud font limitation; no Avalonia implementation changed in this unit. The Core run's five skips are the existing Vosk inference tests because the model is unavailable; the supplied environment notes report that its download is blocked with 403. No new Skip attributes or test exclusions were added. Smoke heads were compiled, not executed.

## Assumptions

- A per-call internal wait delegate is the smallest acceptable WPF seam; no global hook, public API, scheduler abstraction, or new configuration is necessary.
- The unit concerns test determinism, so production audio deadlines and thread scheduling are preserved.
