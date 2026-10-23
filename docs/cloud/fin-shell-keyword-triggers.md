# fin-shell-keyword-triggers

- Base: `261c1011eb828d37b423e648cd31068957fc9295`.
- Adapted response actions, AI/canned comments and Chaster booking from read-only
  `parity/trunk` commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  Retained this base's buffer, Windows hook, settings editors and stoppable player.
- Added WPF action selection: single-trigger duplicates, disabled filtering, legacy
  fallback/HighlightOnly early return, preset-first simultaneous-hit merge by type
  (different visual effects keep separate slots).
- Typed matches reach haptics, session-scaled XP, subliminal/exact/image visuals,
  the running bubble field, guarded Mind Wipe, avatar comments and Chaster.
  Audio repeats finish before later actions; engine/session-off holds audio.
- Chaster uses local tab accounting and persisted per-trigger limits: only
  successful bookings consume the three-booking/15-minute daily allowance.
- Added events, quests/achievements, fire-time snapshots, 20-record history and
  five-newest Last Detected feed with Fluent icons. Keyword quest capability is live.
- Tutorial uses WPF's entitled synthetic Highlight+AvatarComment trigger, never
  saved audio/settings. Start is entitled/idempotent; exact text forces a 3s mute.
- App-scope Enter/focus-loss commits WPF's canonical process names.
- Existing keyword panic entry cancels clips, repeats and pending comments.
  Existing engine/haptics/tube-speech entries own reused effects; no new native surface.

## WPF comparison and remaining gaps

Reference: `MainWindow/MainWindow.KeywordTriggers.cs` and
`Services/KeywordTriggerService.cs` under the WPF head at the pinned base.
The shell editors already have callers in this base's panel/tab. The service has
live WPF callers, so P122 does not apply. The ledger remains `stub`.

Complete gap inventory found on the base:

- Linux typed input; clipboard source; screen capture/OCR cadence updates,
  multiword/regex hit boxes, confirmation counts/quick rescan, ignore-own-UI,
  highlight-all versus fired-only selection, overlays and capture visibility.
- Action dispatch/merge, legacy fallback, visual/haptic/XP/avatar/Chaster responses,
  demo dispatch, trigger events, quests/achievements and pulse feed.
- Recent foreground-app sampling/cache/chips, canonical app-list editing, status
  breathing, entitlement-change reconciliation, entitled/idempotent Start and
  engine-off audio gating.

Remaining after this unit:

- Linux input, clipboard/OCR and highlights/capture remain unimplemented.
- Recent-app sampling/chips, status breathing and entitlement-change source
  reconciliation remain unimplemented.
- OverlayPulse, BrainDrain and bubble SpawnOnce with ambient bubbles off depend on
  unported overlay APIs. Bubbles here only adds to a running field.
- Avatar retries a busy bubble every 750 ms up to 16 times; this head lacks WPF's
  separate audio-busy probe and Awareness v2 arbiter reporting API.
- Audio keeps the base player's completion-based cleanup rather than WPF's single
  batch-wide duck held for the longest audio duration plus 500 ms.
- ExtendSessionAction is also a no-op in WPF; no substitute API was invented.

## Verification limits

The writable fallback checkout was verified at the required base SHA. The original
snapshot was clean but at a different commit and had a read-only Git directory.
.NET SDK 10.0.402 was installed. No WPF reference source was modified.

Observed command results before the cloud executor became unavailable:

- `dotnet build <project> -c Release`: Core, Avalonia and all 12 Linux test
  projects listed in build.yml passed with 0 errors before the final app-list edit.
- After the app-list edit, the repeated build sweep confirmed Core, Avalonia,
  CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report,
  Scheduler and Snapshot projects with exit 0. The executor disconnected while
  the remaining builds were running; their final results could not be recovered.
- `bash .github/scripts/core-guards.sh`: exit 0, "All Core guards passed"
  (before the final pure parser addition).
- XP regression test against the original runtime: exit 1 as expected; its award
  collection was empty. Command: `dotnet test Tests/CCP.Avalonia.Tests -c Release
  --filter FullyQualifiedName~KeywordResponseTests`.
- Focused `--no-build` filter for KeywordResponseTests, KeywordChasterTests and
  ProgramCapabilitiesTests: exit 1; 16 passed, 1 failed. All 14 new response/Chaster
  behavior cases passed. The capability failure was an expected-program order
  mismatch, corrected to BuiltInPrograms.All's order but not rerun before loss.
- `git diff --check`: exit 0 before runtime loss. Last local added-line count: 817.
- Not run: full `dotnet test Tests/CCP.Core.Tests`, the final focused rerun, and the
  WPF test-project build with WindowsTargeting/validation flags.
- Window-based KeywordAudioRuntimeTests/KeywordTriggersPanelTests/PanicSurfacesTests
  were compiled only; the supplied environment reports font creation failures.
  The two new pulse/app-list UI tests also require the supervisor's runner.
- Windows keyboard hook/translation compile but cannot run on Linux. The external
  Vosk model download is unavailable in the supplied environment (HTTP 403).

The executor became unavailable before commit/push and stayed offline on resume.
Source was recovered from the recorded edits and the pinned base through the
GitHub connector; final recompilation was impossible. This is incomplete
verification, not a green gate. Tests use sandbox profiles, mock haptics and local
Chaster accounting.

## Assumptions

- Preserve the existing runtime and prioritize reachable responses within 1,000
  added lines; retain an honest stub until the remaining paths are ported.
- Preserve WPF's single-versus-merged legacy behavior and AI/fallback order; reuse
  the head's current effect support boundaries.
- Cancel pending comments on panic through the existing keyword registry entry.
- Preserve work on the sole authorized branch after infrastructure loss using the
  task's blocked-work exception; final verification must be completed elsewhere.

## Ledger update blocked by approval review

The existing ledger file is unchanged. Automatic approval review rejected its
whole-file Git API write because the payload contains inherited local evidence
paths and unrelated ledger content. A second attempt to clean absolute paths was
also rejected as outside the requested-row scope. No ledger cleanup was published.
The code and this narrowly scoped hand-off are preserved separately.

Prepared replacement notes for the existing shell-keyword-triggers row:

- Status: stub.
- Wired: enabled action dispatch/merge and legacy fallback; haptics/session XP;
  supported visual effects; comments and Chaster; synthetic tutorial demo;
  events, quest/achievement credit, 20-record/5-row pulse feed; canonical app scope.
- Missing: Linux input; clipboard/OCR scanning/confirmation and highlights/capture;
  recent-app feed/chips; status breathing; entitlement reconciliation; unported
  OverlayPulse/BrainDrain/bubble SpawnOnce APIs; avatar audio-busy/v2 reporting;
  WPF batch-wide duck timing. Verification remains incomplete after executor loss.
