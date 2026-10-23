# fin-game-race

- Base: `b8a1ffe9b52690a11318dd06a496794253029e69` (`avalonia-port/cloud-base-chaos-payout-fallin-b`). Verified the exact commit in the requested independent clone fallback after the supplied snapshot's fetch failed on read-only Git metadata.
- Add fullscreen request/ack, race-owned bug report, first Escape as pause, graceful panic exit with the WPF 1200 ms watchdog, mandatory-video pause/resume and keyboard return, and local-track stop at run-end/page exit.
- Boot with local videos and page-recognized root-relative media URLs, top-12 favorites, saved spiral parameters/GIF URLs, and live spiral updates with teardown. Serve only Core-whitelisted spiral GIF names; reject links outside that library.
- Request the lazy audio-web pack at launch; serve downloaded race MP3s through the existing authenticated/ranged asset server. Native sfx use mod overrides, ContentLocator and CoreAudio. Audio payloads reuse CoreSubliminal; refuse video and other visual payload kinds as WPF does.
- Post camelCase meta revisions before payout-result. Batch bubble credit persists achievement progress, credits every 100-bubble point milestone, publishes settled credit, shows one highest-boundary popup with a Fluent sparkle, and advances quests. XP failures do not suppress payout-result or bubble credit. First run-start records day-log usage; launches record EMI usage once.
- Adapt the WPF creator-mod init/media helper: tint, drift pools/caps, portrait/emotes, drone validation, mix/replace media, encoded same-origin mod URLs and bounded serving; add font-independent tests.
- Keep the existing `games` panic registration; its graceful close and final teardown close the report and milestone instances. No new top-level host or effect runner.
- Add focused Core and headless tests; strengthen boot/payout assertions for added frames. Core bubble test failed before implementation (expected 3 milestones, got 0). HTTP audio-pack regression failed before the fix (expected 206, got 404).
- Adapt generic sfx resolution/playback from trunk commit `4a6a258119ad8c7e73f6060ec0c7775882b192e2`. Consulted trunk race-host commit `dcf4482fb22e8677600017be7ea96c6643f51aaf`; no host file copied or overwritten.

## WPF comparison and remaining work

Reference is pinned to the base SHA above: `ConditioningControlPanel/Services/Chaos/CaucusHostService.cs`, `Services/Race/*`, `Services/Chaos/DtrhMetaBridge.cs`, `Services/Chaos/DtrhModContent.cs`, `Services/Chaos/EffectPayloadFiring.cs`, `Services/Progression/AchievementService.cs`, and `Assets/web/dtrh/race.html` (service paths relative to the WPF project).

The original row repeated superseded history. Local playback/decoding/charting, the gated launcher, heartbeat, presence, and XP/Sparks already existed. These were the actual missing behaviors at the base:

| WPF behavior | Result |
| --- | --- |
| Focus at ready; init, complete local images/videos manifest, favorites, loom-list in WPF order | Implemented using the existing Core scanner |
| Root-relative web audio from lazy audio-web pack; native sfx with mod overrides | Implemented |
| Audio-only fire-payload through the existing subliminal surface; refuse visual kinds | Implemented |
| Owned report dialog; fullscreen toggle and echo | Implemented |
| Kept Escape pause; graceful close/exit-done; 1200 ms forced close | Implemented |
| Pause under mandatory video; resume and reclaim keyboard | Implemented at existing native-video show/close boundary |
| End/page-exit stops local audio; meta before payout-result; XP error isolation | Implemented |
| Batch bubble/point/quest credit and one milestone popup | Implemented |
| SeasonRecap TrackFeature on first run-start; EMI NoteOpen | Race is outside the season badge Catalog, so TrackFeature only notes day-log e_rc: implemented. EMI usage implemented; target-open event/suggester bus remains a dependency |
| Cloud levels and BambiCloud window | Missing: URL/track ownership checks, background/foreground opening, hide/reopen session, request-play, audio-element watcher/failure timeout, cloud clock/play/pause/end, source swaps preserving the next lap, denied-source retry after purchase, cloud-ID/hash authored/cache doors, HEAD/Range hash probing, download/retry/progress/cancellation, analysis and temporary-file cleanup |
| Back Room mystery card and returnToCasino/shared-host return | Missing: depends on unported Back Room host/cabinet |
| Grants gained post race-ownership; grants removed close gracefully | Missing: svc-prize-ownership has no live ownership snapshot/change feed; Release still has no real racing-purchase launch path |
| Creator-mod init payload, media mix/replace, mod/art/content origins | Mod payload, merge and mod origin implemented; shared art/content origin mappings remain missing |
| XP skill multiplier in payout-result | Missing: ChaosRunState.SkillMultProvider is unseeded on this head, so the displayed skill multiplier remains 1.0 (shared DtRH/head progression seam) |
| WPF SafeReducedMotion caps to the OS animation switch | Missing: this head has no shared OS animation-preference seam; race still uses the app Motion setting |
| Developer --race-track/--race-cloud scripted drives | Missing: no matching Avalonia CLI drives; normal local-track path already exists |

