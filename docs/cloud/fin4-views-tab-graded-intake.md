- Move the WPF silent intake punch-card service into CCP.Core; both heads now use the same rules and state file. Retain free first hole, matching-session half-run/natural-end credit, bounded/aged pending drafts, atomic persistence, recovery, completion and claim rules. Replace DispatcherTimer with the existing CoreDispatch/System.Threading.Timer pattern, keeping the 30-second cadence and UI-thread serialization. Reuse the original WPF behavior tests through a linked compile item.
- Wire Avalonia startup/shutdown, successful intake drafts, session progress and natural completion. Saved intake history now enables the existing returning-user minimize behavior. The card UI remains hidden, matching WPF's UiEnabled=false. No new surface; intake panic registration is unchanged.
- Match WPF's focus-existing-intake-before-gates order and route the spent-pass toast to the priced Basic intake card. Add draft, session-credit, focus-order and upsell tests, a numbered tester step, and one honest stub ledger row.
- Reference source: WPF at base 85d30ccc4b6cb241d136e8299a0d2e844b88585a. Fetched parity/trunk read-only (a4ee52d40cd347d288dcf7fdaa8445b79f8e5038); no code imported from that trunk commit.

## WPF gap inventory and remaining dependencies

Compared GradedIntakeTabView.xaml/.xaml.cs and forwarded MainWindow.Lab.cs handlers with the Avalonia view and IntakeHostWindow partials. The existing head already has the gate states/copy, mod art, settings/test handlers, pass/AI checks, page protocol, speech/media/remote bridge, achievements, session reveal/start toast, fullscreen persistence, main-window restore and heartbeat recovery. Classic quiz controls/history are hidden in WPF; their reachable behavior does not require enabling them here.

Gaps addressed here: silent punch-card host and session-credit callbacks; persisted first-versus-returning intake history; focusing the already-running intake before entitlement checks; spent toast destination.

Remaining behavior gaps (row stays stub):

- EMI intakeRunning hold/release: WPF IntakeHostService.cs:111/1062 calls EmiDeskService.Fire/ReleaseHold. Avalonia's director is absent; EmiDeskService only owns summon/dismiss. WPF Fire feeds Speak and HoldFace, which in turn cancel the speech channel/chains, acknowledge the engine row and stop/restart idle beats. Avalonia's PlayChain/Say are still stubs. Porting a nominal hold flag would leave those visible behaviors absent. The complete EMI moment/speech/chain director is a separate dependency (WPF EmiDeskService alone is 2,128 lines, before the window bubble/chain implementation), beyond this unit's 1,000-added-line budget.
- Native process failure recovery: WPF IntakeHostService.cs:1010 receives the WebView2 process-failed event. Avalonia WebHost currently has navigation completion and page messages, with no native process-failure signal. Heartbeat recovery already works only after ready; it cannot cover a crash before ready. This requires a native web-adapter lifecycle feature, not a page-message imitation. No adapter/package change attempted here.
- Live supported-native-engine page visual/audio playback and actionable toast click remain unproven in this runtime. These are verification gaps, distinct from missing implementation.

No P122 exemption claimed: the WPF hold, process-failed and silent punch-card paths all have live callers. The disabled punch-card UI does not make the service dead.

## Verification limits

- .NET 10.0.100 installed using the pinned-version fallback; channel discovery hit HTTP 403.
- Release builds: Core, Avalonia and all Linux workflow test projects compile with zero errors. WPF tests also cross-compiled successfully with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false (0 errors); Windows code is not executed here.
- core-guards.sh: exit 0, all guards passed. test-plan-coverage.sh is absent on this base; numbered WPF/Rows coverage added manually. git diff --check passes.
- Full Core suite, twice: exit 1, 3,771 passed / 1 failed / 5 skipped. Failure: DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls (expected Loops, got Clips). This is outside changed code. Its isolated rerun plus all linked punch-card cases passed: 30/30. Vosk tests use their existing missing-model skips; no tests were modified or skipped to get green.
- Relevant broad head filter: exit 1, 15 passed / 15 failed, with glyphTypeface creation failures (Inter/Segoe UI Black) in this runtime. No real desktop/profile/microphone used.
- Negative proof: removing the draft and runner credit hooks made both integration tests fail (0 passed / 2 failed); restored hooks passed both. Focus-order and Vault UI tests require working font creation; their runtime results are recorded below.
- Native web/audio and render-all not run; fonts/native engine support prevent meaningful proof. WPF compiled only, never run. No PR opened.

## Assumptions

- Preserve the WPF disabled punch-card panel and prize policy; silent progress must still be earned and saved.
- Shared service uses CoreDispatch as QuestService already does, avoiding a head-specific timer abstraction.
- Retain stub status until the named EMI/native-adapter dependencies and live verification are resolved.

Final focused head run: exit 1, 2 passed / 2 failed. Draft and runner-credit tests pass; focus-order and priced-card tests fail before their assertions in glyphTypeface creation. Final Avalonia test build: exit 0, 0 errors. The negative hook-removal run remains the behavioral failure proof for both new credit paths.
