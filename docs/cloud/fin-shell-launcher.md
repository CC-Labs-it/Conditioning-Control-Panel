# fin-shell-launcher

- Cloak the panel before its boot Show, retaining Opened initialization without flashing the panel. Windows uses WPF's DWMWA_CLOAK; X11 uses an empty XFixes bounding shape, synchronized before map, without requiring a compositor. Restore activation/taskbar/boot flags and the cloak after routing, in finally.
- Hold existing startup surfaces before Opened. Core LauncherHolding routes passive cards to Inbox; panel/launcher/tray restoration releases the hold. Startup updater offers and failed-install reports wait for the panel, and their waits end on close.
- Keep boot quiet and reuse the tray's one-time minimized notification for the manual launcher door. Recheck Lockdown during boot routing so a newly locked panel stays visible.
- Add focused headless tests for cloak ordering/restoration, Lockdown during startup, hold/Inbox/release, quiet boot/one-time notification, and failed-update waiting/cancellation. No new window/surface was introduced; existing launcher, panel, updater and game panic registration is unchanged.
- Keep exactly one shell-launcher ledger row, honestly `stub` for the separate modal-startup dependency below.
- Read-only import source: fetched parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported or existing base files overwritten. The WPF boot-cloak reference is already on the required base, introduced by `59a78fb8491c59b03b505ba93121ab9eddc2c590`.

## Reference audit and remaining dependency

Base: `17f1147e2fafa4e3219cb6e4cc37a74609fd2eac` (the required cloud-base-wire-shell-launcher tip). Read MainWindow.Launcher.cs in full, LauncherHost.cs (ShowPanel/HoldStartupLadder/ReleaseStartupLadder), TrayIconService.MinimizeToTray, App.ShowHiddenForBoot call site and RouteBootSurface. Compared with App.axaml.cs, LauncherWindow routing, panel crossfades, StartupLadder and AppUpdater.

The base lacked native boot cloaking, a startup hold before Opened, hold/release consumers, the manual door's one-time minimize notice, and guaranteed flag restoration after routing. These are implemented. The routing-time Lockdown check also covers Lockdown starting between boot decision and Show.

Missing: the full modal StartupPresenter pipeline and automatic What's New / season recap startup arbitration. MainShellWindow.Marquee.cs explicitly lists these absent startup callers; shell-marquee owns that dependency. The passive StartupLadder and updater are live consumers of the new hold. This unit does not claim that adding a Held flag ports the missing modal pipeline. The ledger therefore stays stub, rather than claiming every WPF startup modal is reachable. This is not a pure dependency NOOP: the independent boot/notification/hold gaps above required code changes.

Other unported game destinations and launcher art/FX belong to win-launcher and their game rows. No P122 dead-WPF exception is used. Native Wayland is outside the existing HWND/X11 platform seams; headless has no native cloak to apply.

## Verification limits

SDK: .NET 10.0.402. All builds use Release. `DiffEngine_Disabled=true` was exported for tests.

| Command | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` | 0 | 0 Error(s) |
| `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | 0 | 0 Error(s) |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build` | 1 | Failed: 1; Passed: 2810; Skipped: 5; Total: 2816 |
| `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~Launcher\|FullyQualifiedName~GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch'` | 0 | Failed: 0; Passed: 27; Skipped: 0 |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed. |
| `git diff --check` | 0 | No whitespace errors. |
| `git diff --exit-code origin/avalonia-port/cloud-base-wire-shell-launcher -- CCP.Core Tests/CCP.Core.Tests` | 0 | Core and Core tests unchanged. |

The one full-suite failure was `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`, asserting a non-null scheduled timer handle; it passed in the focused rerun. No Core source or Core test changes are included. The five existing Vosk skips reflect the unavailable model; the environment reports the model download blocked (403). No tests were edited to bypass either limit.

Added-line count against the required base: 357 (cap 1000).


The supplied environment notice says this base's Avalonia test host fails font creation unless cloud-test-fonts has landed. That lane is absent from the pinned base. LauncherWindowTests and AppUpdaterTests (eight new focused cases) are compiled with zero errors but are not executed here; the supervisor's GitHub rcheck must execute them. The pending GitHub command is `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~LauncherWindowTests|FullyQualifiedName~AppUpdaterTests'` with `DiffEngine_Disabled=true`. Consequently no fail-before proof is claimed for the new tests. Existing tests were not weakened, skipped or removed.

Windows DWM code is compiled on Linux but cannot be run here. No real desktop or user profile is used for tests. X11 native cloak behavior, locked-down live boot, and render-all are not run in this environment. Native runtime evidence remains necessary before calling the row verified (this task never sets verified).

## Assumptions

- Use XFixes' empty bounding shape as X11's boot-cloak equivalent; restore the shell's default bounding shape after routing. The shell uses custom, undecorated chrome.
- Release the launcher hold when the tray restores the panel too, so a user can resume startup work through either visible-panel path.
- Reuse the existing OsNotifications seam for the one-time minimized notice. Windows' OS-notification fallback remains a shared notification-platform limitation, not a new notification backend in this unit.
- Preserve the frozen WPF reference and shared Core; keep the wider startup-modal feature as a named dependency.