Keep game-race **stub**, with one row and an explicit Missing list. This is not a dependency-only NOOP: the small host/audio/media/accounting gaps were independently implementable. Full cloud support and focused tests exceed this unit's 1000-added-line budget: WPF's cloud window alone is 519 lines, its host section exceeds 400 lines before adaptation/tests, beyond the space remaining after this unit. No P122 claim: remaining WPF cloud/room/grants paths have live callers.

## Verification limits

- SDK 10.0.402. Release builds of CCP.Core, CCP.Avalonia and all 12 Linux-capable test projects listed in build.yml passed (exit 0, 0 errors): Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.Tests, Avalonia.CatalogueStartup.Tests, Avalonia.Consent.Tests, Avalonia.GeneralSettings.Tests, Avalonia.Language.Tests, Avalonia.Migration.Tests, Avalonia.Report.Tests, Avalonia.Scheduler.Tests and Avalonia.Snapshot.Tests (each with the CCP prefix).
- WPF test project Release build passed (exit 0, 0 errors) with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false. Windows code was compiled, not run. Build sweeps used CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true and CreateHardLinksForAdditionalFilesIfPossible=true solely to avoid duplicated generated content exhausting temporary disk.
- Full Core suite: 3553 passed, 5 existing Vosk tests skipped, 0 failed. The Vosk model is unavailable in this environment; no skip/test behavior was changed. Ran with DiffEngine_Disabled=true.
- Final font-independent filter FullyQualifiedName~RaceModContentTests|FullyQualifiedName~WebAssetServerTests: 10 passed, 0 skipped, 0 failed (exit 0), including pack ranges/auth, spiral bounds, mod payload/media and launch-pinned mapping. Ran with DiffEngine_Disabled=true.
- GameWindowTests, including the new partial race cases, are compiled only. This base lacks the cloud-test-fonts lane; the task documents font creation failures in this runtime. Supervisor rcheck must execute these UI tests, existing RaceTrackPlayerTests and render-all.
- Core guards passed: no forbidden Core APIs beyond the existing baseline; 429 XAML references checked. The read-only command sandbox initially prevented temporary files; retry with temporary-file access passed.
- The first build sweep exhausted temporary disk while duplicating native dependencies. Removed only ignored generated test outputs, then retried with hard-linked copies and output cleanup between projects.
- No live WebKit/WebView2, BambiCloud, audio-device, fullscreen, global keyboard, Windows desktop or real-user-profile execution. Executed tests use repository sandboxes.

## Assumptions

- Preserve the existing GameWindow/Core seams instead of replacing them with trunk's larger RaceWindow stack. Generic sfx follows the credited trunk commit; creator-mod content is adapted directly from the pinned WPF reference. Trunk stayed read-only.
- Use Avalonia native fullscreen state for WPF's borderless toggle and echo resulting state.
- Hook video coverage where this head already pauses bouncing text; avoid a polling clock or retained window subscriptions.
- Leave cloud=false and returnToCasino=false until complete user paths exist; do not fabricate a live ownership event.
