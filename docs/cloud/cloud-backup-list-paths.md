# Cloud backup list paths

- Keep AudioLayers, CornerGifOverlays and KeywordTriggers in uploads; remove item paths, legacy AudioFilePath and the path-derived, read-only AudioLayerTrack.DisplayName.
- Restore list settings from the backup, retaining this machine's paths by track/slot index (those models have no stable ID/name) and by trigger/action ID (stable across renames/reordering). Unmatched paths become empty, including frozen WPF Windows paths.
- Keep local lists only when the backup omits them; an explicit empty list clears local entries.
- Add wire-level Core regressions for upload contents, matching, backup-only/wiped-disk restores, legacy Windows paths and missing versus empty lists. Update the cloud-settings-backup decision entry.
- No trunk code imported. Read-only `parity/trunk` reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; behavior adapted within the existing base implementation.

## Verification limits

- Before the fix: focused CloudSettingsBackupTests exited 1 (4 failed, 5 passed), demonstrating dropped upload lists, lost backup settings and incorrect list retention.
- .NET SDK 10.0.402; all builds used `dotnet build <project> -c Release`, exiting 0 with `0 Error(s)`: CCP.Core, CCP.Avalonia, CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, and CCP.Avalonia.{CatalogueStartup,Consent,GeneralSettings,Language,Report,Scheduler,Snapshot}.Tests (13 projects, all Linux-compatible test projects listed in `.github/workflows/build.yml`). Existing warnings remain.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 560 warnings, 0 errors. This compiles the WPF head/test project and Windows code; none was run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed; `git diff --check`: exit 0.
- All test commands used `DiffEngine_Disabled=true`. `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --filter FullyQualifiedName~CloudSettingsBackupTests`: exit 0, 10 passed, 0 failed/skipped (also confirmed with `--no-build` on final code).
- `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --no-build --filter FullyQualifiedName~AccountSettingsSectionTests`: exit 0, 14 passed, 0 failed/skipped, headless.
- Full Core suite: first run exited 0 (2,824 passed, 5 skipped); final-code runs (`dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release`, repeated with `--no-build`) exited 1 (2,824 passed, 1 failed, 5 skipped). The sole failure was the unchanged `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` at line 80: ScheduleAt returned null. Its zero-RTT SafeFireAt uses exactly the minimum buffer, which can expire between clock reads. The class passed in isolation (`--filter FullyQualifiedName~GoonClockAndRoundsTests --no-build`: exit 0, 12 passed). No tests were weakened, removed or newly skipped; the full-suite scheduling failure remains a verification risk outside this unit.
- Tests use fake HTTP handlers and sandbox profiles; no live cloud account or desktop was exercised.
- WPF and Windows-specific runtime behavior cannot run on Linux. The WPF reference remains frozen.
- The pinned Vosk model fetch exited 22 (HTTP 403); speech integration coverage depends on that unavailable model.
