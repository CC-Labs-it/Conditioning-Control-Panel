# FIN4 Workshop roster personality assignment

- Base verified at `df5a59167c58ab3735c2a5295db3b3b7243af475`; work is confined to the FIN4 unit branch.
- WPF audit: the cell delegates to `MainWindow.CompanionTab.cs:327-480`; `CompanionService.cs:424-469` applies the assignment; `CommunityPromptService.cs:273-376,431-492` defines import and activation. Remaining gaps at the base were the unhandled personality event, JSON picker, import/assignment persistence, active-companion reapplication, switch-time application, all five prompt labels/tooltips and validation advisory. Card switching, hero/level refresh and tube avatar-set switching were already live.
- Added the native JSON picker with WPF title/filter and populated custom-folder → shipped-folder preference. Imported personalities receive fresh IDs and filename names; inactive assignments remain inactive until switching. The active companion applies an assignment immediately. Missing/unassigned prompts leave the current voice intact. Malformed imports return no success result, matching WPF.
- Reused the existing Core installed-prompt loader and extended it with local import and activation. Explicit-content acknowledgement remains required; validation logs counts and posts a soft advisory. Catalogue/network community features are outside this row and are not prerequisites for local import.
- Refreshed mod-aware labels/tooltips, retained live switch/level subscriptions and cleanup, and registered roster feedback for panic cleanup.
- Added focused Core and headless button/label/cancellation/panic coverage. The existing pointer-switch tests now cancel the OS picker through the picker seam, keeping tests off the desktop.
- Replaced the historical duplicated ledger description with one `wired` row and added numbered WPF tester steps.
- Import provenance: fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported. Existing base files win. Behavior adapted directly from the WPF files pinned by the base above.

## Assumptions

- The live WPF picker is the specification; its stale comment about choose-installed/clear options has no corresponding UI or caller and introduces no additional requirement.
- WPF's unlock checks always return true, so no obsolete level-lock dialog is added.
- Native JSON picker behavior is delegated to Avalonia StorageProvider; headless tests substitute cancellation or a synthetic local JSON file.

## Verification limits

Commands used .NET SDK 10.0.100, Release configuration, `DOTNET_PROCESSOR_COUNT=2`, `-p:UseSharedCompilation=false`, and `DiffEngine_Disabled=true` for tests.

- Before fix: `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~SwitchAppliesInstalledAssignmentBeforeAnnouncingTheCompanion` exited 1: expected assigned ID, actual null. After fix: focused CompanionCoreTests/InstalledCommunityPromptsTests exited 0, 7 passed.
- All required builds below exited 0 with **0 errors** (existing warnings retained). WPF build used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`; Windows application/tests were compiled but not run.

```text
build CCP.Core/CCP.Core.csproj exit=0
build CCP.Avalonia/CCP.Avalonia.csproj exit=0
build Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj exit=0
build Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj exit=0
build Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj exit=0
build Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj exit=0
build Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj exit=0
build Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj exit=0
build Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj exit=0
build Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj exit=0
build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj exit=0
build Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj exit=0
build Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj exit=0
build Tests/CCP.Core.Tests/CCP.Core.Tests.csproj exit=0
build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj exit=0
```

- `bash .github/scripts/core-guards.sh`: exit 0, `All Core guards passed.`
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, **4119 passed, 5 skipped, 0 failed**. Existing Vosk tests skipped because the model is unavailable; no tests were weakened or newly skipped.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~WorkshopRosterPersonalityTests|FullyQualifiedName~WorkshopRosterSwitchTests|FullyQualifiedName~CompanionRosterSwitchTests|FullyQualifiedName~WorkshopCellsTests|FullyQualifiedName~PanicSurfacesTests'`: exit 1, **5 passed, 7 failed**. Every failure is the documented environment font error `Could not create glyphTypeface` (Segoe UI Black or Inter), including the new test. UI assertions could not run to completion; the supervisor must run them in its font-capable environment. No font workaround or test weakening was introduced.
- `git diff --cached --check`: exit 0. Exactly one ledger row remains for this ID; three numbered tester steps cover it. The base has no `.github/scripts/test-plan-coverage.sh`, so that script could not run.
- Native picker interaction, render-all and Windows runtime were not run. Picker order/title/filter are covered by manual tester steps; headless code uses the picker seam and temporary profile. Both pre-existing pointer test fixtures explicitly cancel through that seam now that assignment is wired.
- SDK channel discovery returned HTTP 403; pinned SDK installation succeeded. Native package restores retried after truncated responses and all builds completed.
- Final compile after sandboxing the second pointer fixture: `dotnet build Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj -c Release -p:UseSharedCompilation=false` exited 0, 96 existing warnings, 0 errors. Font-blocked UI execution was not repeated.
