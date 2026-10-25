# fin4 views-tab-she-listening

- Base verified: `85d30ccc4b6cb241d136e8299a0d2e844b88585a`; clean checkout, task branch created from that commit. No WPF changes.
- Added live shell routes for mind wipe, pop quiz, standalone bubble count, louder/quieter and mute/unmute. Existing Core matching, confirmations, guards, grammar/help filtering and tab filtering remain the owners of those behaviors.
- Volume steps by 15, clamps at 0/100, updates live layered/video volume and notifies Settings/dashboard mirrors. Mute remembers the last positive volume (including repeated mute), silences the current avatar clip while keeping its text, disables whispers and mutes the avatar; unmute restores the remembered volume or 70, and enables whispers/voice.
- Added ten focused headless cases using inline transcripts and fake quiz/count hosts: wipe host routing/removal, normal quiz interaction policy, force-count with the feature/engine off, volume boundaries/mirrors and repeated mute/restoration. No real microphone or video playback. Existing test module provides an isolated user-data profile.
- Adapted the action mappings and mute behavior from read-only `parity/trunk` commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (`MainShellWindow.VoiceActions.cs`), checked against WPF `AutonomyService.VoiceCommandHost.cs` and `MainWindow.Patreon.cs`. Existing base files win; no file was overwritten with trunk content, and the base's newer confirmation/cancellation logic is preserved.
- Kept one ledger row, honestly `stub`, and added numbered tester steps. No new surface: existing engine-stop registrations close wipe/quiz/count, and `tube-speech` cuts the same clip handle.

## WPF gap inventory and remaining work

At the pinned base, the tab's handlers already reached master arm/disarm, Settings Devices, sensitivity, consent/revoke, spoken-mantra toggle/test, entitlement gates, device chips, mod artwork and the model-folder button. Hero readiness and armed pulse were also implemented. Fluent icon substitutions and adaptive side-art are existing separate decisions/ownership.

The remaining live WPF behaviors at that base were:

1. Voice actions: video pause/play; sustained spiral at the user's opacity; sustained pink with a 40% floor that never dims a stronger tint; mind wipe; pop quiz; keyword start/stop; forced bubble count; shake (60, 1200); deeper/BrainDrain; session pause/resume; mute/unmute and volume steps. This unit completes wipe, quiz, count and the four audio intents. The other actions remain missing. Also restored WPF's explicit mantra intent marker to the command grammar; Core defers it to the existing listen funnel, tested by grammar inclusion and inline deferral.
2. Sherpa KWS availability/status: Active, configured-but-failed and missing-model states; lazy initialization/model fingerprint detection without resetting a live decode; reliable wake detection integrated with the existing Vosk fallback, capture ownership and stop/reconcile paths. The head still has only Vosk wake detection and the missing-model calibration notice.
3. Five-utterance calibration: duplicate-click guard, installed-model check, consent, stop wake/PTT, wait up to 20 x 25 ms for capture, disable button, collecting/analyzing progress, chosen sensitivity persistence, success/failure status, re-arm and button restoration. All remain missing.

These are live WPF paths, so P122 does not apply. Sources at the pinned base: `MainWindow.SheListening.cs:84-175`, `Services/AutonomyService.VoiceCommandHost.cs:55-108` and `Services/AutonomyService.Voice.cs`. Calibration is called by the tab button; voice actions are consumed through Core `VoiceCommands`.

The missing hosts are substantial: WPF `SherpaWakeService.cs` alone is 711 lines (NAudio capture/native KWS/model lifecycle and calibration), and `KeywordTriggerService.cs` is 2117 lines; Core has no corresponding portable engines on this base. Sustained overlay ownership, primary-video pause and remote session/panic resume policy also require head work. Completing those hosts plus focused tests exceeds this unit's 1000-added-line budget. This is partial implementation, not a dependency-only NOOP, and no completion claim is made.

## Verification limits

- .NET 10 installed. Channel discovery initially returned HTTP 403; pinned 10.0.100 installation succeeded, and the channel installation subsequently completed with 10.0.402, used for verification.
- Release builds: CCP.Core, CCP.Avalonia, CCP.Avalonia.Tests and every other Linux test project named in build.yml succeeded, zero errors. These include Core.Tests, Core.Settings.Tests, Core.Content.Tests, Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Migration.Tests, Report.Tests, Scheduler.Tests and Snapshot.Tests.
- Windows WPF test project compiled with `EnableWindowsTargeting=true` and `ValidateExecutableReferencesMatchSelfContained=false`: zero errors. Windows code was compiled but not run.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`, with `DiffEngine_Disabled=true`: exit 0; 3743 passed, 5 skipped, 0 failed. Existing Vosk cases skip because the model fixture is unavailable; no tests were changed or skipped by this unit.
- Focused Avalonia filter `SheListeningActionTests|SheListeningCommandRowsTests|SheListeningTests`: compiled successfully, execution exit 1; 2 passed, 12 failed on the known environment error `Could not create glyphTypeface` for `Segoe UI Black`. New shell assertions were not reached, so neither passing runtime evidence nor a before/after failure proof is claimed. GitHub rcheck must run these cases with working fonts.
- An initial overlapping build hit an Avalonia PDB file lock. The subsequent serialized head/test build succeeded with zero errors.
- `test-plan-coverage.sh`: unavailable on this base (exit 127). Numbered tester steps were added in the required format anyway.
- No live microphone, Windows runtime, desktop render-all, or full Avalonia suite run. Render-all needs the same unavailable shell font. No push to any branch other than the task branch and no PR.

## Assumptions

- Prioritize actions whose real hosts already exist; preserve the stub rather than invent reduced substitutes for the remaining hosts.
- Follow WPF mute behavior literally: unmute enables whispers/avatar voice rather than restoring their previous flags. A previous zero master restores to 70.
- Reuse existing panic registrations because these commands open existing registered surfaces only.
