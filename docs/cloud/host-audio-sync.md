- Port the WPF audio-sync coordinator into Core with a head-supplied decoder; retain chunk planning, latency compensation, five-second resync, live power and band split.
- Wire the Home browser's WebHost detection/state/seek/end messages, processing progress, ready signal and chunk loading overlay. Missing devices and processing failures allow unsynced video playback.
- Cancel pending sessions on navigation, detachment and panic; register browser-audio-sync in PanicSurfaces and SafetyCritical. Late completion cannot resume a stopped page.
- Linux uses distro FFmpeg for mono 44.1 kHz range decoding. Windows uses the adapted WPF MediaFoundationRangeReader via NAudio.
- Source: current WPF reference at base 39260f198fad67d90bc988c63a00480830e5fefc. Fetched parity/trunk a4ee52d40 for reference; no trunk code imported.

## Verification limits

- Core suite: 3958 passed, 5 existing Vosk tests skipped (model unavailable). Focused audio-sync tests: 5 passed.
- Headless audio host and panic tests: 9 passed. Fake-page JavaScript tests: 4 passed. Linux decoder exercised against a generated WAV.
- Core guards passed. Test-plan coverage script is absent from this base; a numbered manual step and wired ledger row were added.
- All Linux workflow test projects compiled. WPF test build with EnableWindowsTargeting and ValidateExecutableReferencesMatchSelfContained=false passed on retry (0 errors); initial output copying exhausted the cloud disk. Windows decoder compiled in Avalonia but cannot run here.
- Native WebHost engines, real haptic hardware and authenticated/DRM browser media were not exercised. Linux requires FFmpeg on PATH; failure unblocks playback without haptics.
- Assumptions: Home browser is the first real caller. Injection occurs after navigation because WebHost has no document-created injection API. Late-device rearm and other browser hosts remain follow-up work.
- Initial broad checks exhausted the cloud disk with copied native runtimes; disposable build outputs were removed and checks retried sequentially.
- Regression proof: removing the Core reset generation increment failed FirstChunkBecomesReadyAndResetSuppressesPendingCompletion (expected 1 completion, got 2); disabling the panic registration failed HomeBridgeUnblocksMissingServiceAndPanicDisarmsIt. Both production lines restored.
