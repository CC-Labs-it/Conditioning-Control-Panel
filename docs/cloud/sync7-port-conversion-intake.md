# sync7-port-conversion-intake

- Ported the remaining part of WPF `2a1fd1e88f2864fab99e1787d74604e35263c03d`: the spent weekly Intake pass notification now opens the Graded Intake Basic price card through the existing shell vault route, keeping the localized copy and eight-second warning duration.
- Added `IntakeSpentOfferTests.SpentPassToastOpensBasicIntakePriceCard`, which clicks the real notification action and checks the feature title, Basic perk, and absence of the Prime chip or Account navigation.
- Read-only reference fetched: `parity/trunk`. No trunk code imported; source credit is the WPF commit above. No sibling commits ported and no ledgers changed.

## Commit disposition

`2a1fd1e88`: **ported** at `CCP.Avalonia/Views/Tabs/GradedIntakeTabView.axaml.cs:183`, covered by `Tests/CCP.Avalonia.Tests/IntakeSpentOfferTests.cs:27`.

Already-covered parts on the expected base:

- `CCP.Core/Services/Progression/IntakePassService.cs:98`: unlimited runs read `CoreAccount.HasPremiumAccess`.
- `CCP.Core/Models/ExclusiveFeature.cs:229`: the Exclusives gate reads `CoreEntitlement.HasPremium`.
- `CCP.Avalonia/Views/Dialogs/VaultGateDialog.cs:368`: Basic includes `vaultgate_basic_9`; Prime omits the removed `vaultgate_prime_5`.
- `CCP.Core/Localization/Languages/en.json:8514`: Basic Intake perk key; all nine shared language dictionaries already contain the moved key.
- `CCP.Avalonia/Views/Tabs/GradedIntakeTabView.axaml.cs:110`: the page directly uses `App.IntakePass.State`; `CCP.Avalonia/App.axaml.cs:25` initializes the non-null service, whose paid check is already correct. The WPF nullable-service fallback is therefore unnecessary.

No blocked-by-host portions. No new window or active surface: reuses the existing vault card and shell route; no PanicSurfaces entry needed.

## Verification limits

- SDK: .NET 10.0.100. Tests used `DiffEngine_Disabled=true` and the assembly's `CCP_USERDATA_DIR` sandbox; the new test explicitly registers bundled Inter fonts.
- `dotnet build <project> -c Release`: exit 0, zero errors for CCP.Core, CCP.Avalonia and all 12 Linux test projects named in `.github/workflows/build.yml` (CatalogueStartup, Consent, GeneralSettings, Language, Migration, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, Core.Tests).
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. WPF and Windows-specific code compiled but were not run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The initial sandboxed attempt lacked temporary-file write access; rerun with that access passed.
- `dotnet test Tests/CCP.Core.Tests -c Release` (and a full `--no-build` retry): exit 1, 3,952 passed / one failed / five skipped. Both full runs fail `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` at line 185 (expected Loops, actual Clips). The test passes alone with `--filter FullyQualifiedName~DtrhRemoteMediaTests.Take_ColdCacheFetches` (exit 0). No Core production or existing test code changed. Five existing Vosk tests skip without the unavailable model.
- New focused Avalonia test: initial fixed run exit 0 (one passed). Restored the old production action, rebuilt and ran it: exit 1, `Assert.NotNull` fails because no vault card opens. Restored the fix afterward.
- Final focused test rerun: `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~IntakeSpentOfferTests`, exit 0, one passed / zero failed / zero skipped (7 seconds).
- An initial overlapping Avalonia build hit a PDB file lock; the subsequent sequential project build passed with zero errors.
- `test-plan-coverage.sh` is absent on this base; no ledger row was added or promoted.
- `git diff --check`: exit 0. No full Avalonia suite, real desktop, Vosk download, or Windows runtime tests were run.

## Assumptions

- Reuse `ShowVaultGate("gradedintake", 1)` as the existing Avalonia counterpart of WPF `OpenPricedCard("gradedintake", 1)`; this also preserves the head's Lockdown check and Account fallback if card construction fails.
- Keep the existing gate unlock button behavior: this commit changes the spent notification action, not the separate Account/sign-in button.
