- Finish `shell-programs-fx` from base `550ca894b3232c89baa05293b7c20d8f376d6d13`; mark the single ledger row `wired`, not `verified`.
- Wire observed task completions to the existing `JustCompleted` carrier: card spring 0.93 → 1 in 350 ms (BackEase amplitude 0.6), tick spring 0.2 → 1 in 420 ms (amplitude 0.9), and heat-sized task sparks. Keys include run, attempt, day and task; already-complete tasks open settled.
- Add the once-per-day banner spring (0.9 → 1, 350 ms, amplitude 0.6) and WPF's separate 90-particle, theme-coloured banner burst. Keep the existing day wave, XP float and pip ignition.
- Restore the seal's WPF BackEase amplitude 0.5 landing (1.35 → 1 in 300 ms), drift to 1.06 at 1.6 s, opacity keys at 0/.22/1.15/1.6 s and collapse. Add seal and graduation sparks with `ProgramHeat.BurstCount(1)` and the outgoing chapter/gold colours.
- Drive the live program-session clock, progress and Warm+ sheen from the existing session tick path, with no task-list or rail rebuild. Pause/resume updates immediately; motion, visibility, minimise and detach park the sheen. Reuse the rig's shared beat and the existing burst layer; register `programs-fx` for panic teardown and invalidate queued celebrations when stopped.
- Add focused headless regression tests for springs, seal timing, once-only bursts, session updates/gates and panic cancellation; retain existing coverage. No Core or WPF source changes.
- Trunk reviewed read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` after `git fetch origin parity/trunk`. Its `MainShellWindow.ProgramsFx.cs` is an analysis-only stub; **no code imported from trunk**. Existing base helpers win and are reused.

## WPF gap analysis

Reference pinned to base `550ca894b3232c89baa05293b7c20d8f376d6d13`, not a moving branch. Read `ConditioningControlPanel/MainWindow/MainWindow.ProgramsFx.cs` in full and the supporting call sites in `MainWindow.ProgramsTab.cs`, `MainWindow.EventFx.cs` and `Views/Tabs/ProgramsTabView.xaml`.

The base already implements heat/tier computation, shared accent and crossfade, wash/sigil/border/rail/counter/edge/boss/comet/particle scenery and lifecycle gates, daily wave/XP, rail breath/ignite, chapter baselining and graduation colour. The row's “Follow-up wire-shell-programs-fx-b” names deferred work; those implementations were absent from this base.

Every remaining gap identified for this row is addressed above:

| WPF behaviour absent on base | Live Avalonia path |
| --- | --- |
| Task completion card/tick pops and heat-sized sparks (`ProgramsFx.cs:990`, task template triggers) | `BuildTodayPanel` observes incomplete → complete; generated `TaskCard`/`TaskDoneChip` get their springs and `EventBurstLayer` burst. |
| Daily completion banner spring and separate event burst (`ProgramsTab.cs:978`, `EventFx.cs:412`) | `BuildRunPanel` uses the existing once-per-day guard for banner spring/burst alongside wave/XP. |
| Chapter seal's eased landing and sparks (`ProgramsFx.cs:1075`) | Existing completed-chapter baseline → `PlayChapterSeal`; beat writes the WPF curve, fade and final collapse. |
| Graduation sparks (`ProgramsFx.cs:1149`) | Existing graduated panel → `CelebrateGraduation`, one gold burst per run. |
| Live clock/progress and heat-paced session sheen (`ProgramsTab.cs:1423`, `:1777`) | Shell session ticks → `UpdateLiveSession`; only our program session shows the strip; paused/foreign sessions park it appropriately. |

No remaining behaviour of `MainWindow.ProgramsFx.cs` depends on an unported feature. This is not a dependency NOOP or P122 dead-caller promotion. The run entrance, Dashboard card and other `MainWindow.ProgramsTab.cs` work belong to `shell-programs-tab` and are not claimed by this row.

## Verification limits

Commands ran with .NET SDK 10.0.402, Release configuration, and `DiffEngine_Disabled=true` for tests. Final results:

| Command | Exit / result tail |
| --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0; 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0; 0 errors. Windows platform seams compiled, not executed. |
| `dotnet build <test-project> -c Release` for every Linux test project listed below | All 11 exit 0; 0 errors in each. |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0; 512 warnings, 0 errors. WPF app and test assembly compiled; Windows desktop tests not run. |
| `bash .github/scripts/core-guards.sh` | 0; “All Core guards passed.” |
| `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build` | 0; 2811 passed, 0 failed, 5 existing Vosk-model-dependent skips (2816 total). No tests changed or disabled. |
| `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~ProgramIgnitionTests.FirstDayStaysBelowChargedWithNoParticlesRimOrComet` on the unmodified base | 1; 0 passed, 1 failed before assertions: “Could not create glyphTypeface”, Segoe UI Black. |
| `git diff --cached --check` | 0. |

The 11 compiled test projects under `Tests/` are `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests` and `CCP.Avalonia.Snapshot.Tests`; each uses its matching `.csproj`.

The new `ProgramIgnitionTests` cases and `PanicSurfacesTests` compile but were **not run**, in accordance with the supplied cloud font limit after the baseline confirmed it. Before-fix failure proofs, render-all, desktop motion/performance measurement and WPF side-by-side comparison were not possible here; the font-capable CI/rcheck host must run the focused classes. No claim of runtime verification is made by `wired`.

Infrastructure: the initial snapshot HEAD differed from the expected commit, and the instructed fetch failed with read-only `FETCH_HEAD` (exit 255). The explicitly authorised fallback clone was verified clean at the expected commit before creating this branch. The first build-matrix attempt filled the small disk with native binary copies; generated test `bin` folders were removed and every required build was rerun successfully in sequence. No source/test workaround was used. No real desktop or user profile was accessed by tests.

## Assumptions

- Scope follows the cited `MainWindow.ProgramsFx.cs` plus the deferred springs/session-sheen work explicitly named by this row. Other Programs-tab work stays with its existing ledger row.
- WPF's banner burst is 90 particles in the theme colour (`EventFx.ProgramDayBurstCount`), despite the adjacent older comment suggesting heat sizing. The executable reference wins.
- Finite transform moments use the existing shared beat so they can be cancelled and stepped; no animation runs on a Transform, no infinite shell Animation or tile Effect is introduced.
- Finite springs respect the ignition rig’s transition policy: Reduced keeps them, Off settles them immediately.
