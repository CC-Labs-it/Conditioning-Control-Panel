# fin5-shell-launcher

- Base verified at `0608550b1ce20cae6278d830a67140608e5fdfa7`; the initial cloud snapshot was different and was corrected before implementation.
- Conceal the native panel before its first Show, keeping Opened initialization alive without activation or a taskbar entry. Restore opacity, activation, taskbar and companion boot flags after routing, including failure fallback.
- Reuse the existing startup passive router: hold before Opened and after the panel fade, route passive announcements into Inbox using Core's existing LauncherHolding input, release on every tray/panel restore. Update notification waits while held.
- Recheck Lockdown during boot routing so a late activation cannot tuck the panel away.
- Add three focused headless regression tests and numbered tester steps. No new surface or panic registration; existing launcher/shell registrations remain in force.
- No trunk code imported. `git fetch origin parity/trunk` was performed read-only; implementation adapts the pinned base's WPF reference and existing Core rules.

## Gap analysis and assumptions

The row's inherited follow-up list named boot concealment, startup hold, and live Lockdown evidence. The first two are addressed for the hosts this head already runs. All existing crossfade, exit-beat, close/refuse/minimize, handoff, first-run and tray behavior is retained.

WPF `MainWindow.Launcher.cs` uses DWM cloak because Window.Opacity is ineffective for its non-transparent window. Windows uses the same DWM attribute behind a small platform seam; other backends conceal panel content with zero opacity before Show. X11 has no DWM cloak. Native compositor evidence is still required, particularly for X11 window chrome.

The row remains **stub**. Full WPF startup-modal arbitration is not ported: `Services/Startup/StartupPresenter.cs` is 646 lines, with queue timing, predicates, deduplication, modal ownership, parking without timeout consumption and passive drain integration. Its live What's New and failed-update-report paths also need migration and behavior tests. Porting the full host and those callers faithfully exceeds the remaining allowance in this unit; inventing a second modal queue solely for the launcher would duplicate that host. This change wires the existing passive router and existing update-notification wait only. The early failed-update report still lacks that arbitration.

Pinned WPF proof: `MainWindow/MainWindow.xaml.cs:594-595` calls ShowWhatsNewIfNeeded and TryPresentSeasonRecap. `MainWindow/MainWindow.Marquee.cs:271` now defines TryPresentSeasonRecap as silent monthly bookkeeping (seasons retired); it opens no recap dialog. The old Avalonia comments claiming a recap popup dependency are stale. What's New is live and enqueues priority 30; failed-update reporting is priority 10, updates priority 80. No P122 promotion is claimed because the live modal dependency remains.

No new shared engine logic is needed: LauncherBoot and StartupQueueCore already own the rules. No Core source changed. Tests use the existing suite's isolated profile and memory-only secret-store setup.

## Verification limits

- SDK: .NET 10.0.402. Channel discovery initially returned HTTP 403; the stable SDK installation succeeded.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. Initial execution in the read-only sandbox could not create temporary files; rerun with workspace write access passed.
- `bash .github/scripts/test-plan-coverage.sh`: not available on this base (exit 127); numbered steps were added anyway.
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 1; 4,168 passed, 5 skipped, 1 failed of 4,174. The failure is `BoardTilesTests.A_frame_fits_the_budget`; Core source is unchanged.
- Initial overlapping app/test builds hit a generated-assembly file lock. Serialized `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` passed with 0 errors.
- Initial focused launcher run: 2 passed, 1 failed because the boot-only test observer also ran on tray restore. Diagnostic stack confirmed ShowFromTray; observer now unregisters after the first Opened without removing any assertions. Windows/native compositor behavior cannot run on this Linux worker. Vosk-dependent and unrelated full Avalonia suites are outside this focused run. No PR is opened.

Build command for each project below: `dotnet build <project> -c Release --nologo -v quiet` (Windows test compile additionally uses `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).

| Project | Result tail |
| --- | --- |
| `CCP.Avalonia.CatalogueStartup.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Consent.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.GeneralSettings.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Language.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Migration.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Report.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Scheduler.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Snapshot.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Avalonia` | `0 Error(s)`; exit 0 |
| `CCP.Core.Content.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Core.Settings.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Core.Tests` | `0 Error(s)`; exit 0 |
| `CCP.Core` | `0 Error(s)`; exit 0 |
| `ConditioningControlPanel.Tests` | `0 Error(s)`; exit 0 |

- Corrected focused headless tests: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~HiddenBoot_Conceals|FullyQualifiedName~LauncherHold_Inboxes|FullyQualifiedName~BootRouting_WhenLockdown'`: exit 0; 3 passed, 0 failed/skipped, 29 seconds. Font creation worked on this worker for this run.
- Core launcher rules: `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~LauncherRulesTests`: exit 0; 13 passed, 0 failed/skipped.
- WPF test project and the new Windows DWM seam compiled, but neither WPF tests nor Windows native code ran. No real-desktop boot or compositor check ran. Full Avalonia suite and Vosk-dependent tests did not run.

- Failure proof: temporarily set boot opacity back to 1 and remove launcher hold registration plus the boot Lockdown recheck. The same three tests failed on their behavior assertions (3 failed, 0 passed, exit 1, 32.6 seconds). Restore the exact saved implementation bytes afterward; no test was removed, skipped or weakened.
- Restored implementation: the same focused test command (with build, normal console logger) passed again, exit 0; 3 passed, 0 failed/skipped. Both app and headless test project rebuilt successfully.
