- Added WPF's avatar countdown: fraction from the scheduled interval (including retries), 100 ms updates while Takeover runs, preference/random-trigger visibility, and stop/close cleanup. Reused the existing tube bar; its shadow uses BoxShadow.
- Added active-mod announcement event audio using the existing content resolver and phrase slug. Missing audio stays text-only. Existing speech playback owns volume, mute and panic cancellation.
- Added WPF's diagnostic Test dialogs and their before-action ordering. The existing Takeover panic registration closes this modal; the scheduler drops the continuation after Stop. No new global service or window type.
- Replaced the duplicated historical ledger text with one honest stub row and numbered WPF tester steps.
- Source: adapted the WPF reference at `472c8a71b2b1e29c0c560acb8e3efa711a1acf0f`. Inspected read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## WPF gap inventory and scope

At the pinned base the row claimed the following gaps: Spiral pulse, Brain Drain pulse, Web Video, Wallpaper, announcement banner, countdown, announcement event audio, diagnostic Test dialogs, Force Start, Mind Wipe's host, and non-Takeover time-based quest/achievement minutes. Its historical repetitions also claimed Spoken Mantra was absent.

- Already present: TakeoverAnnouncerOverlay and its scheduler call/test, Spoken Mantra voice funnel, microphone consent, PTT/wake modes, Takeover quest-time credit, Pink Filter/Spiral quest clocks, and MindWipePlayer seeded in App startup when LibVLC loads. No duplicate implementation added.
- Closed here: countdown, event audio, diagnostic Test dialog behavior and panic cleanup.
- Dead on WPF's live picker path: `git grep -n 'AutonomyScheduler.Candidates' 472c8a71b2b1e29c0c560acb8e3efa711a1acf0f -- ConditioningControlPanel/Services/AutonomyService.cs` leads to SelectAction. `CCP.Core/Services/Autonomy/AutonomyScheduler.cs` Candidates never adds SpiralPulse, BrainDrainPulse or BubbleCount. BubbleCount's existing direct test path is retained. SpiralOverlay now exists in this head, but adding a candidate would diverge from WPF.
- Force Start: `git grep -n 'BtnForceStartAutonomy' 472c8a71b2b1e29c0c560acb8e3efa711a1acf0f -- ConditioningControlPanel` finds only the collapsed XAML button, its forwarding handler and MainWindow handler. No code exposes it. Its bypass-all-checks service entry is not a live user path. No P122 promotion is claimed: the row remains stub for the dependencies below.
- Missing Web Video: WPF AutonomyService.TriggerWebVideoFullscreen claims browser media ownership before navigation, waits for the queue/fullscreen slot, selects without repeats, then delegates autoplay and lifecycle/watchdogs to BrowserMediaService (554 lines) and browser navigation/player integration. There is no equivalent playback host in Avalonia. A URL launch alone would not reproduce ownership, panic or completion behavior.
- Missing Wallpaper: WPF WallpaperService (852 lines) owns original desktop capture, image selection, serialized writes and crash restore; AutonomyService adds keep-versus-timed-revert and Stop cleanup. The existing folder/duration UI is not a wallpaper host. Porting both this service and browser integration, plus their tests, cannot fit this unit's 1000-added-line cap.
- Still missing: non-Takeover time-based achievement credits and brain drain/Deeper/conditioning quest minutes. Pink Filter and Spiral quest minutes are already wired via OverlayQuestMinutes. Mind Wipe is already wired through the LibVLC startup host and is unavailable in headless shells that do not run that startup.

## Verification limits

Windows desktop behavior cannot run on this Linux worker. No real desktop or user profile is used by headless tests; the existing CCP_USERDATA_DIR test sandbox remains in force.

- Core guards: passed (exit 0). The base does not contain `.github/scripts/test-plan-coverage.sh`; numbered WPF/Rows steps were added and the ledger has exactly one row for this id.
- Countdown red phase: the focused Core test failed to compile on the base because NextRandomFireFraction did not exist. After implementation the focused scheduler suite passed 16 tests; the subsequently added diagnostic/audio tests also passed in the full Core run.
- Initial full Core run: exit 1, 4411 passed / 5 Vosk-model skips / 1 failed. BoardTilesTests.A_frame_fits_the_budget measured 64.65 ms against 12 ms while builds and rendering overlapped. Board rendering was not changed. No assertions were relaxed.
- Initial combined head run hit the known missing Segoe UI Black font in the quest test's app initialization. The relevant Takeover and quest test builders now reuse the existing WithInterFont/AppFonts setup. Isolated TakeoverTests: exit 0, all 13 passed. One earlier slow minimal-logger run was interrupted and restarted with per-test logging.
- AvatarTubeWindow offscreen render: exit 0, generated 576x754 PNG; inspected. No desktop session used.
- Queued-audio negative control: removed only the panic guard, rebuilt and ran AnnouncementPlaysTheActiveModsEventClipAndPanicStopsIt; exit 1 at Assert.Null because the queued announcement started a clip after Stop. Restored the guard automatically; no test was changed to pass the negative control.
- Required build sweep: all projects below exited 0 with zero errors. Existing compiler/analyzer warnings remain. Each command was `dotnet build <project> -c Release`; the WPF test build additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. The WPF application and test assembly compiled but were not executed on Linux.

| Project | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 |

- Final head run: `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~TakeoverTests|FullyQualifiedName~TakeoverQuestCreditTests' --logger 'console;verbosity=normal'`: exit 0, 14/14 passed. This includes the fail-proven queued-announcement panic guard and the quest clock with portable fonts.
- Final `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0.
- Core rerun without concurrent builds/rendering: `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 4412 passed / 5 existing Vosk-model skips / 0 failed, 41 s. The board timing test passed; the initial failure was load-sensitive.
- Not run: Windows WPF tests or Windows desktop behavior, the full Avalonia suite, Vosk model-dependent tests (model unavailable), and a render-all sweep. This row stays stub, so no P122/verified claim is made.

## Assumptions

- Kept Force Start collapsed, matching the WPF user path. Did not add actions that WPF's shared candidate list never selects.
- Kept the ledger stub because the remaining hosts do not fit; completed independent user-visible gaps first as requested.
- Installed pinned .NET SDK 10.0.100 after initial channel-discovery HTTP 403s; the channel installer subsequently completed with 10.0.402, which the final builds use.
