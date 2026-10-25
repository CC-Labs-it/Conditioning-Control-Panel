- Add flash voice selection/playback through the existing Core audio seam and LibVLC backend. Reuse the mod-aware companion catalogue, union install/content-pack clips with recursive extras, honour phrase toggles and custom voiced phrases, and deal a shuffled pool.
- Match master/avatar/voiceline mute gates, the WPF gain floor and companion voice level. Voice level zero keeps a silent clip running for audio-linked image timing. Play once per burst; hydra children remain silent and retain their parent's audio XP rate.
- Use reported clip length + 1 s for flash lifetime, preserving ambient stay-until-popped safety. Award 8 rather than 4 XP when voice starts; keep existing hydra/lucky multipliers. Stop/panic retires pending audio, stops the clip and restores its duck lease. Automatic unduck remains scheduled for length + 1500 ms and is generation-bound/idempotent.
- Add lucky gold blur/opacity pulsing on the existing frame/motion clock and sibling Border BoxShadow; closing the last flash stops the shared clock. Port the single reused LUCKY! 10x XP toast (200 ms fade-in, hold to 3 s after last proc, 300 ms fade-out), with perk/game suppression and explicit `lucky-flash-toast` panic registration plus Stop/shutdown cleanup.
- Keep one honest `feat-flash` ledger row at `stub`; add numbered tester steps and focused Core/headless behavior tests.
- No trunk code imported. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; existing base implementations won. All adaptations here follow WPF at base `559f9cbac6348b6a6a3e90e57e89131f28cf4e12`.

## WPF gap inventory and remaining scope

The original ledger mixed its current summary with obsolete gap lists. The reference control and service were compared at the pinned base. The following were still absent on this head before this unit:

1. Flash voice: library/recursive/custom pool, filtering, shuffled selection, mute/gain controls, one playback per burst, duck/restoration, audio-derived duration and XP. Implemented here; avatar speech/bark coordination remains below.
2. Remote GIF/still/video-clip sources: warming/prefetch, source-weighted selection, byte/clip cache and decode, retries/fallback and the empty-library source offer (`FlashService` EnsureRemotePrefetch, LoadRemoteImageAsync, CurrentRemoteClipPath, OfferRemoteMediaSource). Still missing.
3. Gaze: target enumeration and rect updates, dwell feedback, gaze-origin pop with hydra restrictions, linger lifetime extension and feedback (`GetGazeTargets`, `GazePop`, `FlashWindow.BoostLifetime`, GazeFocusService). Still missing; the existing gaze settings do not implement these paths.
4. Avatar: pre-announcement before the 1 s delay, click-category reaction, spoken-clip bubble text and whisper/bark suppression (`AvatarTubeWindow.Reactions` OnFlashAboutToDisplay, OnFlashClicked, OnFlashAudioPlaying; `FlashService.ShowImages`). Still missing.
5. Lucky: halo pulse and shell lucky toast. Pulse and the flash toast presenter/proc delivery implemented; bubble proc delivery is outside this row.
6. Natasha favourite: idle/ownership/Chaster eligibility and roll, red halo pulse, dodge ring/timer, click/fling escape and booking the consequence (`StartNatashaDodge`, `OnFlashClicked`). Still missing; requires the live Chaster host rather than inventing a local consequence.
7. Jackpot Remix: owned toggle/box visibility, quiet prebuild/cache and ambient-only roll/carry, builder cancellation and centred multi-monitor playback with mirrors excluded from lucky/hydra (`EnsureRemixDirector`, `ShowRemix`). Core director rules exist, but the head builder/playback integration is absent. Still missing.
8. Solid rendering: shared-host pictures, passive mouse behavior, gaze-only interaction, path-specific caps/lifecycle and static motion (`MouseClickable`, `SpawnFlashWindow`). Still missing; the toggle remains settings-only.
9. Real prize delivery remains the separate `svc-prize-ownership` dependency; existing flash ownership refresh is preserved.

These are live WPF paths, so P122 does not apply. Completing the remaining source/cache/video, gaze, avatar, remix-builder, solid-host and Chaster integrations together cannot fit this unit's 1000-added-line cap with behavior tests. This unit prioritizes audible/timing/XP behavior and the visible lucky pulse. It does not claim the row is finished or wired.

## Assumptions

- Re-enumerate and filter the voice pool before each draw so a removed/disabled clip cannot remain queued. Reuse the existing ShuffleBag rather than copy WPF's separate queue implementation.
- A failed audio start leaves authored image timing and silent XP. A player with unknown duration uses WPF's five-second fallback; a two-second bounded start wait prevents a failed device from holding the burst busy. This is a deliberate small head adaptation.
- Existing Stop/panic ownership covers audio and pulse; the new toast is explicitly registered and covered by panic tests.

## Verification limits

- `dotnet build <project> -c Release`: exit 0, zero errors for Core, Avalonia and all 13 workflow test projects (15 builds total). WPF tests built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; Windows code compiled but was not run.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final quiet run exit 0; 4404 passed, 5 existing Vosk tests skipped, 4409 total. One concurrent run failed the unrelated `BoardTilesTests.A_frame_fits_the_budget` timing check (26.82 ms); the earlier and final quiet runs passed. No tests were weakened or skipped by this unit.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~FlashVoiceTests`: exit 0; 6 passed, 0 skipped.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~FlashWireTests|FullyQualifiedName~FlashFinishTests|FullyQualifiedName~FlashPopTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0; 38 passed, 0 skipped. Headless fonts work on this snapshot.
- Negative proof: temporarily restoring silent-only XP and disabling the pulse made both focused head tests fail (expected exit 1: 4/40 versus 8/80 XP, static blur 18 versus peak 28.8). Restoring the implementation passed the suite.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The optional `test-plan-coverage.sh` is absent on this base (attempt exit 127); numbered WPF tester steps were added directly.
- Real audio output/device changes, external process ducking, native window placement and Windows desktop behavior were not exercised; playback/duck contracts use fake providers and visual tests use the existing isolated headless harness. Windows desktop behavior and real audio hardware cannot be exercised in this Linux headless environment. Remote/Vosk model download restrictions remain unrelated to this change.
