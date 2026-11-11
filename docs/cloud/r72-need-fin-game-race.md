# Race gap re-cut onto 7.2

Base: 74ac6ce3d5e0ae082c2871c00e864943e15aa2ad, clean local work branch.
Repository: CC-Labs-it/Conditioning-Control-Panel. Initial snapshot aa9918833 was reset after fetching the requested branch.
Old lane: 6716a0acf2ef; own work compared with b8a1ffe9b526 (916 additions, 22 removals).
Overlap inspected: #3286, avalonia-port/cloud-r72-fin4-game-race at 4821600f250a49b31c7f73160fad60e8586f0f28.

Paths below are repository-relative. WPF host means ConditioningControlPanel/Services/Chaos/CaucusHostService.cs.
Avalonia race means CCP.Avalonia/Views/Games/GameWindow.Race.cs.

| Behavior | Result and Avalonia evidence | WPF spec |
| --- | --- | --- |
| Fullscreen request and state echo | Ported: race:79, queued with closed/exiting guard at :208; old race:98 | host:350,512 |
| Favorites from shared ranking | Ported: race:112; old race:146 | host:301 |
| Initial and live Loom GIF/params | Ported: race:114,215,254; old race:148,291,350. Reuses base CCP.Avalonia/Platform/WebAssetServer.cs:379 safe __spirals__ mapping instead of old /ccp.spirals | host:306,652,692 |
| Batch bubbles, all crossed milestones, one highest-boundary popup, quests | Ported: race:171,186; CCP.Core/Services/Progression/AchievementEngine.cs:153. Old race:212,227 and old engine:153 | host:479; ConditioningControlPanel/Services/Progression/AchievementService.cs:428,468 |
| Purchase gate, init, one payout latch, watchdog, teardown/presence | Already on base race:27,95,143,171,188 (base line numbers); current race:26,102,155,235,252 | host:132,255,431,595,692 |
| Track picking, charts, playback | Already on base CCP.Avalonia/Views/Games/GameWindow.RaceTracks.cs:47,272,321; RaceTrackPlayer.cs:24 | host:736,817,958; ConditioningControlPanel/Services/Race/TrackPlayer.cs:1 |
| Kept Escape | Already on base CCP.Avalonia/Views/Games/GameWindow.Escape.cs:23,47 and Views/Windows/MainShellWindow.PanicKey.cs:166. Keep base safety ordering, not the old lane panic edits | host:95 |
| Track stop on run end/exit, video pause, ownership, exit fallback | Covered by #3286 race:95,155,198,210,231 at 4821600f. No duplication here | host:383,435,558,1108,235 |

No WPF source or safety policy changed. No new strings: existing localized bubble milestone keys are reused.
No live parity claim.

## Part B

The selected slice is self-contained. These old-lane behaviors remain absent or partial.
References to old race are at 6716a0acf2ef. Base/current race still has no handlers for the missing message types.

| Remaining behavior | Old-lane evidence | WPF spec and current gap |
| --- | --- | --- |
| Native sfx and audio-only payload | old race:87,90,310 | host:326,392; current race:65 has no sfx/fire-payload handlers. Audio payload must honor strength/duration; do not blindly copy the old phrase-only shortcut |
| Audio-web request and EMI usage | old race:56,57; old WebAssetServer.cs:239 | host:144,148; current race:47 launch has neither |
| Creator mappings, active media merge, sound overrides | old CCP.Avalonia/Platform/DtrhModContent.cs:1, old WebAssetServer.cs:200, old race:143,170 | host:167,285,288; current race:110 uses local manifest and :136 keeps modContent null |
| Meta payout state | old race:204 | host:158,459 (DtrhMetaBridge award publishes state); current race:173 only sends payout-result |
| Bug report | old race:106 | host:347,528; current race:65 has no report-bug |
| Ready focus | old race:139 | host:262; current race:102 does not focus |
| Season feature event | old race:118 | host:338; current race:87 only logs start |

Missing in both base and old lane: BambiCloud browser/clock/chart swap (host:1117; ConditioningControlPanel/Services/Race/RaceCloudWindow.cs:1), Back Room return/mystery card (host:127,280; current race:133 returnToCasino=false).
Immediate panic remains base behavior. Old graceful panic is not copied.

## Regression checks

- Tests/CCP.Avalonia.Tests/GameWindowRaceGapTests.cs:24: fullscreen enter/leave echo and no queued reply after close.
- Same file:47: initial Loom frame, repeated-ready single subscription, live params, actual safe URL resolution, silence after close.
- Same file:75: favorites shared ranking, explicit in-memory store restoration.
- Same file:94: 95 + 255 bubbles gives 350 and three points, visible popup, quest update once, duplicate end silent, XP failure isolated, popup closed with race.
- Tests/CCP.Core.Tests/AchievementEngineTests.cs:33: batch persistence, every crossed milestone, nonpositive refusal, achievement threshold unlock persisted.
- Headless setup and per-test isolation reuse GameWindowTests.RunRace and TestIsolationTests.cs.

