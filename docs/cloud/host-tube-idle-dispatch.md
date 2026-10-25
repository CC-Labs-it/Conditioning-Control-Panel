- Extend the existing Core BarkEngine with WPF DispatchIdle: conditions, normal gates, persisted pool-wide no-repeat, last-rule reseed and 0.35 gated bias.
- Start one idle timer when the Avalonia tube opens, reset it on speech and interval edits, refresh the interval each tick, and recover a stale speech latch while respecting pending queued speech.
- Adapt the pure CompanionIdleVoice rule from WPF. Unprompted bark text remains visible; engine/session state, session pause, engine-off opt-in and panic quiet decide audio. Reuse the existing Workshop toggle.
- Extend the existing SafetyCritical `tube-speech` panic surface to stop idle ticks; close also detaches the timer.
- Add run-scoped NoteVoicelineHeard telemetry through the existing CoreAudio clip-start/duration callback and DtRH local stats store. Held/muted lines do not report playback; delayed starts cannot count into another run.
- Reference credit: fetched read-only parity/trunk at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038. Adapted behavior from 20b2ccce0 and e5830315e; f5455ca20's toggle already exists on the base and is now effective. c34ce92d2's separate voice-level slider is outside this host unit. Existing base files were extended, not replaced.

## Verification limits

- Expected checkout verified at 39260f198fad67d90bc988c63a00480830e5fefc after the instructed fetch/reset; local cloud branch name was work.
- .NET SDK 10.0.402 installed. Release builds: Core, Avalonia and Linux workflow test projects; detailed final results below.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 3962 passed, 0 failed, 5 existing Vosk tests skipped (model unavailable). No tests weakened or removed.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~CompanionIdleHostTests`: 9 passed after restoring production code.
- Fail proof: omit `_usedIdleRules.Add(rule.Id)` in production, run IdleRotationAndGates: exit 1, expected 3 distinct lines, actual 1. Restore that line: focused suite green.
- Focused Avalonia filter: TubeIdleHostTests, TubePanicTests, PanicSurfacesTests. Compiles; runtime attempt: 5 passed, 3 failed during UI construction because Segoe UI Black / Inter glyph typefaces cannot be created in this environment. UI behavior and panic assertions require supervisor/GitHub execution with working fonts; the failures are not claimed as behavior proof.
- `bash .github/scripts/core-guards.sh`: passed with temporary-file write access. The test-plan-coverage script does not exist at this base. `git diff --check`: passed.
- No Windows desktop execution, real audio playback, notifications, or manual live UI validation. Existing LibVLC audio is the cross-platform seam; no new OS-specific API was added. Tests use the repository's sandbox profile setup.
- Assumptions: reuse the registered SafetyCritical tube-speech surface rather than add a duplicate surface. With no BarkHead, preset fallback remains text-only (ponytail); ordinary voiced barks retain the base's priority mouth. Other missing bark event sources and voice-level UI remain separate lanes.

Release build matrix (`dotnet build <project> -c Release`):

- CCP.Core, CCP.Avalonia: exit 0, 0 errors.
- CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests: exit 0, 0 errors.
- CCP.Avalonia.Consent.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Migration.Tests: exit 0, 0 errors.
- CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Snapshot.Tests: exit 0, 0 errors.
- CCP.Avalonia.Tests: an intermediate compile found a missing return in the new dispatcher lambda; corrected without changing existing tests. Subsequent compile: exit 0, 0 errors. Runtime font limitation remains as recorded above.

Final recheck notes:

- A repeated full Core run exposed an unrelated intermittent `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` failure (expected Loops, observed Clips). That test passed immediately in isolation; the final full suite passed again: 3962 passed, 0 failed, 5 Vosk skips, exit 0.
- WPF test compilation initially exposed a duplicate public type name with the frozen head. The new pure Core helper is named `CompanionIdleVoiceRule`, preserving the WPF reference unchanged. Final architecture guards pass after that rename.
- Final `dotnet build Tests/CCP.Avalonia.Tests -c Release`: exit 0, 266 warnings, 0 errors.
- Final `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 557 warnings, 0 errors; compiled only, not executed on Linux.
