# fin-shell-she-listening

- Wire sustained spiral/pink, video pause/resume and screen shake into the existing voice command grammar, help and command rows.
- Preserve saved overlay settings, WPF's configured spiral opacity and pink's 40% floor. Holds survive reconciliation; release leaves another active owner alone. Register explicit panic cleanup even when the engine is already stopped.
- Cut the playing tube clip immediately on mute through its existing stoppable audio handle. The regression checks before the queued confirmation could mask the bug.
- Freeze video playback/attention/health clocks during a deliberate pause, re-phase on resume, retain strict-video refusal and prevent voice resume from bypassing the grace card.
- Adapt `ScreenShake.cs` from read-only trunk commit `b8576c3fade91143293d04e29abb8a30e1702d57` (fetched tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). WPF wins over trunk's easing and independent offsets: all windows share one offset, 30 ms ticks, 1200 ms voice duration. Include native web CSS and immediate panic cleanup. No existing base file was overwritten with trunk content.
- Add focused headless regressions for ownership, opacity, panic, immediate mute, video guards/grace/strict refusal, shake timing/replacement and transform restoration.
- Keep exactly one `shell-she-listening` ledger row at **stub**, with explicit remaining dependencies.

## WPF comparison

Reference pinned to base `9fed7071f1cef8fbb4d6fc675a545b620323177f`. Read `MainWindow.SheListening.cs` in full and follow its voice, audio and overlay callers.

| Behavior absent before this unit | WPF reference | Result |
| --- | --- | --- |
| Immediate mute of a playing clip | `MainWindow.Patreon.cs:2136-2174`; `AvatarTubeWindow.Speech.cs:1872` | Implemented without dismissing the bubble. |
| Sustained spiral/pink commands | `AutonomyService.VoiceCommandHost.cs:70-78`; `OverlayService.cs:1054-1158` | Implemented over existing renderers; no setting writes, symmetric ownership, panic cleanup. |
| Video pause/resume | `AutonomyService.VoiceCommandHost.cs:62-63`; `VideoService.cs:645-670` | Implemented with strict/grace protection and deliberate-pause health exemption. |
| Screen shake | `AutonomyService.VoiceCommandHost.cs` shake_once; `Services/UI/ScreenShakeService.cs` | Implemented, including web CSS. WPF passes 60, clamped by the service to 1 (28 px), for 1200 ms. |
| Configured/failed/active wake status and model hot detection | `MainWindow.SheListening.cs:152-173`; `Speech/SherpaWakeService.cs` | Missing: no cross-platform Sherpa runtime/dependency on this base; status/calibrate retain the not-installed notice. |
| Five-say calibration: consent, mic handoff, progress, sweep, persistence, failure handling, re-arm | `MainWindow.SheListening.cs:83-149`; `SherpaWakeService.CalibrateAsync` | Missing with Sherpa. The WPF service exceeds 800 lines before adapting NAudio and App dependencies; it cannot fit alongside these fixes under the 1000-added-line cap. |
| Dedicated KWS capture with Vosk fallback | `AutonomyService.Voice.cs:223-272` | Missing with Sherpa; the existing Vosk fallback remains live. |
| keyword_on / keyword_off | `AutonomyService.VoiceCommandHost.cs`; `KeywordTriggerService.cs` | Depends on `shell-keyword-triggers`: global keystrokes, matching/dispatch, OCR/highlight. `KeywordTriggerHead` is only a tour stub here. |
| deeper | `AutonomyService.VoiceCommandHost.cs`; `BrainDrainService` | Depends on unported Brain Drain runtime, desktop capture and blur overlay. Editor controls do not provide a live seam. |
| Video/randomized spiral sources | `OverlayService` spiral playback | Existing `SpiralOverlay.SourcePath` explicitly lacks randomized source selection and video decoding. GIF/static-image voice use is reachable. |

The remaining commands have live WPF callers, so P122 does not apply. This is a partial implementation, not a dependency NOOP. No new premium/consent policy, service abstraction or configuration was introduced. Existing platform limits remain: Avalonia mic capture/PTT is Linux-only; native Wayland and Windows lack these X11 tint/spiral renderers.

## Verification limits

- The pre-fix `VoiceSeamCommandsTests.MuteCutsHerPlayingClipBeforeTheConfirmation` attempt failed during font creation in `PremiumSpark`, before the behavior assertion. This confirms the supplied environment limitation, **not** a fail-proof of the regression. Remaining window tests are compiled but not executed here; the supervisor must run them with working fonts.
- Required build matrix and Core test results are recorded below when complete. Initial matrix exhausted the worker disk while copying duplicate native assets; rerun uses hard links and removes generated test outputs between projects.
- New tests use existing assembly `CCP_USERDATA_DIR`/XDG sandboxes, headless windows and overlay platform bypass flags. They never open a real mic or desktop notification.
- Windows cannot run on this Linux worker. Native web CSS and live X11 compositing were not exercised here. No render-all success is claimed.

### Results and commands

| Command/check | Exit | Result tail |
| --- | --- | --- |
| `dotnet build CCP.Core/CCP.Core.csproj -c Release` | 0 | Build succeeded; 0 errors. |
| `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release` | 0 | Build succeeded; 0 errors (existing warnings). |
| `dotnet build <workflow Linux test project> -c Release` for all 11 projects | 0 each | All 11 succeeded; 0 errors. |
| Final `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release` | 0 | 51 existing warnings; 0 errors, including strengthened new regressions. |
| `bash .github/scripts/core-guards.sh` | 0 | All Core guards passed. |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release` (rerun) | 0 | Passed 2814; failed 0; skipped 5; total 2819. Vosk-model tests skipped because the model is unavailable. |
| Pre-fix mute regression via `--filter FullyQualifiedName~VoiceSeamCommandsTests.MuteCutsHerPlayingClipBeforeTheConfirmation` | 1 | Could not create glyphTypeface: Segoe UI Black, in PremiumSpark. Product assertion not reached. |
| `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter 'FullyQualifiedName~PanicSurfacesTests&FullyQualifiedName!~EachPanicRouteCallsStopAllExactlyOnce'` | 0 | Passed 4; failed 0; skipped 0 (non-window registry checks). |
| WPF test build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` | -6 (process abort) | WPF app compiled; test output copy exhausted disk. No WPF tests run. |

Linux workflow test projects built: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings and Core.Tests. Build commands also use `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true` to reduce duplicate native-output storage; this does not change project source or compile options. The full Linux matrix is 13 successful builds including Core and the head.

The first Core test run overlapped generated-output cleanup and lost its working directory/assets (13 failures); rerunning after cleanup completed passed the whole suite. The first build matrix also ran out of disk; both infrastructure failures were corrected for the Linux checks. A non-window panic test attempt aborted during the WPF disk/resource failure (exit 134); rerun after cleanup and build-server shutdown passed all four checks. Raw worker logs are not committed because they contain machine-specific paths.

## Assumptions

- Prioritize reachable user-visible gaps within the line cap and retain stub status for remaining runtimes, as permitted by the task.
- Freeze the existing combined playback/attention/health clocks during an intentional hold and re-phase on resume so the five-second output-loss watchdog cannot replay a deliberately paused clip.
- Preserve WPF's actual `Shake(60, 1200)` caller and 0..1 clamp instead of interpreting 60 as a percentage.
- Use the requested writable clone fallback after the snapshot fetch failed on read-only `FETCH_HEAD`; verify it clean at the required commit before project inspection.
