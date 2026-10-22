# wire-dialogs-welcome-back-sheet

- Fill the existing welcome-back sheet from its real startup/profile-loaded offer: release version, shared current patch notes, and the WPF season sentence when the server-confirmed current month is later than `LastSeasonResetSeen`.
- Extract WPF's exact 7.1.5 note literal into Core `ReleaseNotes.Current`; WPF's `UpdateService.CurrentPatchNotes` aliases it and Avalonia seeds its existing `CoreReleaseContent.PatchNotesProvider`. A source comparison confirmed the extracted literal is identical to the frozen base. No second note list or new UI host was added.
- Reuse Core `WelcomeBackDecision.ShouldShowSeasonLine` and `ProfileAdopt.LooksLikeMonthKey`, which WPF's season service already uses. `SeasonRecapStore` only persists snapshots; it cannot establish a server-confirmed current season. Local snapshots and the wall clock must not invent a rollover.
- Keep the existing folded notes row, Enter toggle, localized sentence/fallback, trigger guards, wizard deferral and one-off modal. Restore/flavour remain decided out (2026-10-09, no cloud backup on this head), and tests assert those rows stay hidden. No panic surface was added.
- Update only ledger row `dialogs-welcome-back-sheet` to `wired`, with the backup decision and verification limits recorded.
- No trunk code imported. Read-only `parity/trunk` was fetched and inspected at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; behavior follows the WPF reference on the brief's `companion-transcript` base (`1423ff908e605aace2fdf58fcad5683f1d8e4749`).

## Verification limits

Commands used .NET 10, Release configuration and sandboxed test profiles. Tests used `DiffEngine_Disabled=true`; successful headless tests/render used a task-local `FONTCONFIG_FILE=/tmp/ccp-worker-1-fonts.conf` excluding this executor's unsupported OpenAI Sans WOFF2 default. No app fonts or existing tests were altered.

- Before the production fix, `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter FullyQualifiedName~WelcomeBackContentTests` failed all six cases at the intended assertion: expected `Release notes from the head`, actual empty string.
- After the fix, `dotnet test Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release --filter 'FullyQualifiedName~WelcomeBackContentTests|FullyQualifiedName~WelcomeBackLogoTests|FullyQualifiedName~IconGuardTests'`: **16 passed, 0 failed/skipped**, 6 s. The new six actual-offer cases cover confirmed-later, equal, backwards, blank, malformed and unseen seasons, visible/collapsed rows and Enter expanding the notes. Existing trigger guards/one-off behavior and icon tests also passed.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: succeeded, **0 warnings, 0 errors**.
- `bash .github/scripts/core-guards.sh`: passed, **424 XAML references**, 1591 app / 1882 Core types.
- `dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj -c Release --no-build --no-restore`: **2831 passed, 0 failed, 5 skipped, 2836 total**, 1 m 27 s. The five existing Vosk tests need an external speech model absent here.

Build commands below were `dotnet build <project> -c Release` with `CreateHardLinksForCopyLocalIfPossible`, `CreateHardLinksForAdditionalFilesIfPossible`, and `CreateHardLinksForCopyFilesToOutputDirectoryIfPossible` set to `true` for this run's disk constraints. Test projects except WPF used `BuildProjectReferences=false` against the already-built heads; disposable ancillary test bins were removed after their build. Only the focused Avalonia classes and full Core suite above were executed; the other Linux test projects were compiled.

| Project | Result |
| --- | --- |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 errors; exit 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 errors; exit 0 |

The one conditional Windows build attempted was:

```sh
dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false
```

Restore succeeded and Core built; WPF reached `ConditioningControlPanel_ytyh01xg_wpftmp.csproj` compilation. The client was stopped with SIGTERM under shared memory/4-CPU pressure before a build-completion summary. It returned 0 on shutdown, which is **not evidence of a completed compile**. No successful WPF build or Windows/native execution is claimed; there was no repeat attempt. Last output:

```text
ConditioningControlPanel.csproj : warning NU1510: PackageReference System.Security.Cryptography.ProtectedData will not be pruned.
ConditioningControlPanel_ytyh01xg_wpftmp.csproj : warning NU1510: PackageReference System.Security.Cryptography.ProtectedData will not be pruned.
```

`FONTCONFIG_FILE=/tmp/ccp-worker-1-fonts.conf CCP_USERDATA_DIR=/tmp/ccp-worker4-welcome-render-profile dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all /tmp/ccp-worker4-welcome-render`: **225/225 views**, exit 0. `WelcomeBackSheet.png` was visually inspected: the notes toggle and muted season line fit the existing layout. This render uses the existing all-row sample (including the decided-out backup rows); actual offer content, hidden backup rows and expanding the notes are proven by the headless tests, not by a live authenticated startup screenshot.

Logs and exact build command arrays are local under `/tmp/worker4-welcome-*.log` and `/tmp/worker4-welcome-build-results.json`. No PR was opened.