## Fail-proofs and commands

The four focused headless tests passed first (4 passed, no skips).
Temporarily removing fullscreen handling, favorites posting, the live Loom subscription, and bubble credit produced four failures (4 failed, 0 passed).
Fullscreen expected FullScreen but got Normal; favorites had no frame; Loom had no change frame; bubble credit had no quest batch.
Changing Core batch addition from count to 1 failed the milestone test (expected 3, actual 0).
All mutations were restored in finally blocks. The final suite runs against restored production code.
The strict existing boot test now requires init, manifest, loom-list in order.

Commands use /tmp/dotnet/dotnet (10.0.403).
The requested dotnet-install --channel 10.0 attempted ci.dot.net and received 403; --version 10.0.403 --azure-feed https://builds.dotnet.microsoft.com/dotnet succeeded.

| Command | Result tail |
| --- | --- |
| dotnet build CCP.Core | 50 warnings, 0 errors |
| dotnet build CCP.Avalonia | 201 warnings, 0 errors |
| dotnet build Tests/CCP.Avalonia.Tests | 147 warnings, 0 errors |
| dotnet test Tests/CCP.Core.Tests --no-restore | Passed: 4636, Failed: 0, Skipped: 6, Total: 4642, 57 s |
| bash .github/scripts/core-guards.sh | All Core guards passed |
| python3 .github/scripts/avalonia_docs.py | avalonia_docs self-check OK |
| git diff --check | exit 0 |

Core's existing skips are five Vosk tests and the board frame budget.
The first guard attempt lacked writable temporary files under the read-only sandbox; rerunning with write permission passed.
The first headless run caught the old boot frame expectation and an event-observation error in the new quest test. The assertion now observes the batch directly and the existing boot assertion strictly includes the new frame. No tests were skipped or weakened.
No credentials or secret files were inspected.

Final restored-head command:
dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter 'FullyQualifiedName~GameWindowTests.Race_|FullyQualifiedName~TestIsolationTests'
Result: Passed: 22, Failed: 0, Skipped: 0, Total: 22, 51 s. Fonts worked; these tests ran, not merely compiled.

## Independent review fixes

The original port citations above refer to a54b3cf03. Both P2 findings are accepted.

- Fullscreen restoration: CCP.Avalonia/Views/Games/GameWindow.Race.cs:84 captures the pre-entry WindowState only on entry and restores it on exit. WPF ConditioningControlPanel/Chaos/ChaosWebViewHost.cs:371 restores Maximized and :598 ignores repeated requests. Tests/CCP.Avalonia.Tests/GameWindowRaceGapTests.cs:25 now tests Normal and Maximized, repeated enter/leave requests, and closed queued work.
- Popup failure isolation: the same Avalonia race file:214 catches notification failures before quest credit at :224. WPF ConditioningControlPanel/Services/Progression/AchievementService.cs:468 catches construction and display failures. The existing bubble credit test now covers successful notification, constructor failure, and display failure, asserting points/totals and exactly one quest batch despite duplicate run-end messages.
- Factory and display failure seams belong to the window instance; they add no shared test state. No new strings or WPF edits. The game-race ledger row remains partial, with no live parity claim.

Review-fix validation commands (SDK /tmp/dotnet/dotnet, 10.0.403):

| Command | Result tail |
| --- | --- |
| dotnet build CCP.Core --no-restore | 50 warnings, 0 errors |
| dotnet build CCP.Avalonia --no-restore | 201 warnings, 0 errors |
| dotnet build Tests/CCP.Avalonia.Tests --no-restore | 147 warnings, 0 errors |
| dotnet test Tests/CCP.Core.Tests --no-restore | Passed: 4636, Failed: 0, Skipped: 6, Total: 4642, 42 s |
| dotnet test Tests/CCP.Avalonia.Tests --no-build --filter 'FullyQualifiedName~GameWindowTests.Race_\|FullyQualifiedName~TestIsolationTests' | Passed: 25, Failed: 0, Skipped: 0, Total: 25, 1 m |
| bash .github/scripts/core-guards.sh | All Core guards passed |
| python3 .github/scripts/avalonia_docs.py | avalonia_docs self-check OK |

Review fail-proof command: dotnet test Tests/CCP.Avalonia.Tests --no-restore --filter 'FullyQualifiedName~GameWindowTests.Race_FullscreenEchoAndClosedQueue|FullyQualifiedName~GameWindowTests.Race_BubbleBatchCreditsOnceEvenWhenXpThrows'.
Forcing restore to Normal and rethrowing popup failures produced 3 failed, 2 passed, 0 skipped: Maximized expected but Normal observed; construction and display failure cases lost the quest batch. Source was restored in a finally block.
The same command against restored source passed 5, failed 0, skipped 0 (24 s). The 25-case race/isolation suite also passed before mutation. No tests weakened or skipped.
