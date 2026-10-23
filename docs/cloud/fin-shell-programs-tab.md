- Finish `shell-programs-tab` against base/WPF reference commit `38ba1f616be82a1e661da1d9ab0fd7fcc2f9dc14`; ledger status is `wired`, not `verified`.
- Enable locked premium card actions and route their real click handler to Settings > Account, the head's existing App Info destination. This happens before enrollment, read-only and capability checks. No enrollment occurs. Programs requiring the unported Keyword Triggers engine retain their explicit missing-feature reason; paid ownership still cannot bypass enrollment checks.
- Restore the first-visit announcement on the owning You rail row: 1.2-second launch delay, four 700 ms sine dips from 1 to 0.35 with auto-reverse, then opacity 1. Quiet startup, seen/current-section, reduced/off motion and performance gates suppress it. Opening Programs cancels pending/live work and saves the seen flag. The existing shared 30 Hz BeatLoop parks while hidden/minimised; close, motion change and panic release it. Register `programs-announcement` in PanicSurfaces and its safety-critical assertion.
- Add focused card-model, routed card-click, announcement timing/gating, navigation, parking, close and panic tests. Update prior assertions to distinguish enabled Account links from enrollment actions.
- Imports: fetched `parity/trunk` read-only (`a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). No trunk code imported; the advanced base implementations win. Existing BeatLoop is reused (its own provenance is already recorded in its source).

## Verification limits

All required builds completed with zero errors. Window-based Avalonia tests are compiled but not run in this runtime, per the task's measured font-creation limitation; supervisor rcheck must execute them. The new card-model test opens no window and renders no text, so it can run here.

- Baseline Avalonia test build: exit 0, 0 errors.
- Before the fix: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~ProgramsCardRoutingTests` exited 1; the test failed because all four locked premium actions were disabled.
- After the fix: the same command exited 0; 1 passed, 0 failed, 0 skipped. This compiles the new window-based tests too.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (after allowing its temporary files).
- Full Core suite: `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --no-restore`, exit 0: 2822 passed, 0 failed, 5 Vosk-dependent tests skipped by the existing missing-model gate (2827 total). No tests were changed or disabled for that limitation.

Build commands were `dotnet build <project> -c Release`. The WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. All had zero errors; existing compiler/analyzer warnings remain.

| Project | Exit |
| --- | --- |
| CCP.Core/CCP.Core.csproj | 0 |
| CCP.Avalonia/CCP.Avalonia.csproj | 0 |
| Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj | 0 |
| Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj | 0 |
| Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj | 0 |
| Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj | 0 |
| Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj | 0 |
| Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj | 0 |
| Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj | 0 |
| Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj | 0 |
| Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj | 0 |
| Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj | 0 |
| Tests/CCP.Core.Tests/CCP.Core.Tests.csproj | 0 |
| Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj | 0 |

- The initial sweep exhausted the runtime filesystem on duplicated native assets. Only generated, untracked build outputs were removed; the successful sweep built each test project sequentially and cleaned its output after recording the result.

- Final focused run: `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~ProgramsCardRoutingTests|FullyQualifiedName~PanicSurfacesTests.EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered|FullyQualifiedName~PanicSurfacesTests.TheOrderingConstraintsHold'`, exit 0: 3 passed, 0 failed, 0 skipped. The final Skia setup matches the existing test host; no windows or text were rendered. The final compilation includes ProgramsAnnouncementTests.
- All test commands use `DiffEngine_Disabled=true`.
- No real desktop, account credentials or real user profile used. No Windows execution, display smoke, render-all or WPF visual comparison performed. No Vosk model downloaded (the task records a blocked download).

## WPF gap analysis and assumptions

The last ledger row at the base was `stub`. Its remaining tab behaviors were:

1. Locked premium cards: WPF ProgramsTab.cs:622-628 enables the action and gives the locked hint; :1982-1986 routes the click to ShowAppInfoPopup before CanEnroll. The head disabled all locked actions and never routed them. Implemented here; retained the head's existing missing-feature copy on programs that cannot finish.
2. First-visit pulse and rail/door escalation: WPF MainWindow.xaml.cs:3818-3833 schedules StartProgramsTabPulse after 1200 ms when HasSeenProgramsTab is false; ProgramsTab.cs:47-66 gates ambient motion and forwards to StartNavDoorHeaderPulse("programs"). TabNavigation.cs:831-874 supplies quiet/current-section gates and four opacity dips on the owning row. The head had no corresponding caller or pulse. Implemented here with the shared beat.
3. Entering Programs: WPF TabNavigation.cs:453 stops the cue. The head already saved HasSeenProgramsTab and opened ProgramsIntroPopup but did not cancel a cue. Cancellation is now on that same real navigation path; panic and close also cancel it.

Other behavior in the cited WPF file already has live base paths: browse ordering/art and selection; enrollment and persisted progression; pause/resume, withdraw/restart/dismiss; ritual photos and Mantras; day/tasks/reward track and lapsed/graduated views; session start, busy/failure paths and the live progress strip; event, language and mod repaint; and the Dashboard Today card. The latter has its own wired `shell-program-banner` row. The prior ledger's decoration gaps are explicitly owned by `shell-programs-fx`: FireBurstAt sparks, rail-node ignite, today's pip breathe and banner spring. They are not reimplemented in this unit.

The WPF pulse XML comment still describes button scaling. At the pinned base, its live body only forwards to the owning section row; there is no scale or page-pill fallback after StartNavDoorHeaderPulse returns false. This unit follows that executable behavior rather than reviving the retired tab button. Reproduce the caller proof with `git grep -n -E 'StartProgramsTabPulse|StopProgramsTabPulse|StartNavDoorHeaderPulse' 38ba1f616be82a1e661da1d9ab0fd7fcc2f9dc14 -- ConditioningControlPanel/MainWindow`.

The unported Keyword Triggers engine remains a named dependency of kept/firmware_install, protected by existing capability checks. This unit does not claim those programs can enroll. ProgramEnrollDialog's share level is Private in live WPF as well, so the head's existing default remains equivalent.
