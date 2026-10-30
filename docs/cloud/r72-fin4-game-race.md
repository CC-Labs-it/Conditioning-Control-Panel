# game-race re-cut onto 7.2

Base: `74ac6ce3d5e0ae082c2871c00e864943e15aa2ad`.
Old lane: `61d2191c5228`, compared with `a65dbbe2f493`.
All WPF paths below are under `ConditioningControlPanel/Services/`.

## Ported safety slice

- Stop audio at run end and page exit: `CCP.Avalonia/Views/Games/GameWindow.Race.cs:95,155`; old lane same file:120,186; WPF `Chaos/CaucusHostService.cs:383,435`.
- Mandatory video pause/resume and focus return: same Avalonia file:198; old:264; WPF `Chaos/CaucusHostService.cs:558-592`. Core cleanup notification: `CCP.Core/Services/Video/MandatoryVideoScheduler.cs:434`, old:431; WPF `Video/VideoService.cs:7950`.
- Accepted counter-snapshot additions refresh tracks; losses request exit with 1200 ms fallback, loading pages close immediately: Avalonia race file:210; old:298; WPF `Chaos/CaucusHostService.cs:1108-1115,235-248,631-638`. `CCP.Avalonia/Platform/PrizeOwnership.cs:61` publishes accepted snapshots outside the lock.
- Listeners detach on close/panic: Avalonia race file:231; old:343; WPF `Chaos/CaucusHostService.cs:692-723`.

## Already on base

- Kept Escape routes Race through the shared policy: `CCP.Avalonia/Views/Games/GameWindow.Escape.cs:23,47`, `CCP.Avalonia/Views/Windows/MainShellWindow.PanicKey.cs:166`; old lane used its race-specific probe at `MainShellWindow.PanicKey.cs:159`; WPF `Chaos/CaucusHostService.cs:95-119`. Keep the base's safety ordering.
- Purchase gate, init/manifest, one payout, watchdog, teardown: base `CCP.Avalonia/Views/Games/GameWindow.Race.cs:27,95,143,171,188`; old:33,129,183,220,343; WPF `Chaos/CaucusHostService.cs:132,255,431,595,692`.
- Local charting and playback: base `GameWindow.RaceTracks.cs:47,272,321`, `RaceTrackPlayer.cs:24`; unchanged by fin4. WPF `Chaos/CaucusHostService.cs:736-1035` and `Race/TrackPlayer.cs`.

## Part B

The old diff totals 750 additions and 28 removals, including shared bubble work. This commit delivers the self-contained safety lifecycle first within the 600 line cap, with focused runnable regressions. Remaining fin4 behaviors, all in old `CCP.Avalonia/Views/Games/GameWindow.Race.cs` unless stated:

- Native audio-only payload and clamps: old:284; sfx:96; WPF `Chaos/CaucusHostService.cs:328-335,395-424`.
- Fullscreen echo: old:103; owned bug report:107; WPF `Chaos/CaucusHostService.cs:347-352,506-549`.
- Favorites and live Loom GIF/params: old:142,323; `CCP.Avalonia/Platform/WebAssetServer.cs:209` supplied the old Loom route; base `CCP.Avalonia/Platform/WebAssetServer.cs:379` already serves safe Loom GIFs under `__spirals__/`, so only the race feed remains; WPF `Chaos/CaucusHostService.cs:292-309,652-680`.
- Meta payout frame: old:198; bubble batch/milestone popup/quest credit:235, with `CCP.Core/Services/Progression/AchievementEngine.cs` batch helper; WPF `Chaos/CaucusHostService.cs:158,459-479` and `Progression/AchievementService.cs:428-485`.
- Audio-web request and EMI usage: old:58-60; WPF `Chaos/CaucusHostService.cs:144-147`.
- Focus on ready: old:133; WPF `Chaos/CaucusHostService.cs:262`.

Still missing in both lane and base: BambiCloud browser/clock/chart swap/refusal (`Chaos/CaucusHostService.cs:1117`, `Race/RaceCloudWindow.cs`); Back Room mystery card and shared-host return (`Chaos/CaucusHostService.cs:127,280`); creator mappings and sound overrides (`Chaos/CaucusHostService.cs:167-184,285`); SeasonRecap feature (`Chaos/CaucusHostService.cs:338`). Panic continues the base's immediate close, as authorized, rather than WPF's graceful close.

Ownership notifications cover accepted counter snapshots. Profile-sync and account-change notifications remain the existing svc-prize-ownership gap.

No WPF source, Scrolller code, localization keys or progress HTML changed. No live desktop/browser parity claim.

## Regression evidence

Focused tests reuse `GameWindowTests.RunRace` and its shared Avalonia setup, with fake audio and clock seams:

- `Tests/CCP.Avalonia.Tests/GameWindowRaceSafetyTests.cs:23`: separate run-end and exit cases. Removing both stop calls gave two failures, each at `Assert.False(player.IsPlaying)`.
- Same file:45: accepted additions, stale revision refusal, lost grant exit request, loading-page close, silence after panic. Removing the subscription failed the expected ownership frame; independently ignoring grant loss produced `race-ownership` instead of `exit-request`.
- Same file:117: pause, resume, silence after panic. Removing the start handler failed on an empty pause frame collection; independently changing resume to pause failed `Assert.False`, actual true.
- `Tests/CCP.Core.Tests/MandatoryVideoSchedulerTests.cs:40`: end fires once, repeated cleanup is silent, a throwing subscriber cannot block cleanup, and stop during pre-roll ends the lifecycle. Removing the end notification failed expected 1, actual 0.

