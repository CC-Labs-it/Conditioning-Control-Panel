- Wire WPF empty-local-library warnings to engine start and wallpaper arming: recursive/case-insensitive probe, custom folder fallback, 18-second warning, Library/Assets action and sandbox-safe pack catalogue action.
- Wire localized player and mod-aware companion milestone/MAX LEVEL OS notices for the shell lifetime; detach on close and suppress queued callbacks after close.
- Wire legacy multi-monitor calibration sticky prompt and clear it after successful full calibration. Explicitly register calibration panic teardown before camera shutdown.
- Port scheduler/ramp hosts: 60-second grace, 30-second schedule checks, two-second ramp ticks, overnight/current-day rules, parser fallback, manual-stop latch, preset exclusion, base restoration and WPF-format ramp crash journal. Reuse Core RampMath and SchedulerTime.
- Keep one honest stub ledger row; add numbered tester steps and focused Core/headless tests.
- WPF reference pinned at be12c4537ed3a9d12a440bc36db51583bed8d25d. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no trunk code imported.

## Remaining dependency analysis

Initial gaps were the two empty-library warning callers, scheduler/ramp completion notices, player/companion level-up notices, legacy calibration startup/resolution and remote minimize/stop notices. The cited MainWindow.UiUpdates.cs contributes no additional toast or OS-notification calls.

Remote is not dead WPF: MainWindow.RemoteControl.cs MinimizeToTrayForRemote is called by the remote session host. Core RemoteCommands on this head has no start_session/pause_session/resume_session/stop_session handlers and falls through to NotOnThisBuild. Porting that host requires session selection, settings restoration, lockdown/panic gates, remote ownership and tray restoration, beyond the remaining unit budget with behavioral tests. Native Windows balloons also remain absent; OsNotifications retains its established visible-toast/hidden-drop fallback. No P122 exemption is claimed. The ledger remains stub.

## Verification limits

- .NET SDK 10.0.402 installed; initial snapshot was read-only and at a different SHA. Authorized fallback clone checked out the exact required base, with a clean status before creating the task branch.
- Core and Avalonia Release builds: exit 0. Every Linux test project listed in build.yml: exit 0 (serial build sweep; output copies removed between projects to fit the temporary filesystem).
- Windows-targeted WPF test build attempted with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false. C# compilation progressed but output copying exhausted temporary storage; build aborted. Windows runtime behavior was not run.
- Core guards: exit 0, all passed. test-plan-coverage.sh is absent on this base. git diff --check passed.
- Final focused headless tests: exit 0, 23 passed (ShellNotificationCallerTests, NotificationServiceTests, StickyNotificationTests, OsNotificationsTests, PanicSurfacesTests). Tests use CCP_USERDATA_DIR sandbox; no desktop notification, camera or user profile access. Font setup worked here.
- Final full Core suite: exit 0, 4014 passed, 5 Vosk skips, 4019 total (37 seconds). Earlier full run: 4010 passed, 5 Vosk skips. Subsequent expanded run: 4013 passed, 5 skipped, existing BoardTilesTests.A_frame_fits_the_budget failed under concurrent build load (17.90 ms). All new Core behavior tests passed after using the effective clamped ramp duration in their fixture.
- Existing SandboxNetTests caught the initial direct catalogue launcher; switched to ExternalOpener and that guard passed on rerun. No tests weakened or skipped to obtain green.
- Manual interactive acceptance, calibration acceptance with a camera, native Windows delivery, and render-all were not run.

## Assumptions

- Reuse existing OS notification transport and toast/calibration surfaces; native Windows delivery remains explicitly missing.
- Keep the shell visible if no tray icon exists to avoid stranding it without a restore path.
- WPF settings-save immediate scheduler kick is dead (schedulerWasEnabled is read immediately before comparison to the same value); preserve live periodic polling instead.
- Use the established wallpaper toggle as the arming path while wallpaper application remains an unported service.
