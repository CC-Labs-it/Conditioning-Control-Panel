# Sync 7: plan card and funnel

- Ported WPF/trunk `5265111bca77df0bf37386e03e807757e7899269`: the Avalonia vault card reads the plan list, repaints prices and sale, shows euro/yearly hints, and launches the selected plan's checkout. See `CCP.Avalonia/Views/Dialogs/VaultGateDialog.cs:224`, `:347`, `:364`, `:478`. Tests: `VaultGateTests.PlanPricesHintsAndCheckoutUseTheSameFreshList`, existing sale and checkout tests. The shared catalog, pricing and localized copy were already on the base.
- Ported WPF/trunk `219fdf936661cad585ffbedf72ae3ceefaff8d3e`: shared beacon at `CCP.Core/Services/Funnel/PanelFunnelBeacon.cs:39`; card show/click calls at `CCP.Avalonia/Views/Dialogs/VaultGateDialog.cs:110`, `:123`, `:347`, `:364`. Tests: `FunnelBeaconTests` and `VaultGateTests.CardCountsShowAndTheRailActuallyOpened`. Payloads contain only allowlisted event/feature/rail tokens; failures never block checkout.
- `f627edd0618a2aea8c33eba048940c9a56657cc5`: **blocked-by-host**. There is no Avalonia `FunnelPageMessage` handler for page plan checkout requests. Adding that sibling feature merely to normalize its rail would exceed this unit. No unused parser imported.
- WPF/trunk `4fcd2fe658130a1b72a729b7ca54bcea316994ea`: partially ported. Beacon respects Offline Mode (`CCP.Core/Services/Funnel/PanelFunnelBeacon.cs:46`), covered by `FunnelBeaconTests.OfflineModeSuppressesTheBeaconAndPlanFetch`. Already covered: plan offline guard (`CCP.Core/Services/Vault/PlanCatalog.cs:85`), logout account state (`CCP.Core/Services/Account/ProgressionClear.cs:63`, called by `CCP.Avalonia/Platform/AccountSeed.cs:220`), page host messages and prevention of bare popups (`Assets/web/dtrh/game/planCard.js:102`, `Assets/web/backroom/shared/win/end-card.js:119`). **Blocked-by-host**: lapse Inbox/greeting require absent `ConversionRules` / `EntitlementTierSync` flows; cloud restore local-state preservation requires the absent restore service (documented at `CCP.Avalonia/Views/Windows/MainShellWindow.CloudBackup.cs:11`). No substitute flows invented.

The read-only `parity/trunk` source was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. Imports above credit their individual source commits. Existing Avalonia controls and external-link seam were retained; no new surface needs panic registration. No parity or sync ledger edited.

## Verification limits

- `dotnet test Tests/CCP.Core.Tests -c Release` (with `DiffEngine_Disabled=true`): exit 0; 3971 passed, 0 failed, 5 pre-existing Vosk model-dependent skips. No test was weakened or disabled.
- Negative proof: temporarily removed the production offline guard in `PanelFunnelBeacon.Post`; `--filter FullyQualifiedName~OfflineModeSuppressesTheBeaconAndPlanFetch` exited 1, expected 0 sends / actual 1. Restored the line unconditionally; `--filter FullyQualifiedName~FunnelBeaconTests` exited 0, 18 passed.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~VaultGateTests`: compilation succeeded; execution exited 1, 9 failed at environment font creation (`Could not create glyphTypeface`, including existing tests). The new UI assertions therefore remain unverified here; run them on the supervisor's configured runner. No real desktop or external browser was used.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The base has no `.github/scripts/test-plan-coverage.sh` (attempt exited 127); numbered tester steps were added anyway.
- Builds use `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true` and sequential output cleanup to fit the cloud disk. Final test-project rebuilds use `-p:BuildProjectReferences=false` after compiling Core and the head. Final Linux build results (all exit 0, 0 errors):
  - `dotnet build CCP.Core/CCP.Core.csproj -c Release`
  - `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`
  - `dotnet build Tests/<project>/<project>.csproj -c Release` for `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`, `CCP.Avalonia.Tests`, `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`, `CCP.Avalonia.Migration.Tests`, `CCP.Avalonia.Scheduler.Tests`, `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.GeneralSettings.Tests`, and `CCP.Avalonia.Snapshot.Tests`. These cover every Linux-compatible test project named in the workflow. The main headless test assembly also compiled after the shared-type rename.

- WPF app compiled in the cross-build; WPF tests then built successfully (exit 0, 0 errors) with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false -p:BuildProjectReferences=false`. They were not executed on Linux.

Windows desktop behavior cannot run in this Linux environment. Tests use their existing isolated profile fixtures and intercepted external links.

The shared type is named `PanelFunnelBeacon` to avoid colliding with the frozen WPF assembly's `FunnelBeacon` when its tests reference both heads.

## Assumptions

- Keep absent sibling hosts out of this branch, as instructed; report their fixes as blocked rather than importing unused helpers.
- Match WPF sale precedence: a server plan list owns the sale (including null), while a fallback list uses the legacy sale endpoint.
