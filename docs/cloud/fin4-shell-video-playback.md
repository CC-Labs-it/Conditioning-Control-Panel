# fin4-shell-video-playback

- Base: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a` (checkout verified before implementation).
- Connect HelpVideoWindow to the existing InlineLoopVideo decoder after opening. Preserve WPF's silent looping playback, letterboxing, drawn-loop precedence, caption fallback, single-instance replacement and teardown. Register panic cleanup.
- Port cascade deferral into MandatoryVideoScheduler: one coalesced request, 90-second absolute expiry, 250 ms clear probe, original clip/strict override retained, scheduled feature recheck, stop/force-cleanup cancellation. The head supplies its existing cascade's IsRaining probe.
- Replace the repetitive historical ledger text with current behavior and an honest Missing list; retain `stub`.
- No trunk code imported. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation adapts the pinned WPF reference and existing Core/head code.

## WPF audit and scope

Reference paths below are relative to ConditioningControlPanel at the pinned base.

- `Services/Video/VideoService.cs`: schedule, local/pack selection, duration filtering, pre-roll, volume, ducking, full-screen monitors, strict/grace keys, click/toy attention, verdict/replay, watch credit and played-time cap already have Core/head paths. Scheduled DND already retries after 30 seconds. Cascade guard (`1916`, `2229`) was missing and is implemented here.
- Remaining native safety: `RetireSharedLibVLC`, managed/mandatory wedge watchdogs, blur-surface recovery, off-thread bounded stop/quarantine, audio reprobes, system lock/suspend cleanup. The current one-replay guard still uses the same shared instance and synchronous native Stop. The WPF native recovery implementation spans multiple watchdogs, shared consumers and platform escape hatches; copying only a timer would not provide its guarantees.
- Remaining coordination/media: For You visible/ghosted deferral (with explicit earned/test announcement), interaction-queue claim/release, cancellation tokens for chaos runs, browser-media deferral/takeover, browser-to-LibVLC fallback, URL playback, remote consent/channel ratio/prefetch/cache/materialization and the remote offer after empty-library guidance. No live Avalonia For You host or interaction queue exists. Core pack selection **does** exist and must not be overwritten by an import.
- Remaining presentation: floating mandatory videos and multi-instance limits/together/join/speed/sound rules (`VideoService.Floating*.cs`), mute/close controls (`VideoService.Controls.cs`), gentle speed/curtains, gaze attention targeting and no-activate/click-raise handling. Core has several of these pure rules; the mandatory host does not consume them yet.
- `Services/Video/DualMonitorVideoService.cs`: mandatory local clips already share one frame sink across targeted screens. The separate URL/file takeover service and its bounded recovery/audio routing are not supplied by that mandatory path; remote takeover remains missing.
- `Controls/InlineLoopVideo.cs`: existing head already handles silent looping, pause/resume, attach/detach and buffer cleanup. HelpVideoWindow was still claiming no surface existed; this unit reuses it instead of adding another player implementation.
- `Windows/MiniPlayerWindow.xaml.cs`: existing head has video/GIF/image loading, transport, seek/time display, drag, keyboard close/play controls and disposal; no additional mini-player gap found in this audit.
- Enhancement video already navigates and polls its browser; browser fullscreen and zoom remain stubbed in EnhancementPlayerWindow. Its browser-host completion plus the missing native recovery, remote/For You host and floating presenter cannot be completed responsibly with their behavior tests within this unit's 1000-added-line cap. User-visible independent gaps were prioritized; this is a partial implementation, not a dependency-only NOOP or a wired claim.

## Assumptions

- Reuse InlineLoopVideo for tutorial decoding. Its native repeat option and EndReached fallback provide the existing WPF loop behavior without another lifecycle.
- Poll the cascade probe every 250 ms because the head exposes IsRaining but no RunWhenClear subscription. Keep WPF's absolute 90-second ceiling; do not reset it on coalesced requests.
- Existing direct Trigger calls are earned/manual; scheduler ticks explicitly mark their requests unearned so the feature-enabled recheck applies only to scheduled requests.

## Verification limits

All commands used .NET SDK 10.0.100; test runs set DiffEngine_Disabled=true. No real desktop or user profile is used by the headless tests (the assembly's TestUserDataProfile sets CCP_USERDATA_DIR and disables desktop polling).

- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia, Tests/CCP.Avalonia.Tests and every Linux test project in build.yml: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests and CCP.Avalonia.Snapshot.Tests. Final changed Core/head/test assemblies rebuilt successfully.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0; final result 3957 passed, 5 skipped, 0 failed (3962 total). Existing Vosk tests skip without their unavailable model.
- Cascade regression proof: with the new cascade branch disabled, `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~Cascade` exited 1 with the three new cases failing. Restored implementation before final full-suite run. Added a second cleanup case covering ForceCleanup too.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~HelpVideoWindowTests|FullyQualifiedName~InlineLoopVideoTests|FullyQualifiedName~MandatoryVideoOverlayTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0; 11 passed, 4 skipped, 0 failed. This worker successfully creates fonts. Native LibVLC is unavailable; the existing decoder-fixture prerequisite checks skip the three inline-video cases (including the new help playback regression) and the mandatory-video decoding case. Actual decoding, muted looping and active-player panic cleanup therefore remain unexecuted here; the new help playback regression could not be failure-proven at runtime. No tests were weakened or disabled.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 602 warnings, zero errors. WPF/Windows behavior compiled but was not run on Linux. WPF reference source was not edited.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script is absent at the pinned base. Numbered WPF/Rows tester steps were added directly.
- `git diff --check`: exit 0. No render-all or live desktop playback run; row remains stub.

The channel-based SDK installer hit a blocked discovery host; the version-pinned 10.0.100 installer succeeded. No credentials or secret files were inspected.
