# fin4 shell audio playback devices

- Base/WPF reference pinned at `43827ca298e9c35e5557d04d5581d961d6f666ec`. Read Services/AudioService.cs, its playback split, Audio/LayeredAudioService.cs, MainWindow/MainWindow.HomeAudio.cs and Windows/QuizWindow.xaml.cs.
- Added Windows active WASAPI render endpoint enumeration, retaining synthetic default and partial results after failures; uses WPF's NAudio.Wasapi 2.2.1 with guarded native calls.
- Wired quiz drone through the existing LibVLC overlap loop: mod/content-pack neutral cue then legacy fallback, WPF perceptual gain, surrender mute/restore, close/panic disposal. Explicit quiz panic registration; focused headless lifecycle tests.
- Shared voice players apply preferred output once live, covering quiz/mind-wipe loops.
- Corrected stale missing-production-caller claims; kept the single ledger row stub.
- Read-only trunk fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Behavior adapted from pinned WPF.

## Reference audit and remaining work

The starting row listed missing sound openers, Windows output enumeration/ducking and browser-session exclusion OR. The pinned base already has real openers in PopQuizHost, GradedIntakeTabView, MainShellWindow.SessionRun, ChaosRunHost and BubbleCountHost. App/CompanionEffects/BarkHead/avatar chat also call GigglePriority with sound enabled. P122 does not apply: these are live WPF paths.

Quiz ignored playDrone despite its live checkbox; addressed here. Dashboard mirrors and layered gain/loop already have live implementations and suites.

Still missing: Windows duck sweeps/rescans and process exclusions; persisted crash recovery; pending restores every 5 seconds for 3 minutes with process-name fallback; browser-video-session forced exclusion (requires the unported browser-video host); LibVLC equivalent of WPF endpoint-change/failure circuit-breaker recovery. WaveOut driver compatibility fallback cannot be copied literally to LibVLC. This unit prioritizes visible device and quiz controls; it does not finish full audio recovery parity.

## Verification limits

- `dotnet build CCP.Core -c Release`: exit 0.
- `dotnet build CCP.Avalonia -c Release`: exit 0, zero errors.
- Every Linux test csproj listed in build.yml compiled with zero errors: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, Core.Tests. Consent and Avalonia.Tests passed on retry after interrupted builds.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent from this base. Added numbered tester steps regardless.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~CoreAudioTests|FullyQualifiedName~AudioDuckRescanTests|FullyQualifiedName~QuizDroneTests.EndpointFailure'` with DiffEngine_Disabled=true: exit 0, 8 passed, 0 skipped.
- Quiz drone window lifecycle and PanicSurfaces tests compiled, not run due to the documented font limitation. The drone fix therefore has no executed before/after failure proof here; it needs the supervisor headless run.
- Initial full Core run hit disk exhaustion (25 failed, 2791 passed, 5 pre-existing Vosk skips); generated output cleanup restored space and clean rerun exit 0: 2816 passed, 0 failed, 5 pre-existing Vosk skips (model unavailable).
- Final shipped-head/Avalonia.Tests rebuild: exit 0, zero errors. Final pure audio regression filter: 8 passed, zero failed/skipped.
- Windows-targeting WPF test build with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false: restore completed and WPF temporary project compiled with warnings, but the overall build timed out after 300 seconds (exit 124). WPF build not claimed complete; Windows code compiled in the cross-platform head but not run.
- `git diff --check` and added-text private-path scan passed.
- Build outputs were deleted between test-project builds to avoid copying 1.3 GB of content for every head test project. No real sink, Windows native call or desktop window exercised. Window tests use the existing isolated CCP_USERDATA_DIR/headless host; the documented cloud font limitation prevents running them here. No new animation surface.

## Assumptions

- Keep stub while Windows ducking/recovery remain.
- Reuse the existing 120 ms overlap loop for quiz audio.
- Preserve the localized synthetic default used by the head.
