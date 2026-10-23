# fin-views-tab-programs

- Finish `views-tab-programs` as `wired` through the existing shell Programs door; no new navigation or lifecycle service.
- Reuse `BeatLoop`, `ProgramHeat` and `EventBurstLayer` for the remaining view effects. Cancel generated-container callbacks on hide, detach, run change and panic; register `program-moments` in `PanicSurfaces`.
- Add seven focused headless tests and retain the existing ignition tests, sharing their read-only enrollment fixture. Require the new panic entry in `PanicSurfacesTests`.
- Source inspected: `origin/parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported: its Programs view lacks the base's ignition/session partials, so the advanced base wins. All new behavior is adapted from the pinned WPF reference below.

## WPF gap audit

Reference and base: `dc9e6c92d7a597624f9b4003f610b411a6961be6`.
The cited `ProgramsTabView.xaml.cs` only forwards eight live buttons and rounds the session-bar clip; those routes and rounded clipping already exist on the head. Browse, details, entitlement routing, lifecycle controls, ritual/mantra doors and session timing were already wired. Comparing its XAML and the called MainWindow partials found these remaining behaviors:

| Missing on the base | Reference | Completed here |
| --- | --- | --- |
| Warm+ today's pip breathing | `ProgramsTabView.xaml:626-653`, `MainWindow.ProgramsTab.cs:778-823` | Shared 30 fps beat, opacity .35-.95 and scale .9-1.12, sine 1.2 s each way; static halo below Warm or with ambient motion disabled. |
| Once-per-day node ignition | `ProgramsTabView.xaml:666-692`, `MainWindow.ProgramsFx.cs:899-919` | .9 s cubic expansion .6-1.35; opacity 0/.95/0 at 0/.18/.9 s, using the existing per-run/day guard. |
| Day-complete banner spring and sparks | `MainWindow.ProgramsTab.cs:971-986,1833-1845`, `MainWindow.EventFx.cs:412-415` | .9-1 scale over 350 ms, BackEase amplitude .6; 90 theme-colour sparks (the actual WPF method uses a fixed count). |
| Task card, done-chip pops and task sparks | `ProgramsTabView.xaml:1278-1320`, `MainWindow.ProgramsTab.cs:1074-1091`, `MainWindow.ProgramsFx.cs:984-1026` | Track tasks seen incomplete by run/attempt/day/id; .93-1 in 350 ms and .2-1 in 420 ms, BackEase .6/.9. Heat-sized accent sparks after containers exist. No replay on repaint or completed first paint of another attempt. |
| Chapter-seal sparks and eased landing | `MainWindow.ProgramsFx.cs:1071-1133` | Preserve seal copy/fade; BackEase amplitude .5 to scale 1 at .3 s, then linear to 1.06 at 1.6 s; full-heat accent sparks. |
| Graduation sparks | `MainWindow.ProgramsFx.cs:1150-1192` | Full-heat gold sparks, sharing the existing once-per-run gold handover. |
| Header/Today/rail entrance | `MainWindow.ProgramsTab.cs:1689-1765` | Header and Today: 14 px rise in 340 ms, fade in 300 ms, quadratic out, delays 0/90 ms. Rail: cubic growth in 550 ms after 150 ms. First reveal/tab entry only. |

These are live WPF callers; P122 does not apply. None requires another unported feature. The overlapping `shell-programs-fx` ledger row is outside this unit's status audit.

## Verification limits

SDK: .NET 10.0.402. Builds use Release. The WPF test build additionally uses `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. The final Avalonia test build was retried with `--no-restore` after reclaiming generated Windows outputs.

| Command | Final result |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release` | 0; 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0; 0 errors |
| `bash .github/scripts/core-guards.sh` | 0; All Core guards passed. |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` with `DiffEngine_Disabled=true` | 0; Failed: 0, Passed: 2823, Skipped: 5, Total: 2828; 50 s. |
| `git diff --check` | 0; clean. |

The five skips are the existing Vosk tests: the model is unavailable under the documented 403 restriction. No tests were changed to skip or weaken checks. WPF was compiled but not executed on Linux.


- Avalonia tests are compiled, not executed: the supplied environment limit identifies font creation failure unless `cloud-test-fonts` has landed; that lane is absent from the pinned base. Therefore there is no local red/green runtime proof for the new regressions. Supervisor rcheck must run the focused filter below on its font-capable host.
- Suggested filter: `ProgramsCompletionMotionTests|ProgramIgnitionTests|ProgramsBrowseTests|ProgramsLockedRouteTests|ProgramsRunControlsTests|ProgramsRunLifecycleTests|ProgramsRunViewTests|ProgramsSessionSheenTests|PanicSurfacesTests` (FullyQualifiedName contains matches).
- No desktop comparison, render-all, Windows runtime execution or Vosk-model download. No Windows source changed.
- Build sweeps initially exhausted disk while copying native dependencies (affected build processes exited -6). All affected builds subsequently passed after removing only generated test/Windows outputs; no source or dependencies were removed.

## Assumptions

- Keep the head's motion policy: Off settles new interactions immediately; Reduced keeps finite interactions but removes ambient loops and particles. WPF's standalone banner/task pop helpers do not independently check Off; this head consistently applies the shared transition gate.
- A panic suppresses Programs decoration through queued service refreshes until a new tab visit or run, preventing immediate restart after stop.
- Existing capability-specific disabled reasons and the intentional Fluent icon choice remain the established port behavior.
