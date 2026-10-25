# fin4-shell-cloud-backup

- Base verified: `715b7da7e9a0216bc3a97e4a0d5f0cd0f29163e9`; snapshot branch name `work` ignored.
- Adapted the manual cloud backup client/card and tests from trunk `ad080c973d959def85546fe7b3c161405d94c4a1`, inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Existing base files were merged, not replaced by trunk.
- Account settings now expose manual backup, restore and status. Busy labels retain Fluent icons; result dialogs use the existing MessageDialog/ShowDialogSafe path. Restore refuses active locked sessions, confirms, keeps local identity/progression/entitlements and refreshes settings sections.
- Core owns gzip/base64 payloads, budget trimming, metadata and local-wins rules. Updated trunk exclusions/preservation for current WPF conversion state and AudioPlaylist fields. Fake HTTP tests exercise payload, signed-out/offline failures, metadata, restoration and safety.
- Ledger remains `stub`; no claim of full parity or verification.

## WPF gap inventory

At the pinned base, all manual handlers/status/reload were empty, automatic save/exit backup was absent, and export/startup restore had no host. Privacy-policy launch already worked. WPF evidence: MainWindow/MainWindow.CloudBackup.cs, Services/Settings/ProfileSyncService.cs (4257–4760), Services/Settings/SettingsBackupBudget.cs, App.xaml.cs (683, 4763–5260).

Delivered: manual busy/success/failure paths, restore lock refusal/confirmation, identity/progression/entitlement/local-only preservation, UI refresh and status date/version/no-backup/error handling.

Remaining: GDPR export transport, 401 recovery/retry and save picker; startup welcome-back host and its lifetime/companion progress merge, version/day-log/flavour/inbox follow-ups; automatic save backup (SettingsService 30-second gate plus service five-minute debounce, rolling-backup reconciliation guard and 413 session suppression); exit backup. These plus focused coverage do not fit the remaining line allowance. Nonempty cross-OS asset-list translation remains undesigned; WPF preservation semantics are retained rather than inventing translation.

## Assumptions

- The unit authorizes the manual WPF endpoint contract despite historical P44 deferral; automatic uploads remain disabled.
- Trunk's safety floor is retained. This base lacks its panic-specific OCR notice field, so a locally disabled OCR switch stays disabled during restore.
- No new surface/window type is introduced: existing informational MessageDialog is reused; it has no media or looping work to stop.

## Verification limits

- SDK 10.0.402 installed from the official builds feed; channel discovery initially returned HTTP 403.
- `dotnet build -c Release`: Core, Avalonia and all Linux test projects listed in build.yml compiled with zero errors. Report suite initially hit MSB4166; serial `-m:1` retry succeeded (0 warnings, 0 errors).
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~CloudSettingsBackupTests`: exit 0, 12 passed; repeated after final edits.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~CloudBackupCardTests`: exit 0, 1 passed; repeated after final edits. Font initialization worked here.
- Full Core suite: exit 1, 3964 passed / 1 failed / 5 Vosk skips. Failure: DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls (expected Loops, actual Clips). Isolated rerun passed (exit 0). Full rerun: exit 1, 3963 passed / 2 failed / 5 skipped; same remote-media assertion plus BoardTilesTests.A_frame_fits_the_budget (16.73 ms under concurrent compilation). Neither product/test source was changed; baseline-suite reproduction was not run.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed (repeated against final code).
- `bash .github/scripts/test-plan-coverage.sh`: exit 127, script absent on this base; numbered manual steps added regardless.
- `git diff --check`: exit 0.
- No live cloud requests or real profile/desktop tests. Windows runtime behavior cannot be exercised here. Remaining Linux suites were compiled, not executed.
- Automatic approval review rejected an attempt to remove one imported test for the line budget; all test cases were retained and comments trimmed instead.

- Windows test project compiled successfully with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. It was not run on Linux.
- All 15 required project builds completed successfully (Report passed on serial retry); existing warnings remain.
