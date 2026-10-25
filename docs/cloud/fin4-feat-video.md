# fin4-feat-video

- Base verified at `85d30ccc4b6cb241d136e8299a0d2e844b88585a`; local snapshot branch names are immaterial.
- Restored both mandatory-video art plates through the existing head `ModArt` resolver: mod override first, built-in fallback, WPF decode widths 480/800, right-aligned 90% hero and full side art. Mod changes marshal to the UI thread; detach removes the hook and releases owned bitmaps.
- Restored the Test exception dialog: **Error**, **Error triggering video: <message>**, OK. Registered `video-test-error` in `PanicSurfaces`; either OK or panic completes the click task.
- Added focused headless tests and numbered WPF-linked tester steps. Consolidated the ledger's duplicated history into one honest `stub` row with the remaining gaps.
- No trunk code imported. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapts the pinned WPF reference and reuses this base's helpers.

## Pinned WPF gap audit

Reference SHA: `85d30ccc4b6cb241d136e8299a0d2e844b88585a`. Read `ConditioningControlPanel/Features/VideoFeatureControl.xaml` and `.xaml.cs` and followed their service callers. These gaps were present before this unit:

| Behavior | Result / remaining dependency |
| --- | --- |
| Hero and side plates resolve mod art, load and repaint on mod switch | Implemented here, preserving decode widths, stretch, alignment and hero opacity. |
| Test exception reports the error to the user | Implemented here; adds panic dismissal. |
| Gaze click catches a visible attention target | Missing. `VideoService.GetGazeTargets` lines 763–779 feeds `Tracking/GazeFocusService.cs:962`. The shared webcam has coordinates, but no Focus Gaze host/target registration on this head; Play's handler is empty. The WPF focus host is 1,226 lines before adapting its cursor, monitor, dwell and other-target integrations. |
| Test's second conflict prompt; Yes cleans video, web takeover and queue, No returns | Missing. WPF card lines 502–524 calls `Autonomy.ForceEndWebVideoTakeover` and `InteractionQueue.ForceReset`. This head has neither the interaction queue nor web-video takeover host. WPF queue alone is 500 lines; all current fullscreen callers would need to participate. Existing first reset prompt and Yes/No labels remain. |
| Empty-library guidance offers remote media, once per launch, except when the duration filter emptied the library | Missing. `VideoService.cs:2469` calls `App.OfferRemoteMediaSource("videos")`; `App.xaml.cs:876` relies on startup-dialog gates, the StartupLadder/Inbox, once-per-launch claim and FeatureIntroPopup coaching. No matching shared coaching host exists on this head. Existing length-filter and installed/active-pack guidance remain. |
| Fullscreen/floating appearance, size, live drift speed, max simultaneous count, together and keep-sound switches | Missing controls and multi-instance floating playback host. The WPF card loads these at lines 81–99 and writes them at 180–248. Core presentation math exists but MandatoryVideoOverlay still owns one fullscreen run. Merely exposing settings would not complete the user path. |
| Random-start cut switch enabled only when max duration is nonzero; changes next playback | Missing control and random-start playback. WPF card lines 65–66, 349–356; this head caps elapsed playback but does not seek to the random cut start. |
| Replay-after-failed-check switch; normal video can end on fail/troll while strict/Lockdown still replay | Missing control and scheduler policy. WPF card lines 100, 251–258 and `VideoService.cs:5928` use existing Core `MandatoryVideoPresentation.ReplaysAfterCheck`; scheduler currently always replays fail/troll. |
| Clip funscript follows video playback position | Missing playhead seam. Existing start/stop hooks and background vibe remain, but the base ledger explicitly records that funscript never drives without a playhead source. |
| Test is user initiated and explains For You feed deferral | Missing feed guard/notice host. WPF card lines 527–529 passes `userInitiated: true`; this head's scheduler has no feed ownership path. |

These are live WPF callers, so P122 does not apply. The remaining services, floating playback and their tests together exceed the 1,000-added-line limit. This unit finishes two independent visible gaps, retains `stub`, and does not expose inert floating/cut/replay controls.

## Verification limits

Commands used .NET SDK 10.0.100 and `DiffEngine_Disabled=true` for tests.

- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia, and Tests/CCP.Avalonia.Tests. All Linux test csproj entries in `.github/workflows/build.yml` also built with exit 0: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests. Existing compiler/analyzer warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 552 warnings, zero errors. Compiled only; Windows tests and application were not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, **All Core guards passed.** The first invocation lacked temporary-file write access; rerunning with workspace execution permissions passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent from the pinned base. Numbered WPF-linked steps were added manually.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, **3,743 passed, five Vosk skips, zero failed**.
- Pre-fix `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~VideoFeatureCompletionTests`: exit 1, two intended failures after correcting the initial test fixture constructor: missing art plate, empty error-dialog collection. These are behavior failures, not font/environment failures.
- Post-fix focused head classes VideoFeatureCompletionTests, VideoTargetMonitorTests, MandatoryVideoToyInputTests and PanicSurfacesTests: exit 0, **17 passed, zero failed/skipped**. Strengthened VideoFeatureCompletionTests subsequently passed all three cases in Release.
- Expanded combined Release regression run including MandatoryVideoOverlayTests: exit 1, **17 passed, one font failure, one decoder skip**. Existing `PanicSurfacesTests.EachPanicRouteCallsStopAllExactlyOnce` could not create `Segoe UI Black` in PremiumSpark; this is the known environment/process-wide font-bootstrap limitation. The new test bootstrap now uses shipped `AppFonts.Options/Register`. Separate-process Release checks after that bootstrap change all exited 0: VideoFeatureCompletionTests **3 passed**, PanicSurfacesTests **5 passed**, and VideoTargetMonitorTests/MandatoryVideoToyInputTests/MandatoryVideoOverlayTests **10 passed, one native-playback skip**. All new assertions passed; no existing test was weakened or skipped by this change.
- `git diff --check`: exit 0.

Windows execution, real desktop playback, decoder/hardware and webcam proof are outside this Linux headless run. No real user profile or desktop is used by the headless tests (the suite's isolation fixture supplies `CCP_USERDATA_DIR`).

## Assumptions

- Keep the smallest WPF-equivalent diff for the two independent visible gaps; defer host-dependent behavior as a named, honest stub rather than claim wired.
- Reuse the existing uncached `ModArt` resolver and release plate bitmaps on repaint/detach; no new resolver/cache abstraction.
- Reuse MessageDialog's established appearance; preserve the WPF error title/message and OK behavior, with panic teardown required by the port.
