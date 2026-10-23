- Adapted the subscription mapping and idle pool selection from read-only trunk commit `abc82671e16ae1449b05983b3b76e97d27bad3bc` into the existing engine/head. Existing base files win; no second engine or state implementation was imported.
- Added barks for flash, actual subliminal visuals (including Freeze/Reset), video before/start/end, player XP/levels, companion levels, achievements, quests, mantra, lockdown, Patreon tier transitions, tutorial completion and persistent memory recall. All new subscriptions detach on Stop.
- Published bubble-count success and final answer/mercy failure events. Retries remain silent; a canceled mercy callback cannot bark after panic.
- Added the tube's slider-driven idle timer and WPF's persisted pool-wide no-repeat rotation, last-rule recycle protection, single-rule fallback and 0.35 bias toward eligible gated rules. Muted/busy idle ticks spend nothing. Unprompted bubbles keep text while their sound is held with the engine off unless opted in.
- Added the whisper floor, including the 250 ms tail, monotonic extension, and WPF possession/emergency-exit exemptions. Whisper playback reserves its spin-up floor before its actual duration arrives. Safety and guaranteed barks retain their bypasses.
- Added actual `id@Tier` latch keys and `CCP_BARK_DRYRUN=1`: dry-run exercises in-memory gates without speaking, echoing, persisting latches or persisting rotation.
- Ordinary voiced barks use the preset queue and its earned gap. WPF intentionally drops the clip on the delayed path; the unprompted flag survives that queue. Priority barks retain preemption.
- Added focused Core and headless adapter/queue/panic tests. The dry-run regression failed against the unchanged engine (`Assert.False`, actual true), then passed after the fix.
- Kept `feat-companion-barks` at `stub` and replaced its duplicated historical prose with one current row. Remaining behavior cannot fit with focused tests in this unit's 1,000-added-line budget; this is not a pure dependency NOOP or a P122 claim.

## Reference and gap inventory

WPF reference pinned at required base `db079bf91474c77a5ea67ec306f6337a7de44b83`, primarily `ConditioningControlPanel/Services/Companion/BarkService.cs`:

| WPF behavior absent before this unit | Result |
| --- | --- |
| `WireSubscriptions` video before/start/end, flash/subliminal, player XP/level, companion level, achievements, quests, mantra, lockdown, Patreon tier, tutorial, persistent memory | Implemented with WPF trigger keys and context payloads; tier events retain invite-week suppression and the None-to-None guard. |
| Bubble-count completion/failure | Successful count, final wrong answer and mercy completion implemented. Terminal playback/library error notifications remain missing. |
| `DispatchIdle`, pool rotation and gated bias (:1178-1230, :1846-1852, :1914-1938) | Implemented with the tube timer from `AvatarTubeWindow.Speech.cs:1571-1624`; mute, speaking, normal gate, persistence and recycle behavior tested. |
| Whisper gate (:1689-1692), actual tier key (:1781), dry-run (:184, :200, :1824, :1923, :1934), ordinary voiced `Giggle` (:1988) | Implemented; the existing engine is retained. |
| Webcam `Blink`, `MouthOpen`, `TongueOut`, `LongStare`, `FaceLost`, `FaceFound` (raise before clearing elapsed loss), `TrackingStateChanged`; gaze popped/active; blink trainer state | Missing adapters and bark state reads, despite camera/Lab surfaces on this head. |
| Attention pass/fail only while video plays, including fail count; ambient bubble misses; lock-card completion's 50/50 custom/pool choice | Missing. The lock-card event currently lacks the authored completion payload needed by the coin-flip path. |
| Brain-drain, mind-wipe, keyword trigger context and eight-second self-echo mute | Missing. |
| Awareness activity/still-activity, v2 routing, bark/external-line cooldown accounting | Missing arbiter integration; the ordinary window-awareness surface alone is insufficient. |
| Skill unlocked, lucky proc, Pink Rush start/end; roadmap step/track/badge; quiz outcome/timing gates | Missing. Skill purchases/lucky producers remain unported (`MainShellWindow.Enhancements.cs`); roadmap and Pink Rush have live surfaces needing adapters. Classic quiz still lacks its `RaiseQuizCompleted` caller (`QuizWindow.axaml.cs`). |
| Remote command/controller connection, activity idle state, update availability, Discord authentication | Missing adapters; activity idle tracking has no service on this head. |
| Session start/stop/complete/phase/progress/pause/resume and >=20-second window refocus | Missing session adapters/state fields. `SessionRunner` currently exposes Tick/Stopped rather than the full WPF event surface. |
| Setting changes count toward setup readiness; 8-12-second debounce, one 45-second stall follow-up, enabled-media gate | Missing; numeric SettingChanged barks retain the existing path. |
| Tray wake; launch away-bucket greeting, launch recency and streak milestone; avatar appearance, enhancement and leaderboard context | Missing direct caller adapters and launch/state bookkeeping. |
| Possession rung/effect/tripwire/warden/rules/timer-restart/remember/conscript; emergency-exit opened/verdict | Missing bark caller adapters. The floor exemptions are now ready, but do not constitute those callers. |
| Descent near-bank/day-banked/stage-crossed/lapse-return/first-Spiral-open | Missing watcher/caller integration. |
| Chaos notifications beyond the existing draft-autopick, rank-up and results doorbells | Missing run start/wave/combo/act/ending/completion, defuse/detonation/absorption/benign, boon/curse/skip, darter/freeze catches, first-time/lesson/reveal/gift/duo/gold/dollhouse and tease/teaching edges. These are live WPF callers, not dead-code exemptions. |
| Chaos narrator floor (:1695); `PickVoiceLine` (:1603) | Missing voiced narrator/voice-picker callers; no dormant seam added. |
| Missing live condition reads | `webcam_running`, `all_skills_unlocked`, `setup_idle_sec`, `session_elapsed_sec`, `session_phase_index`, `blink_count`, `face_lost_sec`, `days_away`, `instant_relaunch`, `phase_name`, `phase_is_deepener`, `pauses_this_session`, `refocus_this_session`, `session_planned_min` still need their sources. |
| Mood delivery and heard-voice telemetry | The tube's existing mood parameters are not rendered; DTRH heard-voice duration accounting remains missing. |

