# fin5-shell-video-playback

- Restored HelpVideoWindow clip playback using the existing InlineLoopVideo and shared VlcFrameSink, rather than another decoder abstraction. Matches WPF Loaded timing, muted hardware decoding, Uniform letterboxing, looping, missing-file/native-unavailable fallback and close/replacement teardown.
- Registered the help-video singleton in PanicSurfaces and its safety-critical test inventory. Added real-decoder headless coverage for playback/looping/replacement/panic cleanup and a missing-clip fallback/panic-close test. Reused AppFonts registration in the two video test fixtures to make bundled fonts available regardless of test initialization order.
- Kept shell-video-playback **stub**, replacing repeated historical notes with the current implementation and explicit gaps. Added numbered tester steps.
- Import source fetched read-only: parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No code imported; this change reuses the pinned base implementation.

## Pinned reference audit

Reference SHA: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`.
Read the row and its four WPF references: VideoService.cs, DualMonitorVideoService.cs, InlineLoopVideo.cs and MiniPlayerWindow.xaml.cs. Also compared HelpVideoWindow.xaml.cs and the enhancement head.

The row's repeated Missing notes were stale: PackMediaPool is already connected in MandatoryVideoOverlay's constructor and MandatoryVideoScheduler.PickNext; Tick already retries DND after 30 seconds; InlineLoopVideo already plays through VlcFrameSink. MiniPlayer already provides local image/GIF/video preview, play/pause, seek, clock, looping and close. Mandatory playback already provides the local schedule, pre-roll, screen selection, attention verdict/replay, strict keys, grace card, blur, ducking and watch credit. Help clips were the isolated user-visible gap addressed here.

Remaining WPF behavior, grouped by owning path:

- VideoService.StartWedgeWatchdog/WedgeWatchdogTick: off-dispatcher heartbeat, staged rescue and strict-close fail-open; Stop/CloseAll/ReleaseManagedPlayer: bounded asynchronous stops, quarantine, retire budget/cooldown, managed player leases and deferred retire; DispatchVoutHeal: replay once on a fresh shared instance, carrying max-cap time. Current guard retries on the same shared instance and synchronous Stop can still wedge the UI.
- TriggerVideo/ContinueTriggerVideo: cascade defer/coalescing with 90-second ceiling, visible-feed defer (ghost bypass, earned-trigger bypass and explicit-button toast), interaction queue ownership/release, stale trigger recovery and cancellation tokens. The Avalonia cascade exists but has no RunWhenClear host path; For You and its queue/browser ownership are not implemented here.
- GetNextVideo/PlayUrl: mod/remote URL selection, browser playback arbitration, browser-to-LibVLC fallback and DualMonitorVideoService URL takeover. Pack/local selection is already wired.
- Fullscreen transport controls and floating trigger/window controls, primary transport/seek and random-segment clocks, enhancement-driving synchronization and enhancement audio transport. The enhancement window still has an audio-host stub; the local/allowlisted video preview and script transport already work when WebHost has an engine. Completing enhancement rule-clock binding, audio transport and consent is a separate substantial dependency.
- Gaze-click attention adapter, native non-activating z-order policy, session lock/suspend teardown and external mute propagation. Live master/video volume is already wired through AudioSettingsBinder -> MandatoryVideoOverlay.UpdateVolume.
- Gentle playback rate and curtain enter/natural-end exit fades with wall-clock guard conversion; VideoAboutToStart/VideoEnded/VideoWatchCredited integration events, deferred subliminal reset after cleanup and video/idle Discord presence transitions. Core already handles video haptics/funscript start and stop; these are not missing.
- InlineLoopVideo still restarts after popup detach/reopen instead of retaining WPF's paused player; hidden ancestors require the host to call Pause. These are existing documented lifecycle deviations.

The native safety host alone spans the shared LibVLC lifetime, all leased players, recovery timers and platform window rescue; porting it and the browser/feed/enhancement hosts with focused tests cannot fit this unit's 1000-added-line cap. No P122 dead-caller exemption is claimed and the row remains stub.

## Verification limits

Commands use .NET 10, Release configuration, and `DiffEngine_Disabled=true` for tests.

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0.
- `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -m:1 -nr:false`: exit 0, 0 errors (final rebuild).
- Every Linux-compatible test csproj named in build.yml compiled successfully: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler and Snapshot. CatalogueStartup first hit MSB4166 (worker exited); retry with `-m:1 -nr:false` passed with 0 errors.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- First `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 1; 3951 passed, 5 skipped, 2 unrelated failures (BoardTiles frame budget: 17.80 ms; DtrhRemoteMedia expected Loops / actual Clips). No Core code was changed. Targeted rerun of BoardTilesTests and DtrhRemoteMediaTests: exit 0, 76 passed. Full-suite retry with no other active builds/tests: exit 0, 3953 passed, 5 Vosk-model-dependent tests skipped, 0 failed.
- Initial filtered `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~HelpVideoWindowTests|FullyQualifiedName~InlineLoopVideoTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~MandatoryVideoOverlayTests'`: exit 0; 11 passed, 4 skipped. The 4 real-decoder cases (including the new tutorial loop test) use the existing helper and skipped because LibVLC is unavailable.
- Before the test-font setup fix, the filtered rerun after adding the native-independent panic-close assertion, `--filter 'FullyQualifiedName~HelpVideoWindowTests|FullyQualifiedName~PanicSurfacesTests'`: exit 1; 7 passed, 3 failed in font creation (`Could not create glyphTypeface`, Inter / Segoe UI Black), including an unchanged existing shell test. This reproduced the documented font initialization failure. Applying the same bundled AppFonts setup already used by PanicSurfacesTests to HelpVideoWindowTests and InlineLoopVideoTests fixed it. Final rebuild: exit 0, 0 errors. Final broader filtered run (same four classes as above): exit 0; 11 passed, 4 LibVLC-dependent cases skipped. No assertions were weakened and no tests were disabled.
- `git diff --check`: exit 0. This base has no `.github/scripts/test-plan-coverage.sh`; numbered tester coverage was added manually.

Conditional WPF attempt: `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. Stopped with SIGTERM after over eight minutes when CPU time stopped advancing after the temporary markup compilation; the process reported exit 0 on termination, but produced neither a build-success summary nor a test DLL, so this is **incomplete, not a successful build**. Windows code cannot be run here. Other test projects were compiled only. No render-all run was attempted. Tutorial assets are no longer shipped for the standard topics, so real playback coverage supplies a generated local test clip. No new assets or dependencies are added.

## Assumptions

- Preserve drawn-loop priority and support ClipFile when a caller provides an existing clip; do not invent tutorial assets deleted upstream.
- Reuse the existing InlineLoopVideo lifecycle, including its synchronous native teardown limitation; document the remaining native safety host rather than partially replacing the process-wide shared LibVLC.
