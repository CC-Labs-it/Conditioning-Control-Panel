# fin-feat-video

- Park the empty-video remote-media offer while an update or first-run dialog owns the screen, matching WPF `App.OfferRemoteMediaSource` and `FlushPendingRemoteMediaOffer`.
- Flush from both updater completion paths and after the accepted first-run wizard starts its ten-minute quiet window. Keep the first pending request; recheck the media source and both modal gates on flush. Inbox or held-card activation during a new modal re-parks without spending the once-per-launch claim.
- Add focused routing tests that need no window, native decoder, desktop or fonts. The existing test host supplies `CCP_USERDATA_DIR` isolation and memory-only secrets. No new surface is introduced; the existing intro popup and panic registration are reused.
- Keep exactly one `feat-video` row, honestly `stub`, and replace the historical notes with the remaining gaps.
- Imports: none. Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no files were copied or overwritten from that branch.

## WPF comparison and remaining gaps

Reference pinned to base `a8d06e544be40eef87e0dc8cc27ff74ab2fa12a9` (the published base tip was verified equal). Read both cited `Features/VideoFeatureControl.xaml(.cs)` files in full, compared their settings, copy, order, editors, warnings and Test path with the existing Avalonia card, and followed the outstanding callers into WPF `App.xaml.cs`, `Services/Video/VideoService.cs`, `Services/Tracking/GazeFocusService.cs` and `Services/AutonomyService.cs`.

| WPF behavior still absent at base | Evidence / disposition |
| --- | --- |
| Park an empty-video remote-media offer behind update/startup dialogs without consuming the launch claim | `App.xaml.cs:828-905`, `VideoService.cs:2474-2495`. Implemented using existing `AppUpdater.IsUpdateDialogActive` and `MainShellWindow.IsStartupDialogShowing`; a modal ladder is unnecessary for these two real gates. |
| Gaze dwell hits the same idempotent target pipeline as mouse/toy input | `VideoService.cs:776-795`; `GazeFocusService.cs:958,1175-1190` enumerates video targets and completes the dwell. Avalonia persists `VideoGazeClickEnabled`, but `PlayTabView.ChkFocusGaze_Changed` is empty and there is no shared gaze-target registry/service. Dependency: Focus Gaze. |
| Test asks “Another interaction is in progress (...)”, Yes cleans video, ends browser takeover and force-resets the queue; No returns. The playing-video reset also tears down browser takeover and queue. | `VideoFeatureControl.xaml.cs:368-418`; live WPF `App.InteractionQueue` constructed at `App.xaml.cs:2619`, `AutonomyService.ForceEndWebVideoTakeover` at `:1276`. Avalonia's video-only reset exists, but shared `InteractionQueueService` and web-video takeover lifecycle do not. Dependencies: shared fullscreen arbitration and browser takeover. |
| Funscript receives the video's playback time | Core scheduler starts/stops funscripts, but `FunScriptService.SubscribePlaybackTime` remains unseeded in Avalonia (`App.axaml.cs` haptics setup). `MandatoryVideoOverlay` only updates its watched-time counter from LibVLC. Remains a concrete wiring gap. |
| Rescue a blocked UI thread; quarantine wedged players and retire/recreate the shared native engine with the WPF cap/deferred retirement | `VideoService.cs:68-210,1076-1152`. The Avalonia overlay explicitly documents the missing watchdog/quarantine and still stops native playback on the UI thread. Existing UI-timer vout recovery is not this behavior. Requires a coordinated shared-LibVLC lifecycle change, beyond a small card/offer fix. |
| Secondary/video-message no-activate z-order behavior | WPF `VideoService.cs` uses `ShowActivated` and native z-order handling; the Avalonia overlay explicitly records this omission. OS-specific lifecycle work remains; no live Windows proof here. |
| Stand down and announce an explicit Test while For You owns the screen, then replay when released; select remote videos when the online source is active | `VideoService.cs:2273-2338,8028-8207`. The card passes `userInitiated: true` on WPF. Avalonia's scheduler has no matching feed/browser coordination or remote-video coordinator. Dependencies: FYP/browser media integration. |

The other card behaviors are already reachable: loading/rebinding every setting, live Enable, min/max clamping, monitor refresh/fallback, Mercy visibility and changed-value fold opening, strict warning/Lockdown hold, phrase/style editors, Test/playback/reset/error dialogs and mod-aware art. Existing scheduler/overlay tests cover the shipped portions. This is not a P122 dead-caller promotion: the outstanding WPF paths have live callers. The row stays `stub`; no `wired` or `verified` claim is made. The native lifecycle omissions and other listed gaps remain beyond this focused fix.

## Verification limits

- SDK: .NET `10.0.402`. The prescribed channel installer initially reported a 403 from its fallback endpoint; installation from the primary Microsoft build feed succeeded.
- `dotnet build <project> -c Release`: exit 0 and `0 Error(s)` for every Linux project below. Existing compiler/analyzer warnings remain.

| Project | Exit code |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |

- `export DiffEngine_Disabled=true; dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`: exit 0; **2818 passed, 5 skipped, 0 failed**. The five existing Vosk tests skip because the speech model is unavailable in this environment; no tests were weakened or disabled.
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~RemoteMediaOfferModalTests`: exit 0; **6 passed, 0 skipped, 0 failed** against the final production code. These routing tests instantiate no windows and need no fonts. Before the fix, the two modal-refusal cases failed with expected `0`, actual `1` offers (exit 1).
- `bash .github/scripts/core-guards.sh`: exit 0; **All Core guards passed** (424 XAML type references checked).
- `git diff --check`: exit 0. The ledger has exactly one `feat-video` row.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -m:1 -nr:false`: exit 0; **0 errors**, 512 existing warnings. Compiled WPF app/tests and Windows code, not run. The first parallel-node attempt failed with `MSB4166` (child node exited prematurely); the single-node retry passed.
- Existing font-dependent Avalonia video/overlay classes and `--render-all` were compiled but not run, following the supplied environment limit. Windows desktop behavior, live LibVLC decoding, webcam/gaze input and physical haptic devices were not run. No real desktop/profile was accessed by the new tests.
- The initial multi-project build batch exhausted disk space while duplicating generated app outputs. Removed only generated test binaries and repeated the full required build list sequentially, deleting each generated test output afterward; all 13 Linux builds then passed. An initial Core-guards invocation also failed during that disk exhaustion; the completed rerun passed.


## Assumptions

- Use the pinned commit as the authority; the cloud snapshot's local branch name is irrelevant. The original checkout was read-only and at a different commit, so the authorized fallback clone was used and pinned before edits.
- Reuse the two existing modal flags and their actual completion paths instead of introducing an entire startup modal ladder.
- Flush after the accepted first-run quiet window begins, so an offer becomes an Inbox row instead of interrupting onboarding. A declined age gate continues to shut down without showing a card.
- Keep the remaining native-engine and cross-feature work explicit rather than attempting a second feature port inside this unit.
