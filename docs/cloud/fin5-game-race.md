# fin5-game-race

- Base: `472c8a71b2b1e29c0c560acb8e3efa711a1acf0f` (the required snapshot, checked before branching).
- Adapted WPF `Services/Chaos/CaucusHostService.cs`: page fullscreen/state echo, owned bug-report modal, sound cues, ordered favorites boot frame, immediate audio stop on accepted run-end and exit.
- Connected race to the existing Core Escape policy and shared kept-escape frame. A second Escape within two seconds still panics; the existing games panic surface also closes the owned report.
- Added six focused headless regression tests; retained the existing race and DtRH pause tests. Favorites fixtures restore process-global store state and sandbox files.
- Consolidated the duplicated `game-race` ledger history into one honest `stub` row and added numbered WPF-linked tester steps.
- Read-only reference fetch: `parity/trunk` resolved to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk files were imported or overwritten; changes adapt the WPF reference on the pinned base.

## WPF comparison and remaining scope

The row is **not finished** and must not be promoted to wired. Existing launch/account/leash gating, local chart/cache/player/decoder, run payout latch, watchdog and panic teardown remain the starting implementation.

The following WPF behavior was absent when this unit began:

| Behavior | Result in this slice |
| --- | --- |
| Page fullscreen toggle and state echo | Connected |
| In-game bug report owned by race | Connected; dialog closes with the game |
| First Escape pauses, repeated Escape panics | Connected through the existing policy |
| Host sound cues/default scale/master volume | Connected to existing ChaosSfx; its existing mod-audio-override limitation remains |
| Favorites after manifest, omitted for empty store | Connected |
| Keyboard focus at ready | Connected |
| Immediate local audio stop at run-end and page exit | Fixed |
| BambiCloud levels/browser/track observation/clock/pause/download/probe/chart/cache/error/retry/ownership enforcement | Missing; cloud remains disabled |
| Back Room mystery cabinet and shared host navigation/returnToCasino | Missing; requires the Back Room game host, returnToCasino remains false |
| Native audio fire-payload, video refusal and scaled strength/duration | Missing; App's global payload firer is still a logging stub |
| Loom list, sidecar params, live save/delete updates, spiral URL mapping | Missing |
| Mandatory video pause/resume and focus recovery | Missing; scheduler exposes VideoStarted but lacks the matching VideoEnded event |
| Mid-session ownership additions/revocation and race-ownership frames | Missing; PrizeOwnership has no change event |
| DtrhMetaBridge meta snapshot/lifecycle | Missing |
| Global bubble batch, unlock, sparkle milestones, quest credit and persistence | Missing; AchievementEngine lacks the WPF TrackBubblesPopped host method |
| SeasonRecap TrackFeature | Missing; recap models/store exist but TrackFeature service does not |
| Creator mod init payload/manifest merge and art/spiral/mod origins | Missing |
| Launch EMI desk NoteOpen and lazy audio-web pack request | Missing |

The cloud dependency is a substantial host port: WPF RaceCloudWindow is 519 lines, the CaucusHostService cloud section spans approximately 420 lines, and CloudTrackClock plus regression tests and Avalonia browser adaptation are additional work. Porting all remaining behavior with adequate tests does not fit the 1,000-added-line unit budget. This slice prioritizes directly reachable controls and playback lifecycle. It is not a dependency-only NOOP and does not apply P122: WPF has live callers for these behaviors. Developer-only automatic drive/log switches are diagnostic paths, not shipped launch controls.

## Verification limits

- SDK: .NET 10.0.100. Channel installation failed at the latest-version endpoint (403); the pinned SDK download succeeded.
- `dotnet build -c Release`: Core, Avalonia and every Linux test csproj referenced by `.github/workflows/build.yml`: all succeeded with zero errors.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, unavailable on the pinned base (script does not exist). Numbered tester steps were added manually.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 0, 4,409 passed, 0 failed, 5 existing Vosk tests skipped (model unavailable).
- Negative control: all six new tests failed against the pinned base host (exit 1, 6 failed / 0 passed), proving the missing behavior. Final filtered `GameWindowTests.Race_*` plus `Dtrh_Escape` regression run: exit 0, 13 passed / 0 failed / 0 skipped. `DiffEngine_Disabled=true`; tests use the existing CCP_USERDATA_DIR sandbox and memory-only SecretStore.
- Windows-targeted WPF test compilation with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: succeeded, 0 errors (557 existing warnings). Windows execution and live browser/audio/desktop checks are unavailable here.

## Assumptions

- Prefer the existing GameWindow, report modal, ChaosSfx player and Core panic policy; do not introduce a second host or speculative service layer.
- Avalonia WindowState.FullScreen is the existing platform-owned fullscreen operation; echo its resulting state without using the browser Fullscreen API.
- Retain `stub` and list gaps explicitly instead of claiming parity for hidden or unreachable controls.
