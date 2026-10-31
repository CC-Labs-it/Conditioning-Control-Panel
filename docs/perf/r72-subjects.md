# AvailableSubjectsTab switch

Base: `dd0b812769dd4e7b99e01a36fe071d9f22d291b2`.

The WPF visibility callback (`MainWindow.SubjectsFx.cs:45`) only initializes
motion and staggers cards. Its navigation case separately calls `EnterLobbyTab`
(`MainWindow.Lobby.cs:84`), which paints its snapshot. Avalonia's visibility
callback instead calls `Repaint` synchronously and its Loaded handler calls it
again. Both previously replaced all three ItemsSource lists and reassigned the
column button content, even for the empty, unchanged lobby.

The Avalonia view now assigns three observable sources once. A repeated show
checks snapshot values, gates, language and error state before touching controls.
Changed snapshots replace only card slots whose rendered values differ, append
new slots and remove excess slots. Snapshot list copies protect change detection
when a caller reuses a mutable list. Gate and language changes still refresh
cards and header text. Motion, layout policy, shell navigation and WPF are unchanged.

`LobbyReuseTests` checks sources, card instances, realized container instances and
collection event count on a repeat show, a single changed slot, sign-out and
language refresh. It fails against the base at the source identity assertion.
The lobby, frame and stage headless fixtures now use the production app builder to register fonts;
without that, their order could leave the entire process without the app's fonts.
No assertions were removed or relaxed.

## Reproduce

Install .NET 10, then from the repository root:

```sh
CCP_TRACE_TAB_SWITCH=1 dotnet test Tests/CCP.Avalonia.Tests -c Release \
  --filter FullyQualifiedName~TabSwitchCostTests \
  --logger 'console;verbosity=detailed' > /tmp/subjects-trace.log 2>&1
rg 'availablesubjects:|Total tests:|Passed:|Failed:' /tmp/subjects-trace.log

dotnet test Tests/CCP.Avalonia.Tests -c Release \
  --filter 'FullyQualifiedName~LobbyReuseTests|FullyQualifiedName~LobbyPageTests|FullyQualifiedName~SubjectsFxTests|FullyQualifiedName~TabTransitionsTests|FullyQualifiedName~PerfBenchFrameTests|FullyQualifiedName~AmbientBeatLoopsTests'
```

The stage benchmark uses the full 24-tab navigation order, one warm-up and 20
measured cycles, rendered headless with Skia. Layout is explicitly measured
before waiting for the rendered composition batch. Stage medians do not sum to
the median total. Linux headless timings diagnose work; they do not establish
the Windows performance gate. Windows numbers come from the PR's
`perf-bench-windows` CI artifact. The acceptance rule remains median and p95 each
<= WPF x 1.10 + 20 ms (`docs/avalonia-perf.html`).

## Regression checks

Release headless targeted suite (lobby reuse, lobby behavior, Subjects FX,
transitions, ambient beat loops, rendered-frame endpoint and stage trace):

```text
Test Run Successful.
Total tests: 34
     Passed: 34
```

The new guard on the unchanged production base failed with `Assert.Same()` at
`LobbyReuseTests.cs:47` (ItemsSource changed on repeat show). It passes after the
fix, including zero collection changes on repeat show and exactly one event for
one changed row. Existing fontless-fixture failures were resolved by using the
production font registration, without altering assertions. Existing compiler
and Avalonia warnings remain.

## Local stage medians (ms)

Isolated before/after trace processes, Release .NET 10.0.14, Linux headless Skia.
The baseline production assembly was preserved before rebuilding the optimized
view. Font setup used the production builder in both runs.

| Stage | Source | Before | After |
|---|---|---:|---:|
| recent | `MainShellWindow.TabNavigation.cs:148` | 0.600 | 0.611 |
| bark-and-routing | `MainShellWindow.TabNavigation.cs:169` | 0.002 | 0.003 |
| visibility | `MainShellWindow.TabNavigation.cs:174` | 0.325 | 0.289 |
| tab-transition | `MainShellWindow.TabNavigation.cs:181` | 0.016 | 0.017 |
| history | `MainShellWindow.TabNavigation.cs:183` | 0.003 | 0.003 |
| nav-rail | `MainShellWindow.TabNavigation.cs:187` | 0.003 | 0.003 |
| ambient-fx | `MainShellWindow.TabNavigation.cs:189` | 0.004 | 0.004 |
| section-chrome | `MainShellWindow.TabNavigation.cs:191` | 0.227 | 0.205 |
| tooltip | `MainShellWindow.TabNavigation.cs:196` | 0.003 | 0.003 |
| entitlements | `MainShellWindow.TabNavigation.cs:210` | 0.043 | 0.043 |
| tab-hooks | `MainShellWindow.TabNavigation.cs:198` | 0.000 | 0.000 |
| show-tab | `MainShellWindow.TabNavigation.cs:139` | 1.281 | 1.218 |
| layout | `TabSwitchCostTests.cs:63` | 0.692 | 0.674 |
| next-frame | `PerfBench.cs:117` | 180.238 | 178.529 |

Source paths are under `CCP.Avalonia/Views/Windows/`, except
`TabSwitchCostTests.cs` under `Tests/CCP.Avalonia.Tests/` and `PerfBench.cs` under
`CCP.Avalonia/`. The change itself is in
`CCP.Avalonia/Views/Tabs/AvailableSubjectsTabView.axaml.cs:35` (stable sources),
`:89` (unchanged-paint guard), and `:125` (incremental card updates).

Commands used `/tmp/ccp-dotnet/dotnet`; logs:
`/tmp/subjects-before-detailed.log`, `/tmp/subjects-after-isolated.log`, and
`/tmp/subjects-after-detailed.log` (34-check suite). Both isolated trace commands
ended `Test Run Successful. Total tests: 1 Passed: 1`.