Mutation commands used `dotnet test Tests/CCP.Core.Tests --filter FullyQualifiedName~Clip_lifecycle_notifies_once` (1 failed), and `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~Race_RunEndAndExit|FullyQualifiedName~Race_OwnershipRefresh|FullyQualifiedName~Race_VideoPauses'` (4 failed). The independent resume/loss pass selected the latter two methods (2 failed). Each mutation was restored in a `finally` block before final validation. No test was weakened or skipped to get a pass.

The .NET 10 channel installer fell back to a denied CI endpoint. Retrying `dotnet-install --version 10.0.403 --azure-feed https://builds.dotnet.microsoft.com/dotnet` found the installed SDK. Commands below use `/tmp/ccp-dotnet/dotnet`.

## Initial port commands and result tails

| Command | Result tail |
| --- | --- |
| `dotnet build CCP.Core` | 50 warnings, 0 errors |
| `dotnet build CCP.Avalonia` | 201 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests` | 144 warnings, 0 errors |
| `dotnet test Tests/CCP.Core.Tests` | Passed!  - Failed:     0, Passed:  4636, Skipped:     6, Total:  4642, Duration: 57 s - CCP.Core.Tests.dll (net10.0) |
| `dotnet test Tests/CCP.Avalonia.Tests --filter "FullyQualifiedName~GameWindowTests.Race_\|FullyQualifiedName~RaceTrackPlayerTests\|FullyQualifiedName~TestIsolationTests\|FullyQualifiedName~EveryHostedGame_KnowsItsKeptEscapePage"` | Passed!  - Failed:     0, Passed:    30, Skipped:     3, Total:    33, Duration: 1 m 45 s - CCP.Avalonia.Tests.dll (net10.0) |
| `bash .github/scripts/core-guards.sh` | All Core guards passed |
| `python3 .github/scripts/avalonia_docs.py` | avalonia_docs self-check OK |
| `git diff --check` | exit 0, no output |

Compiled but skipped by existing dependency checks: `RaceTrackPlayerTests.PauseRightAfterPlayIsHonoured`, `DecodeTurnsAStereoFileIntoMono16k`, `EndedOnlyForARealEndingAndOnce` (native LibVLC unavailable). System package installation was unavailable without administrator permissions. Core's six existing skips are the board frame budget and five Vosk speech tests. No live parity check or WPF runtime test was performed.

## P2 review fix

Accepted the finding against c391093ab. WPF `ConditioningControlPanel/Services/Video/VideoService.cs:6052` closes the clip for the attention message without raising `VideoEnded`; only final `Cleanup` at :7950 announces completion. `CaucusHostService.cs:579-592` uses that event to resume the race and return focus.

`CCP.Core/Services/Video/MandatoryVideoScheduler.cs:374,434` now emits once on final sequence completion. Per-clip `Finish` still credits watch time and stops haptics, but keeps the sequence active through the 2000 ms fail/troll message, replay pre-roll and the 2500 ms mercy message. Ending or forcing cleanup during a message cancels its pending replay and emits once, preserving the base's safe force-cleanup resume.

New tests: `Tests/CCP.Core.Tests/MandatoryVideoSchedulerTests.cs:65,104` cover both real verdict branches through mercy and End/ForceCleanup/Stop during the gap; `Tests/CCP.Avalonia.Tests/GameWindowRaceSafetyTests.cs:153` checks actual race pause frames through fail/troll replay and mercy. The existing random field is seeded after library shuffling so the troll path is deterministic without a new production seam.

Fail-proof before the production fix: the five Core cases failed with expected 0 notifications, actual 1; both headless race cases failed because the first verdict already posted a second frame (`pause:false`). All seven cases compiled and ran. Existing tests remain intact. No localization key, WPF source or progress HTML changed.

Review validation uses `/tmp/ccp-dotnet/dotnet` (SDK 10.0.403):

| Command | Result tail |
| --- | --- |
| `dotnet build CCP.Core` | 50 warnings, 0 errors |
| `dotnet build CCP.Avalonia` | 201 warnings, 0 errors |
| `dotnet build Tests/CCP.Avalonia.Tests` | 145 warnings, 0 errors |
| `dotnet test Tests/CCP.Core.Tests` | Passed!  - Failed:     0, Passed:  4641, Skipped:     6, Total:  4647, Duration: 43 s - CCP.Core.Tests.dll (net10.0) |
| `dotnet test Tests/CCP.Avalonia.Tests --no-build --filter "FullyQualifiedName~GameWindowTests.Race_\|FullyQualifiedName~RaceTrackPlayerTests\|FullyQualifiedName~TestIsolationTests\|FullyQualifiedName~EveryHostedGame_KnowsItsKeptEscapePage"` | Passed!  - Failed:     0, Passed:    32, Skipped:     3, Total:    35, Duration: 1 m 53 s - CCP.Avalonia.Tests.dll (net10.0) |
| `bash .github/scripts/core-guards.sh` | All Core guards passed |
| `python3 .github/scripts/avalonia_docs.py` | avalonia_docs self-check OK |
| `git diff --check` | exit 0, no output |

The skips retain the dependency and opt-in reasons listed above. Both new headless race cases and all five new Core cases ran and passed. No live desktop parity check was performed.
