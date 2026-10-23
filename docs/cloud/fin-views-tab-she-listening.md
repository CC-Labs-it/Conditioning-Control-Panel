# fin-views-tab-she-listening

- Base verified at `e138d8bfc84cee4008eaf78a91156fb6b8563110`; the read-only checkout required the authorized independent-clone fallback.
- Adapted WPF `MainWindow.SheListening.cs`, `SherpaWakeService.cs`, `SileroVadGate.cs` and `AutonomyService.Voice.cs` at that SHA: portable `IMicSource` capture, KWS/Vosk routing, model status, five-say calibration, progress/results and re-arm. Audio stays in memory. Native packages match WPF 1.13.3.
- Calibration preserves the 300 ms floor seed, 800 ms pre-roll, 250–2500 ms spoken-length bounds, 480 ms end silence, 40 s cap, threshold order and recall-first/ambient/best-effort outcomes. Settings remain unchanged on failure/cancel. Re-entrant capture is refused; native handles survive status repaint and active-session disposal.
- Adapted mute and recorded-confirmation completion/echo-tail handling from read-only `parity/trunk` commit `f705e05a2d6eac1fce37e2946f4ba8730d9d2ab1` (fetched trunk tip `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`). Existing base files were merged, never overwritten.
- Panic's existing `voice-capture` registration also stops calibration; privacy stop, revoke and exit close capture. Mute cuts a live voice clip while keeping its caption, and unmute never replays it.
- Added PCM replay coverage for missing models/devices, int8 selection, five utterances, strict/quiet and recall-biased selection, best effort, failed sweep, busy capture and cancellation. Added head regressions for immediate mute and the confirmation wait.

## Remaining gaps and assumptions

- The ledger stays `stub`. WPF's live `keyword_on/off`, `shake_once` and `deeper` map to KeywordTriggers, ScreenShake and BrainDrain (`AutonomyService.VoiceCommandHost.cs:83–89` at the base SHA). The base has a stand-in KeywordTriggerHead and no ScreenShake/BrainDrain runtime. They remain excluded from grammar/help; P122 is inapplicable. Importing those separate runtimes exceeds this unit's remaining line budget.
- Windows microphone capture is also unported (`PulseMicSource.Seed` only seeds Linux). Windows native packages compile, but a usable capture seam is required there.
- Assumptions: retain WPF's install-relative drop-in model folders and fallback behavior; use the existing Fluent-icon policy (the Active status omits WPF's checkmark glyph). No model download or unrelated runtime was introduced.
- ponytail: detailed opt-in wake telemetry was not copied; init errors and calibration sweep/results remain logged. The line cap prioritizes calibration and live-audio behavior.

## Verification limits

- .NET 10.0.100; Core, Avalonia and all eleven workflow Linux test projects built with zero errors (exit 0). Builds use hard links and discard generated test outputs between projects because this runtime has limited disk space.
- Core guards passed. Full Core suite: 2,820 passed, five existing Vosk tests skipped because no model is installed; no tests weakened or skipped by this change. Expanded wake replay suite: nine passed.
- Avalonia regression classes (`VoiceShellActionsTests`, `SheListeningTests`, `SheListeningCommandRowsTests`, `VoiceCommandsTests`, `PanicSurfacesTests`) are compile-only here: the supplied environment reports a font-creation failure and has no cloud-test-fonts fix. UI regressions were not executed, including fail-before-fix proof; the supervisor must run them on GitHub.
- No real microphone, native keyword recognition/calibration, desktop rendering or Windows execution was tested. Native inference is replaced only in the Core replay tests' sweep seam; production uses sherpa.
- WPF app and test project cross-compiled with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: zero errors, exit 0; never executed on Linux.
- After strengthening the echo assertion to outlast the old 300 ms delay, the final Avalonia test rebuild reached output copying but exhausted disk space (exit 139). Command execution then became unavailable; final rebuild and UI execution remain for the supervisor.
- Infrastructure fallback: publication uses the GitHub connector to preserve the same prepared changes when the cloud command runner cannot be reached. No PR or other branch is created or updated.
