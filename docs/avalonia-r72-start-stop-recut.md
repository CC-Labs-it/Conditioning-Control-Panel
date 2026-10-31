# Start and stop re-cut onto 7.2

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad, clean local work branch.
Repository fetch identified CC-Labs-it/Conditioning-Control-Panel.
No WPF source changed. One new completion body key in all ten languages; existing localized dialog_session_complete title reused. Safety and Scrolller retain base behavior.

WPF paths below are relative to ConditioningControlPanel/.

## Lane audit and delivered behavior

Fetched all three named branches and read their own-work ranges, not their inherited changes:

- fin-shell-start-stop: 0e1aa0473e7a..375d07202df9.
- host-modal-triggers: 39260f198fad..9ec3ce5761f3.
- merged-account-recovery-head: 27850c70070e..cedd65e35bef.

| Behavior | Result and WPF spec | Avalonia/Core evidence |
| --- | --- | --- |
| Ramp capture and 2 s timer | Ported, MainWindow/MainWindow.StartStop.cs:619 | CCP.Avalonia/Views/Windows/MainShellWindow.EngineRamp.cs:15; CCP.Core/Helpers/IntensityRampRun.cs:27 |
| Six links, caps, range, preset visual/completion guard | Ported, MainWindow/MainWindow.StartStop.cs:709 | CCP.Core/Helpers/IntensityRampRun.cs:52; CCP.Avalonia/Views/Windows/MainShellWindow.EngineRamp.cs:41 |
| Restore values on stop | Ported, MainWindow/MainWindow.StartStop.cs:648 | CCP.Avalonia/Views/Windows/MainShellWindow.EngineRamp.cs:25; MainShellWindow.StartStop.cs:116 |
| Crash journal | Ported current WPF addition, MainWindow/MainWindow.StartStop.cs:632 | CCP.Avalonia/Views/Windows/MainShellWindow.EngineRamp.cs:19; existing launch heal CCP.Avalonia/App.axaml.cs:509 |
| Preset ramp captures user values before session overrides | Ported current WPF ordering, MainWindow/MainWindow.Presets.cs:1618 | CCP.Avalonia/Views/Windows/MainShellWindow.SessionRun.cs:59; EngineRampLifecycleTests.PresetStopRestoresUserValuesRatherThanPresetRampBases |
| Manual/preset start relapse | Ported, MainWindow/MainWindow.StartStop.cs:331; Services/Progression/AchievementService.cs:659 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:84; MainShellWindow.SessionRun.cs:58 |
| Manual stop and panic timestamp | Ported, MainWindow/MainWindow.StartStop.cs:99; Services/Progression/AchievementService.cs:642 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:36; MainShellWindow.PanicKey.cs:122 |
| Relapse boundary and persistence | Ported, Services/Progression/AchievementService.cs:668 | CCP.Core/Services/Progression/AchievementEngine.cs:29 |
| Audio-only lifecycle | Covered by PR #3308, MainWindow/MainWindow.StartStop.cs:365,533 | fetched origin/pr-3308 MainShellWindow.StartStop.cs:96,117; not copied |
| Remote controller start gate | Already, MainWindow/MainWindow.StartStop.cs:43 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:29 |
| Forced overlay/lock teardown | Already; base retained, MainWindow/MainWindow.StartStop.cs:485 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:114 |
| Shared ramp math and journal launch healing | Already, MainWindow/MainWindow.StartStop.cs:728,632 | CCP.Core/Helpers/RampMath.cs:84; CCP.Avalonia/App.axaml.cs:509 |

PR API enumeration failed with Forbidden. Only #3308 was independently inspected via Git refs;
other open-PR coverage is not claimed. The supplied triage identifies the remaining gaps.

## Part B

Each next complete lane plus focused tests would exceed the remaining budget when combined with this delivery.

| Missing behavior | WPF spec | Current head evidence |
| --- | --- | --- |
| Scheduler startup/poll/manual-stop memory | MainWindow/MainWindow.StartStop.cs:795,834,875 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:5; no SchedulerRun on base |
| Start hero charge/exhale/ignition/heartbeat and lifecycle | MainWindow/MainWindow.HeroFx.cs:169 | CCP.Avalonia/Views/Windows/MainShellWindow.HeroFx.cs:17; rings exist in MainShellWindow.axaml:2036 |
| Time-of-day counters, crash sentinel and autonomy engine start | MainWindow/MainWindow.StartStop.cs:343,454,421 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:88; old EngineTimers.cs:21-32 not copied |
| Empty flash/wallpaper notices and second toast action | MainWindow/MainWindow.StartStop.cs:258,286 | CCP.Avalonia/Helpers/NotificationService.cs:37; old StartStop.cs:96 and BambiTakeoverTabView.axaml.cs:547 not copied |
| Presets running label | MainWindow/MainWindow.StartStop.cs:91 | CCP.Avalonia/Views/Windows/MainShellWindow.StartStop.cs:133 |
| Modal ordering, settle/timeout, launcher hold, game park and deferred passive draining | Services/Startup/StartupPresenter.cs:98 | CCP.Avalonia/Platform/StartupLadder.cs:21,56,66 remains passive-only |
| Required display name, server save and panic close | MainWindow/MainWindow.DisplayNameAsk.cs:46 | CCP.Avalonia/Platform/AccountSeed.cs:139 publishes ProfileLoaded; no RequiredNameAsk subscriber |
| Recovery seams, provider reauth and repaint/sign-in offer | Services/Account/MergedAccountRecoveryHead.cs:11,59; MainWindow/MainWindow.Login.cs:49; App.xaml.cs:4590 | CCP.Avalonia/App.axaml.cs:669 seeds SyncPush only; no MergedAccountRecoveryHead |
| Merged reply doors | Services/RemoteControlService.cs:129; Services/Content/ContentPackService.cs:781; Services/PieceByPiece/PieceByPieceHostService.cs:489 | CCP.Core/Services/RemoteControl/RemoteRelay.cs:109; CCP.Core/Services/Content/ContentPackService.cs:478; CCP.Avalonia/Views/Games/GameWindow.Pbp.cs:211 |

