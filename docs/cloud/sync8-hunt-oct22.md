# Sync 8: Oct 22 hunt

- Port crash recovery from WPF commit `8a89eaeb05e961901ecfb5722aed6914b4ea01b5`: journal the pre-session scalar settings and three phrase pools before overrides; restore on next launch before settings consumers; clear after clean handback. Core uses CorePaths/CoreSettings and Serilog.
- Port real elapsed-time minute credits, retaining WPF's 15-second tick-gap cap and existing wall-clock remainder at stop.
- Keep existing SubliminalDuration restoration. No new surfaces; PanicSurfaces registration is unchanged.
- The first `compat-scratch:` commit supplies pure rule files already required by the merge and replaces three stranded Core `App.*` references. Core counterpart type names carry a Core prefix to avoid ambiguity in the WPF tests that reference both assemblies. Integration should drop this scratch commit when the compatibility lane is present.
- Reference `parity/trunk` was fetched read-only. No code was imported from that branch; source credit is the WPF commit above and the pure reference files already present at base.

8a89eaeb0 = ported: CCP.Core/Services/Session/SessionSettingsJournal.cs:26 + SessionRunner.cs:115 + CCP.Avalonia/App.axaml.cs:453 (SessionSettingsJournalTests, SessionRunnerTests.JournalPrecedesOverridesAndCleanStopClearsIt, SessionRecoveryStartupTests); CCP.Core/Services/Progression/ConditioningTimeTracker.cs:41 + ConditioningClock.cs:24 (ConditioningTimeTrackerTests delayed/capped tick cases); already-covered: CCP.Core/Services/Session/SessionSettingsSnapshot.cs:38 (SubliminalDuration; SessionRunnerTests.OneMinuteSession_CompletesByTick_SavesLog_RestoresSettings_ReleasesPools); n/a: floating-video bubble clearing, because Avalonia's MandatoryVideoOverlay.cs:28 implements fullscreen windows only, and Core MandatoryVideoScheduler.cs:343 correctly pauses bubbles for those windows (existing MandatoryVideoSchedulerTests).

## Assumptions

- Do not introduce floating-video playback in this bug-fix unit. Stored floating settings and pure presentation rules have no Avalonia playback consumer.
- Retain WPF's stop-time wall-clock top-up, including after a capped tick gap.
- Preserve the existing phrase-pool custody behavior on clean stops; recovery uses WPF's same-mod rule.

## Verification limits

- Checkout verified at required base `28ab74eed692067fed4f2051791b3a6d0976b1dc` before lane changes.
- Negative proof: with base tracker and runner restored temporarily, all three new lifecycle/timing regressions fail (3 failed, 0 passed); restoring the implementation gives 24 focused Core tests passed (including the added exact-field coverage assertion).
- Final full Core tests: 3962 passed, 5 existing Vosk tests skipped (model unavailable), 0 failed. No tests were weakened or removed.
- Commands: `dotnet build CCP.Core/CCP.Core.csproj -c Release`, `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`, and `dotnet build <project> -c Release` for every Linux test csproj in build.yml: exit 0, zero errors (existing warnings retained). This includes Core.Tests, Core.Settings.Tests, Core.Content.Tests, and Avalonia CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, and main Tests projects.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: initially exit 0, 3961 passed / 5 skipped; final rebuilt assembly run with `--no-build`: exit 0, 3962 passed / 5 skipped; the subsequent focused run with the extra field-coverage assertion: exit 0, 24 passed / 0 skipped. All test commands use `DiffEngine_Disabled=true`.
- Avalonia negative proof: `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~SessionRecoveryStartupTests` with base App source: exit 1, one assertion failure. With restored App source and filter `SessionRecoveryStartupTests|ConditioningTimeTrackerShellTests|SessionRunWireTests`: exit 1, startup test passed, two shell tests failed before behavior assertions at glyphTypeface creation for Segoe UI Black. Shell runtime behavior therefore remains unverified in this environment; supervisor CI must run these.
- A temporary overlapping build hit an Avalonia resource-file lock; the sequential standalone head rebuild passed (zero warnings/errors). No source change was needed.
- One additional full Core run (after adding the field-coverage test) reported 3961 passed / 5 skipped / 1 failed in untouched DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls (expected Loops, got Clips). That test passed in isolation immediately afterward; it was not changed.
- Windows-specific code cannot execute on this Linux runtime; WPF test project cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 605 warnings / 0 errors. WPF tests were compiled but cannot run here. No Vosk model download or desktop/manual playback verification was performed.

- Final sequential gate after Core helper renames: Core, Avalonia, and all 12 Linux test projects built with zero errors; 24 focused Core tests passed; the Avalonia startup test passed; core-guards passed.

## Changed files

- `CCP.Avalonia/App.axaml.cs`
- `CCP.Core/Services/Chaster/ChasterService.cs`
- `CCP.Core/Services/Commands/AiCommandAdmission.cs`
- `CCP.Core/Services/Commands/AiCommandService.cs`
- `CCP.Core/Services/Commands/GetBackToMeCommand.cs`
- `CCP.Core/Services/Commands/MediaCommand.cs`
- `CCP.Core/Services/Haptics/LockdownService.cs`
- `CCP.Core/Services/LockdownExitRule.cs`
- `CCP.Core/Services/Progression/ConditioningClock.cs`
- `CCP.Core/Services/Progression/ConditioningTimeTracker.cs`
- `CCP.Core/Services/Safety/BlinkStopGate.cs`
- `CCP.Core/Services/Safety/PanicAftermath.cs`
- `CCP.Core/Services/Safety/VoicePanicReach.cs`
- `CCP.Core/Services/Session/SessionRunner.cs`
- `CCP.Core/Services/Session/SessionSettingsJournal.cs`
- `CCP.Core/Services/Speech/VoiceCommands.cs`
- `CCP.Core/Services/Timing/TickStep.cs`
- `CCP.Core/Services/Webcam/BubbleAccuracyTest.cs`
- `Tests/CCP.Avalonia.Tests/SessionRecoveryStartupTests.cs`
- `Tests/CCP.Core.Tests/ConditioningTimeTrackerTests.cs`
- `Tests/CCP.Core.Tests/SessionRunnerTests.cs`
- `Tests/CCP.Core.Tests/SessionSettingsJournalTests.cs`
- `docs/cloud/sync8-hunt-oct22.md`
