# fin2-chaos-slot-picker

- Finish `chaos-slot-picker` as `wired` under the task's P122 exception: the only recorded gap was an opener with no live WPF caller. Consolidate six repeated rows into one. This is a documentation-only closure; no new launch path, surface, or runtime behaviour.
- Audit the WPF reference and existing Avalonia implementation at base `be09ff13f0ea97c92e72c6dfcad763e387ad1aec`. No remaining user-visible picker behaviour was found missing, apart from the unreachable opener described below.
- Read-only source fetched with `git fetch origin parity/trunk`, resolving to `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; the base files win.

## WPF behaviour audit

The reference is `ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml` and its code-behind. Existing head coverage:

| WPF behaviour | Avalonia implementation already on base |
| --- | --- |
| Three slots in order; active selection; local path; header drag; visible-owner centering with screen fallback | Constructor and `Pick`; `OwnedDialog.ShowDialogSafe` supplies the head's hidden-owner fallback |
| Slot 2/3 locked until any save owns Ragdoll/Porcelain; existing saves remain open; locked selection falls back to 1 | `RebuildCards`, `BuildLockedCard`; dim, no select/delete |
| Empty slot: New Journey / Empty slot — start fresh | `BuildCard` |
| Existing slot: rank, singular/plural descents, grouped drops/gold, positive best score only, optional local-time last-played date | `BuildCard` / `StatRow`; same copy, order and date format; established Fluent icon substitution |
| Left-button selection and selected border/background/glow | `Card_Click` / `UpdateSelectionVisuals` |
| Erase only for existing saves; default Cancel; exact irreversible-delete prompt and warning if no other save owns the relevant doll | `DeleteSlot_Click` / `EraseMessage`; Core delete and rebuild recompute locks and selection |
| Create/open save folder; catch and log opener failures | `BtnOpenFolder_Click` through existing platform opener |
| Descend chooses slot; close/Cancel returns no choice | Constructor handlers and async `Pick`; head already switches/reloads through Core `ChaosMeta.SwitchSlot` after confirmation |

No timing/animation sequence exists in the WPF picker. No new surface was introduced, so panic registration is unchanged. Existing tests are `ChaosHubStateTests`, `ChaosMetaStoreTests` and `ChaosShelvesTests`; no behaviour was added requiring a new test or a before-fix failure.

## P122: pinned caller proof

Run against the exact base, not a branch name:

```sh
git grep -n ChaosSlotPickerWindow be09ff13f0ea97c92e72c6dfcad763e387ad1aec -- ConditioningControlPanel
```

Only four matches:

```text
ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml:1: x:Class declaration
ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml.cs:20: class declaration
ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml.cs:38: private constructor
ConditioningControlPanel/Chaos/ChaosSlotPickerWindow.xaml.cs:50: new ChaosSlotPickerWindow() inside Pick itself
```

Thus neither `Pick` nor the private constructor has a live external WPF caller. The absence of an Avalonia opener mirrors this dead WPF entry point; it is not an unported dependency needing an invented button. Apply P122 (`wired`, never `verified`) with render-all proof below.

## Verification limits

Commands use .NET SDK 10.0.100 and Release configuration. `DiffEngine_Disabled=true` was exported for tests. Render execution used separate `CCP_USERDATA_DIR` and `XDG_CONFIG_HOME` sandboxes, with no desktop.

- Checkout verification: initial HEAD differed; required fetch and checkout succeeded (exit 0). Reverified exact expected HEAD, local `work`, clean status before creating the task branch.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero errors.
- `dotnet build <project> -c Release` for every Linux-capable test project listed in `.github/workflows/build.yml`: all exit 0, zero errors. Projects: CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests.
- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed. Initial read-only-sandbox attempt could not create temporary files; rerun with write access passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 3,949 passed, 5 skipped, 2 failed. Unchanged failures: `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` (expected Loops, actual Clips) and `BoardTilesTests.A_frame_fits_the_budget` (15.20 ms). Vosk cases skipped without the model; no model fetch attempted given the stated 403 limitation. No tests weakened or disabled.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build --filter 'FullyQualifiedName~ChaosMetaStoreTests|FullyQualifiedName~ChaosShelvesTests'`: exit 0; 4 passed, 0 failed/skipped.
- `ChaosHubStateTests` compiled but not executed, following the task's measured cloud font-creation limitation. The existing class uses bare AppBuilder without the shipped font setup; the shared dispatcher has no font bootstrap. Supervisor/GitHub execution remains needed. Other test assemblies were compiled, not executed.
- `dotnet run --project CCP.Avalonia/CCP.Avalonia.csproj -c Release --no-build -- --render-all <output-directory>`: exit 0; **253/253 views rendered**, including `ChaosSlotPickerWindow.png` (720x560, 50,414 bytes). Picker image visually inspected: empty selected slot, two stitched-shut slots, local-storage footer and Cancel/Descend controls. This proves drawing, not interactive behaviour.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0; 554 warnings, zero errors. Windows-specific code was not executed on Linux; no physical folder opener, native owner/drag, or interactive web DTRH launch exercised.

## Assumptions

- The explicit P122 rule takes precedence over the normal real-user-opener criterion when pinned WPF caller proof shows a dead entry point. No dependency NOOP is needed.
- Preserve the established Fluent icon substitution and head-owned slot switch on confirmation. Do not add a save picker to the web DTRH path, since WPF has no such caller.
- Keep this closure documentation-only; existing rendering techniques are not changed as part of a dead-entry-point audit.