The HTML has no host-modal-triggers row on this base. Its outstanding work is recorded in the
existing shell-inbox row; shell-start-stop, shell-login and shell-remote-control also updated in place.

## Validation

Commands used /home/agent/.dotnet/dotnet (SDK 10.0.403).

- dotnet-install --channel 10.0: default ci.dot.net discovery returned 403; the same channel with --quality GA --azure-feed https://builds.dotnet.microsoft.com/dotnet installed 10.0.403.
- dotnet build CCP.Core: 50 warnings, 0 errors.
- dotnet build CCP.Avalonia: 202 warnings, 0 errors.
- dotnet build Tests/CCP.Avalonia.Tests: 398 warnings, 0 errors.
- dotnet test Tests/CCP.Core.Tests: Passed, failed 0, passed 4640, skipped 6, total 4646. Skips are existing explicit platform/optional-resource cases, unchanged.
- dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~EngineRampLifecycleTests|FullyQualifiedName~JumpRightInTests|FullyQualifiedName~TestIsolationTests': 12 isolation tests passed; 3 shell tests failed during construction with Could not create glyphTypeface, Segoe UI Black. In that combined run, shell behaviors compiled, not run. No assertion was skipped or weakened.
- Fresh process: dotnet test Tests/CCP.Avalonia.Tests --no-restore -p:BuildProjectReferences=false --filter FullyQualifiedName~EngineRampLifecycleTests: passed 2, failed 0, skipped 0.
- Final new head suite: dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter FullyQualifiedName~EngineRampLifecycleTests: passed 3, failed 0, skipped 0, duration 1 m 3 s.
- Head fail-proof: removing the direct ramp start and manual-stop timestamp calls caused both head tests to fail at behavior assertions (Ramp.IsActive and LastPanicPressTime). Exit 1, failed 2, passed 0. Sources restored in finally.
- Preset-order fail-proof: reverting to runner.Start before StartEngineRamp failed the regression test at expected user volume 23 versus captured preset volume 67. Exit 1, failed 1, passed 0. Source restored in finally; restored preset rerun passed 1, failed 0, skipped 0.
- Restored Core focused rerun: passed 5, failed 0.
- Core fail-proof: temporarily changed IntensityRampRun.Tick to return before updating values, and the relapse boundary from <=10 to <-2. EngineRampRulesTests failed 4, passed 1, exit 1. Sources restored in finally. This detects missing ramps, preset audio behavior and the relapse boundary. Production font bootstrap was also tried; the combined run had the same construction failure. A fresh process running only the original two new tests passed both, including the stricter fresh panic timestamp assertion. The restored full new suite, including the preset ordering regression, then passed 3 with 0 failed and 0 skipped.
- bash .github/scripts/core-guards.sh: All Core guards passed.
- python3 .github/scripts/avalonia_docs.py: avalonia_docs self-check OK.
- git diff --check: clean.

No live verification. Shell-close restore and the journal are covered by the passing lifecycle test;
crash recovery already has Core SessionSettingsJournalCoreTests on this base.

## Review follow-up

- P2 live master volume: WPF MainWindow.StartStop.cs:676-680,749-754 drives MainWindow.UiUpdates.cs:998-1010. EngineRamp.cs now refreshes AudioSettingsBinder, the running layered bed and MandatoryVideoOverlay after ticks and restoration. Lifecycle tests assert displayed slider/label and the live player volume at 60% and restored 40%.
- P2 completion notice: WPF MainWindow.StartStop.cs:780-786. EngineRamp.cs sends the localized OS notification before stopping. Tests assert title/body and pre-stop ordering; manual stop, disabled completion and active preset remain silent.
- Review validation: Core build 0 errors; Avalonia build 0 errors; head tests build 0 errors. Full Core suite passed 4640 with 6 existing skips; all 4 focused EngineRampLifecycleTests passed. Core guards and avalonia_docs passed. Completion title/body exist in all 10 valid language files. No live verification.
- Review fail-proof: temporarily omitted both audio refresh calls and the completion notification. Focused tests failed 2, passed 2: displayed volume stayed 40 instead of 60, and the completion notice was absent. Source restored in finally; rerun passed all 4 with 0 failed and 0 skipped.

## Local second-gate follow-up

P2-1: ConfirmStopSession now stamps confirmed early stops, including the Sessions-tab button; the shell-button duplicate is removed (WPF SessionEngine.cs:477-478, Presets.cs:2102). P2-2: StopEngineRamp refreshes audio only when active and OnEngineStopped hides tint and repaints Start first (WPF StartStop.cs:654,527). Tests exercise cancel/confirm/restart, inactive teardown with a playing bed, and tint/button teardown despite a throwing volume setter. Resume ramp restart remains part B. No new keys or WPF changes; no live verification.
Validation: all three builds had 0 errors; Core passed 4640 with 6 existing skips; all 4 focused head tests passed before and after mutation. Removing the session stamp and inactive guard failed 2 tests (missing timestamp, extra player write); reverting teardown order separately failed with the tint still visible after a volume exception. Core guards and avalonia_docs passed. Sources restored in finally.
