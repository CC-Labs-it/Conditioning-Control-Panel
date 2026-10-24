# sync7-port-conversion-backup-exclusions

- Audited WPF commits `23416827ea70f42d2d7006230d61321a7a4bc61e` and
  `b244fda785f9adb73d4f9e5f2f7c7cea82e8acb7` against base
  `39260f198fad67d90bc988c63a00480830e5fefc`.
- Both are **blocked-by-host**. No production behavior changed, no unused exclusion
  helper introduced, and no ledger or parity status promoted.
- Fetched `parity/trunk` read-only; no trunk implementation was imported.

## Commit dispositions

| WPF commit | Status | Missing host and evidence | Test disposition |
| --- | --- | --- | --- |
| `23416827e` | blocked-by-host | Requires the cloud settings backup exclusion filter to omit `ServerTierSeen` and `InboxStamps`. `CCP.Avalonia/Views/Windows/MainShellWindow.CloudBackup.cs:12` explicitly records the missing `ProfileSyncService` backup/restore implementation and `ExcludedBackupProperties`. The active buttons remain stubs at `CCP.Avalonia/Views/Controls/AppSettings/AccountSettingsSection.axaml.cs:472`. | WPF reference: `ConversionLapseRowsTests.Conversion_state_never_rides_the_cloud_backup`. No callable Core/head backup serializer exists to exercise or mutate. |
| `b244fda78` | blocked-by-host | Requires the same exclusion filter to omit `PatreonMember`, `PostGrants`, and `ServerPaidUntil`. Same missing host and stub handlers as above. The model fields alone do not provide a backup transport/filter. | WPF reference: `ConversionFreeMemberTests.Member_fields_never_ride_the_cloud_backup`. No callable Core/head backup serializer exists to exercise or mutate. |

`CCP.Core/CoreSettingsHooks.cs:23` declares only an optional `CloudBackup` callback;
`CCP.Core/Services/Settings/SettingsService.cs:1004` invokes it if bound. No Avalonia
binding or shared exclusion serializer exists. Ordinary local settings persistence
must retain these fields; excluding them from local JSON would not port either fix.
The backup section remains hidden (`AccountSettingsSection.axaml.cs:253`).

## Verification limits

- SDK: .NET 10.0.402. Channel discovery initially returned HTTP 403 but installation
  subsequently completed; an explicit 10.0.100 SDK installation also succeeded.
- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core,
  CCP.Avalonia, and all 12 Linux test projects referenced by the build workflow:
  CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests,
  CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests,
  CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, and
  CCP.Avalonia.Snapshot.Tests. Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML
  references checked). The initial read-only sandbox invocation could not create
  scratch files; rerunning with write access passed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0,
  557 warnings, zero errors. Cross-compiled only; not executed on Windows.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 0;
  3953 passed, 0 failed, 5 skipped (existing Vosk model-dependent tests).
- `git diff --check`: exit 0.
- `.github/scripts/test-plan-coverage.sh` is absent on this base; not run.
No new behavior or surface was added, so there is no production-line mutation test,
new tester step, or PanicSurfaces registration. Avalonia runtime tests are not run:
there is no relevant backup implementation/class, and this environment has the
reported font-creation limitation. Windows execution and real cloud backup/restore
are not available here. The Vosk model is not fetched (reported HTTP 403 limitation).

## Assumptions

- The explicit blocked-by-host instruction takes precedence over creating a new
  cloud settings backup/restore subsystem for two exclusion-list additions.
- Sibling conversion lanes and local backup behavior remain outside this unit.

## Blocker

Port the cloud settings backup serializer/filter and restore host into Core/Avalonia
first, then apply these five exclusions there with payload-level regression tests.
