- Complete the independent cloud-export auth gap in `views-settings-account`: recover a 401 through restore-session first, then Patreon (preferred) or Discord revalidation. Keep WPF's 30-second/2-minute strategy cooldowns, serialized recovery, changed-token success signal for provider recovery, token preservation on failure, heartbeat restart, and one export retry.
- Share recovery with backup metadata checks (recover on 401, return no metadata without retrying, as WPF). Keep one cloud client across Account section repaints so cooldowns persist. Existing dialogs, ownership, save picker, and panic routing are reused; no new surface.
- Add fake-HTTP regression coverage and a numbered tester step. The initial restore-session export regression failed before the fix (one failed test: original `expired` error); the focused wire suite passed after the fix.
- Reference: WPF files at base `5115f6416267f62b96cc7055edacabb5232bc83a`, notably `MainWindow/MainWindow.CloudBackup.cs:59-285` and `Services/Settings/ProfileSyncService.cs:4241-4522`. Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.

## Remaining WPF gaps

The row remains `stub`. Backup now and Restore from cloud remain hidden. Remaining behavior is the forced upload (busy label, success/failure dialogs and status repaint); restore session-lock refusal, confirmation, downloading/decompression, identity/progression/entitlement keep list, local-only fields, settings-object replacement, full settings UI reload, completion/failure dialogs and button reset. These are live WPF callers, so P122 does not apply.

The required host is WPF `ProfileSyncService`'s settings backup/restore pipeline, still absent in Core/Avalonia: exclusions including all Chaster-local fields, `SettingsBackupBudget`, compression, refusal handling and local field preservation, plus the head's settings reload. `shell-cloud-backup` tracks the broader startup/exit host paths. The 2026-10-09 `rows-cloud-remember` decision in `docs/avalonia-decisions.md` explicitly leaves the cross-OS asset-list translation and server contract unresolved. Copying Windows asset paths into Linux settings would not resolve that dependency. This unit completes independent read/export recovery and leaves the upload/restore contract decision for its owner rather than promoting the ledger inaccurately.

## Assumptions

- Preserve WPF provider priority (Patreon, otherwise Discord); SubscribeStar does not participate in WPF token recovery.
- Keep the existing cross-OS backup contract decision in effect. Do not invent path translation or a new server contract within this unit.

## Verification limits

Final validation results: Windows/native OAuth and real cloud service requests cannot be exercised here; wire tests use fake HTTP and in-memory secrets. Existing Account settings tests use the sandbox profile. No desktop or real profile is used.

- SDK: .NET 10.0.100. Channel discovery was blocked (403); pinned SDK installation succeeded.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter FullyQualifiedName~V2AuthServiceWireTests`: exit 0, 18 passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~AccountSettingsSectionTests`: exit 0, 15 passed headlessly.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: final exit 0, 2,821 passed, 5 existing Vosk model tests skipped, 0 failed. `DiffEngine_Disabled=true` was set for test runs. No tests were changed to skip.
- An initial full-suite/build attempt exhausted the filesystem because each head test output copied the assets. Generated test binaries were removed, then all checks were rerun serially with space reclaimed between test-project builds. The initial disk-related failures are not claimed as passing checks.
- `test-plan-coverage.sh` is absent on this base; a numbered tester step was added in `docs/avalonia-test-plan.md`.

- `dotnet build <project> -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0 and 0 errors for Core, Avalonia, and every test project named in `.github/workflows/build.yml`: Avalonia CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, main Avalonia tests; Core Content, Settings and main Core tests; and ConditioningControlPanel WPF tests. Existing warnings remain. WPF/native Windows code was compiled but not run.
- `bash .github/scripts/core-guards.sh`: exit 0, all Core guards passed (424 XAML references checked). `git diff --cached --check`: exit 0. No real cloud, native OAuth, or render-all run was performed.