The inventory covers both the row's named follow-up gaps and the rest of WPF's subscription/direct notification surface. No live WPF caller was declared dead, so render-all/P122 cannot upgrade this row.

## Verification limits

- .NET SDK 10.0.402.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: passed, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: passed, zero errors.
- All 12 Linux test csproj files named in `.github/workflows/build.yml` built with `dotnet build <project> -c Release -o <shared-output>`: zero errors. Shared temporary output conserves the cloud disk; project/target settings were unchanged.
- `bash .github/scripts/core-guards.sh`: passed, 429 XAML references checked. The first read-only invocation could not create temporary files; rerunning with writable temporary access passed.
- `export DiffEngine_Disabled=true`; focused Core filter `FullyQualifiedName~BarkEngineTests|FullyQualifiedName~MandatoryVideoSchedulerTests|FullyQualifiedName~BubbleCountSchedulerTests`: 50 passed, zero failures.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: intermittent baseline failure in `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, actual Clips). One complete recheck passed (3,560 passed / 5 Vosk skips); the final complete run had 3,559 passed / 1 failed / 5 skipped. The isolated class passed all nine tests. An exact, clean detached checkout of `db079bf91474c77a5ea67ec306f6337a7de44b83` reproduced the same failure at the same assertion (3,552 passed / 1 failed / 5 skipped; exit 1). Neither the media implementation nor its tests changed; the full-suite gate remains intermittent on the base.
- `Tests/CCP.Avalonia.Tests` compiled with zero errors. `CompanionBarkHeadTests`, `SubliminalWhisperShowTests` and `TubeSpeechQueueTests` were not executed here: the supplied cloud font-creation limitation still applies and the font-support lane is absent. The supervisor must run these filters on its runner.
- WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: initial attempt lost an MSBuild child worker (`MSB4166`); single-worker retry (`-m:1`) passed with zero errors (402 existing warnings). Windows code was not run.
- No desktop, real profile, visual render-all, audio-device or live interaction verification. Vosk model unavailable; its existing skip policy was retained, with no test weakening.

## Assumptions

- The required commit is authoritative; the read-only snapshot's different initial commit was replaced through the explicitly allowed writable-clone fallback.
- WPF's delayed preset intentionally loses its clip. It retains the unprompted flag so delayed idle chatter obeys the sound hold.
- `ponytail:` panic parks idle until a new line is actually shown (or the timer is explicitly restarted), then normal cadence resumes. Existing `tube-speech` panic registration owns this timer; close also stops it.
- Remaining caller groups stay explicit rather than advertising parity through unused methods. No PR was opened.
