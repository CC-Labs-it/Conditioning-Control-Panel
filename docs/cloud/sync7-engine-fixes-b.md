# sync7-engine-fixes-b

Status: stub; coherent slice on `avalonia-port/cloud-sync7-engine-fixes-b`, based on `origin/avalonia-port/main-20261015-compat`.

- Ported WPF `2c519a67a` (sync ledger row 176): an empty Bubble Count video library produces a warning toast, every manual attempt and once per launch for scheduled attempts. Installed packs that are all disabled receive separate translated advice. Strings/web copy were already merged.
- Moved WPF's pure `BubbleCountNoVideos` into CCP.Core, keeping one implementation for both heads. The existing scheduler/host seam delivers the notice after releasing the busy state.
- Ported the bottom-bar portion of WPF `c52e7f6d8` (row 16): the session tick repaints its name/clock; pause-button and engine-stop paths repaint immediately; the countdown preference is honored and the idle line clears. Repaint does not save settings.
- Head tests initialize the real application's font builder. The existing queue fixture mounts its settings card before starting its one-second cover, so cold XAML/font loading cannot expire it before the tested click. All existing assertions remain.
- No trunk imports. No new window or panic surface was introduced; notices use the existing in-app notification service.

## Follow-up: sync7-engine-fixes-b-next

Row 12 (`33ae795c7`) remains needs-port: this head still has no Natasha red bubble, native press catcher, or equivalent bubble-over-flash interception.

Row 16 remains needs-port: the Pause other effects switch, flash stop/resume, bouncing-text hold and overlay ordering/release still need porting as one coherent feature. Its bar portion is implemented, not the effects hold. Base `SessionRunner` also ends zero-minute sessions on its first tick; this slice does not change that open-ended lifecycle limitation. Matching parity rows remain stub.

## Verification limits

Commands used .NET SDK 10.0.402 with `PATH="$HOME/.dotnet:$PATH"`, `DOTNET_PROCESSOR_COUNT=2` and `DiffEngine_Disabled=true`. Builds used `-c Release -m:1` and these storage-saving properties:

```text
-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true
-p:CreateHardLinksForCopyLocalIfPossible=true
-p:CreateHardLinksForAdditionalFilesIfPossible=true
```

Auxiliary test builds used `-p:BuildProjectReferences=false` after Core/Avalonia had built. The final combined head test used `FONTCONFIG_FILE=/tmp/ccp-fontconfig-no-woff2.conf`, including the system font configuration and rejecting only this host's unsupported `/usr/share/fonts/openai-sans/*.woff2` fonts. Repository fonts were unchanged.

- Before the scheduler fix, `Empty_library_explains_each_manual_attempt_but_scheduled_only_once` failed with expected 3 notices, actual 0.
- Before the bar fix, `Bottom_bar_tracks_session_pause_countdown_preference_and_stop` failed because the actual status was empty instead of starting with `Bar clock - `.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --filter FullyQualifiedName~BubbleCountSchedulerTests`: **12 passed, 0 failed**; includes scheduled/manual notice cadence and disabled-pack advice.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj ... --filter 'FullyQualifiedName~SessionRunWireTests|FullyQualifiedName~BubbleCountNoVideosTests|FullyQualifiedName~BubbleCountQueueTests'`: **5 passed, 0 failed**; covers real warning-toast text/color, session/pause/countdown/stop labels and existing queued-game/unsupported-playback behavior.
- Full `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj`: **2849 passed, 1 failed, 5 skipped** (2855 total). Existing `BoardTilesTests.A_frame_fits_the_budget` failed at 23.86 ms; an isolated recheck also failed at 43.38 ms under concurrent build load. Five existing Vosk tests skipped without their model. No tests were weakened or skipped by this change.
- `bash .github/scripts/core-guards.sh`: passed, including the complete WPF XAML type scan.
- `dotnet build` succeeded for CCP.Core, CCP.Avalonia and all eleven Linux-compatible test projects named in `.github/workflows/build.yml`: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` was attempted with the same copy properties. Cancelled after **10:05** while compiling the WPF temporary project, without completing. WPF head/tests are **not claimed compiled** and were not run on Linux.
- `CCP_USERDATA_DIR=/tmp/ccp-worker2-render-profile dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-worker2-render`: **225/225 views rendered**. `MainShellWindow.png` shows the shell/bar layout idle; active session text is exercised by the headless test, not that idle PNG. No real desktop/native overlay run was performed.
- `git diff --cached --check`: passed.

Added lines versus BASE, with rename detection: 229 (below 1000).
