- Correct gaze rounds now dispatch WPF's default point-fired flash burst through the existing head hook, five `BubbleOverlay.SpawnOnce` calls, `CoreMindWipe.TriggerOnce`, or a pulse of active Pink Filter/Spiral overlays. Flash rewards bypass the suspended ambient schedule (#221); bubble and Mind Wipe rewards work with their continuous services stopped.
- Added the missing one-shot entry point to the existing bubble field. It keeps the existing cap, monitor targeting, clickability and display-change guard, starts no ambient timer, retires empty one-shot windows, and can hand its windows over to the ambient service.
- Added a bounded overlay pulse coordinator using the existing tint/spiral services: doubles/clamps painted opacity for one second, uses the shared gentle-visuals envelope when enabled, restores current settings/session opacity without settings writes, and cancels on engine stop or panic.
- Panic also closes shown/queued flashes, one-shot bubbles and stopped-service Mind Wipe audio, including rewards surviving a normally closed game. Closing a fullscreen game on panic cannot restart its suspended flash schedule.
- Extended `GazeMinigameTests` with one fact per effect, driving real correct rounds through the existing fake frame source and stepped clock. Extra facts cover bubble caps/window lifetime/ambient handover and gentle pulse timing/idle guards. Head tests retain `CCP_USERDATA_DIR` sandboxes and fake audio; they never open a camera, audio device or real desktop window.
- Kept every `lab-gaze-minigame` ledger entry `stub`: this base has no Brain Drain overlay to pulse, and WPF's mod-resolved praise is still hardcoded in this head. Existing bounded GIF-decoding differences remain documented.
- Source credit: fetched and inspected `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` read-only. No trunk code was imported: that snapshot still suppresses gaze rewards and has no bubble one-shot entry point. Existing base head files win; the additions adapt the frozen WPF dispatch (`GazeMinigameWindow.FireRewardEffect`), `BubbleService.SpawnOnce`, and `OverlayService.PulseOverlays`/`StartGentlePulse`.

## Verification limits

- SDK: `dotnet --version` -> `10.0.402` (exit 0).
- Built Core, Avalonia and every test project named in `.github/workflows/build.yml`. Command for each row: `dotnet build <project> -c Release --nologo`; the WPF test row also uses `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. All final builds exited 0; existing warnings remain. An intermediate added test had an ambiguous Avalonia/OpenCvSharp `Window` name, corrected before the final test build. The final headless-test rebuild includes all final source changes.

| Project | Final exit | Result tail |
| --- | --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 | `0 Error(s)` |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 | `0 Error(s)` |
| `Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj` | 0 | `0 Error(s)` |

- `bash .github/scripts/core-guards.sh` -> exit 0, `All Core guards passed` (429 XAML references checked). An initial attempt in the read-only shell could not create temporary files; rerun with write access passed.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release` -> exit 1 initially: 3338 passed, 1 failed, 5 skipped. The pre-existing `BoardTilesTests.A_frame_fits_the_budget` measured 18.15 ms against its 12 ms limit while builds ran concurrently; no Core code or tests changed.
- After `dotnet build-server shutdown`, `dotnet test Tests/CCP.Core.Tests -c Release --no-build --no-restore` -> exit 0: **3339 passed, 0 failed, 5 skipped**, total 3344, duration 40 s. The existing five Vosk tests skip because the model is unavailable (download is blocked in this environment); no tests were weakened, removed or newly skipped.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --no-restore --list-tests --filter 'FullyQualifiedName~GazeMinigameTests'` -> exit 0: **15 cases discovered**, including all six new facts. This was discovery only, not test execution.
- `git diff --cached --check` -> exit 0. The branch stays below the 1000-added-line cap against the exact base commit `875902f05957ff50707db05b704d9fb6768d8065`.
- All test assemblies other than `CCP.Core.Tests` were compiled only. The WPF test project successfully cross-compiled on Linux with the specified flags; neither WPF nor Windows-native Avalonia paths were run.


- Avalonia test execution, including fail-before/fix-after evidence, is deferred to the supervisor's GitHub rcheck per the supplied cloud font-creation limitation; the cloud-test-fonts lane is absent from this base. Required deferred filter: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~GazeMinigameTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~SpiralOverlayTests|FullyQualifiedName~MindWipeTests'`, with `DiffEngine_Disabled=true`.
- No live desktop/compositor, real camera, real audio device, or Windows runtime verification was performed. Existing platform refusals remain in force. Windows-specific Avalonia code is compiled through the Linux head build, not run.
- Assumptions: pulse all overlays that can actually be active on this base (Pink Filter and Spiral); do not import a separate Brain Drain port into this unit. Match WPF's five immediate one-shot calls, including its service cap, rather than starting the ambient bubble schedule. Panic may stop the shared reward services, as the application's global panic already does.
