# fin-shell-notifications-toasts

- Base verified at `45279011f83e4615580ec0b34dcda454f39bb161`; the read-only snapshot required the requested temporary-clone fallback.
- Restore the WPF legacy multi-monitor calibration warning at shell open: exact message, warning colour, `recalibrate-multimonitor` key and Recalibrate action. A successful 16-point calibration clears the notice without persisting a dismissal; cancellation and failed saves leave it unresolved.
- Restore the level-up sound for main levels and companion milestones (every tenth level or maximum), after the notification. Reuse CoreAudio's stoppable player, WPF's three-path lookup order, master-volume curve and minimum volume. Replace the previous clip; stop on panic and shell close.
- Keep the ledger row `stub`: scheduler startup/settings-change/timer start and scheduled-end balloons, plus ramp-complete-before-stop, await the unported shell scheduler/manual ramp runtime (`shell-start-stop`). There are no placeholder notification methods for these unreachable paths.
- Reference source: the frozen WPF files in the base commit above. Fetched and inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code was imported.

## WPF gap audit

At the pinned base SHA, `NotificationService.cs`'s transient/sticky queue, dedup, duration, persisted close, action/programmatic clear, secondary action, typography, colours and fades already exist in Avalonia. Existing tray minimize/restore and controller-joined notices, main/companion level notices, empty-library actions, acceptance notices and popup placement remain the real callers. `MainWindow.UiUpdates.cs` has no notification-service caller (its popup reference is the feature host).

Gaps at the start of this unit:

1. Main/companion level-up sound: implemented, including lookup precedence, WPF volume floor and replacement lifetime. Companion card repaint belongs to `shell-companion-tab`.
2. Legacy calibration startup warning and resolution: implemented. The existing calibration window is already stopped by the registered camera panic surface; only the new sound needs an additional registration.
3. Scheduler startup and timer entry: minimize, then "Scheduler Active" / "Session auto-started based on schedule.", before StartEngine. Settings-change entry has the same balloon without minimizing. Scheduled exit: StopEngine, clear auto-start latch, then "Scheduler" / "Scheduled session ended.". Missing pending the scheduler runtime.
4. Manual intensity ramp completion: "Session Complete" / "Intensity ramp finished. Stopping...", then StopEngine, only if progress >= 1, EndSessionOnRampComplete and no preset session is active. Missing pending the manual ramp runtime.
5. The previous ledger's remote minimize/stop balloon gap is inaccurate. At the pinned SHA, the only occurrence of `MinimizeToTrayForRemote` is its definition; `StopSessionFromRemote` delegates to StopEngineAndSession, which emits no balloon. The controller-joined balloon is already wired. Remote session verbs remain a separate `shell-remote-control` dependency; Core RemoteCommands refuses them with `NotOnThisBuild`.

Reproduce the dead-caller evidence (read-only):

```sh
git grep -n MinimizeToTrayForRemote 45279011f83e4615580ec0b34dcda454f39bb161 -- ConditioningControlPanel
# MainWindow.RemoteControl.cs:1465: definition only
git grep -n 'ShowNotification(' 45279011f83e4615580ec0b34dcda454f39bb161 -- ConditioningControlPanel
# Companion milestones, remote minimize definition/controller joined, scheduler/ramp, player level; no remote-stop balloon.
```

This is not a P122 promotion: reachable scheduler/ramp behaviours remain missing, and the row stays `stub`.

## Verification limits

- .NET SDK: 10.0.402, installed with the requested channel installer (the secondary-feed 403 recovered via the primary feed).
- `dotnet build CCP.Core/CCP.Core.csproj -c Release --no-restore`: exit 0, 0 errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-restore`: exit 0, 0 errors.
- All eleven Linux test projects named in `.github/workflows/build.yml` built in Release: exit 0, 0 errors each. These include CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, and the CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler and Snapshot Avalonia suites. SDK hard-link flags and sequential cleanup of generated test outputs avoided the cloud disk limit. An initial sweep ran out of disk (exit 134); all Linux projects then succeeded on retry.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0, 2,815 passed, 5 existing Vosk-model skips, 0 failed. No tests were weakened or disabled; the Vosk model download is blocked in this environment.
- Before the sound fix, `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~CompanionMilestones_play'`: exit 1, the new regression failed because only the two notices were captured and no audio played.
- After the fix, the ShellLifecycleNotificationTests filter: exit 0, 6 passed. Final filter combining that class with PanicSurfacesTests.OneFailingStopNeverSkipsTheRest, EveryIdIsUniqueAndEverySafetyCriticalIdIsRegistered, TheOrderingConstraintsHold and EveryStarterIsRegisteredOrAllowlisted: exit 0, 10 passed, 0 skipped/failed.
- The new ShellCalibrationNotificationTests and extended WebcamCalibrationTests compiled with 0 errors but were **not run**, per the supplied cloud font limitation. The calibration fix therefore has compile-only test coverage here, not a claimed fail-before/pass-after execution. The supervisor's font-capable lane must execute them. Render-all was not run for the same limitation; no P122/wired claim is made.
- Conditional WPF build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: the first attempt hit the same disk limit; after cleanup, an MSBuild worker exited prematurely (MSB4166). The single-node retry (`-m:1 -nr:false`, same targeting flags) succeeded: exit 0, 511 existing warnings, 0 errors. The WPF tests were compiled, not executed.
- Windows native code, real desktop/camera access and audible playback were not run. Audio assertions replace CoreAudio's provider; tests use the existing sandbox profile setup. The existing OS notification seam uses D-Bus on Linux and visible-window toast fallback on Windows; this unit retains that documented platform decision. Remote taskbar flashing belongs to the separate remote-shell row.
- `git diff --check`: exit 0. No WPF or Core production source changed.

## Assumptions

- Scheduler/manual-ramp runtime and remote session verbs are separate units; do not port those services merely to make notification helpers callable.
- Preserve WPF's literal English legacy warning/action and milestone titles, and its nonzero sound volume floor, including master volume zero.
- Use the existing panic camera stop for the existing calibration surface; register the newly reachable audio explicitly.
