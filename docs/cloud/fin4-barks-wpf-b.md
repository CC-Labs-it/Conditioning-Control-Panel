- Adapted frozen WPF behavior at base `bae161586a96585dcb5f054d7daaf0ac82cdb128`: raw Patreon tier latches, idle selection/rotation persistence, live tube cadence and decision logs that record length rather than dialogue.
- Connected the existing subliminal whisper player to the WPF duration-plus-tail gate, including possession and emergency-exit attribution exceptions. Echo behavior was already implemented; added regression coverage for all nine triggers.
- Reused the existing `tube-speech` panic registration; idle work starts only on an opened tube and stops on panic/close. No new surface.
- Fetched read-only parity/trunk at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Remaining dependency analysis

The original row listed five gaps. Tier latch, idle dispatch/selection/persistence, and LogDecision are implemented. The cited attribution helpers are whisper-gate exemptions, not special echo formatting: `RaiseBarkSpoken` at WPF lines 145-153 passes the same rule and final text. Core already did that; tests now cover each attribution while a whisper is active.

The row stays **stub**. The new idle path holds voice (keeping text) with engine/session off unless opted in. The WPF stale-speech watchdog is reused on every idle tick, preserving pending speech delays. The Safety.PanicAftermath quiet-window policy remains unported; panic stops this timer outright until an explicit restart. WPF's narrator-active gate cannot be reached without its missing narrative host. At the pinned base, Avalonia `ChaosOverlayWindow.axaml.cs:1606` has empty `PlayCardLine`/`EndCard` methods and an empty announcer adapter. The actual WPF host includes `ChaosNarrator.cs` (194 lines), `ChaosNarrativeDirector.cs` (270), hooks (61) and the 3358-line ChaosModeService orchestration, plus the announcer surface. A read-only activity seam without this caller would falsely claim parity. Porting the complete live narrative path, its UI and tests does not fit the remaining budget. Likewise WPF FlashService's audio path calls MarkWhisperAudio, while Avalonia FlashOverlay has only lucky-flash audio, not the flash voiceline host. This unit wires the already-live subliminal source rather than creating dead adapters.

## Verification limits

- .NET SDK 10.0.100 installed successfully (channel discovery returned 403; pinned SDK download succeeded).
- Core build: exit 0, zero errors. Full Core suite: exit 0, 2862 passed / 5 existing Vosk skips / 0 failures. Focused BarkEngine tests are included (22 cases).
- Regression proof: decision-log test with the old fire-log behavior failed (exit 1, missing Chars event); restored implementation passed (exit 0).
- Avalonia application and headless test project builds: zero errors. All Linux test projects referenced by build.yml compiled successfully. WPF test project also cross-compiled with EnableWindowsTargeting and ValidateExecutableReferencesMatchSelfContained=false: zero errors; Windows code was not run.
- Core guards: exit 0, all guards passed. Diff whitespace check: exit 0. Test-plan coverage script does not exist on this pinned base; numbered steps were added anyway.
- Avalonia runtime tests (CompanionBarkHeadTests, SubliminalWhisperShowTests, TubeSpeechQueueTests and TubePanicTests) were compiled but not run: this snapshot lacks the documented cloud font fix. GitHub rcheck must execute them; no rendering/manual desktop claim is made.
- Two intermediate full-Core runs under concurrent build load failed unrelated timing/performance assertions (GoonClockAndRoundsTests scheduling and BoardTilesTests frame budget respectively). A subsequent full run without heavy concurrent builds passed; no tests were changed or skipped to address those failures.

Linux builds cannot prove Windows runtime behavior; no Windows desktop or real user profile was used.

## Assumptions

- Raw Patreon tier is used rather than merged premium/Lab entitlement, matching WPF LatchKey exactly. WPF's in-session one-shot suppression remains even if tier changes.
- Panic stops the new idle timer via the existing tube registry. Reopening the tube, a later speech line or changing its interval can restart it.
- Missing narrator and flash-voiceline hosts remain explicit dependencies; the row is not promoted to wired.
