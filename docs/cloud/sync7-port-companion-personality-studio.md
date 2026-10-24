# sync7-port-companion-personality-studio

- Audited WPF commits `df219f44ddbcd0bde82481506f8689b985bfe06e` and
  `8e1e7f83063cdf92ae29dc8517ee1ff947f623fe` against base
  `39260f198fad67d90bc988c63a00480830e5fefc`.
- Recorded the existing Core/localization coverage and the missing Avalonia studio host.
  No production changes, sibling ports, ledger promotions, or new surfaces.
- Fetched `parity/trunk` read-only at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No code imported from trunk; no import credit is applicable.

## Commit dispositions

### df219f44d — partially already-covered; studio blocked-by-host

Already-covered on the base:

- `CCP.Core/Services/Companion/PersonalityService.cs:329` implements `DeletePreset`.
  Lines 341–359 remove the user preset, reset an active selection to the context
  default, advance `PersonaVoiceFenceUtc`, save, and notify `PersonalityChanged`
  only when deleting the active preset. Built-in/mod presets are rejected at
  line 331; a missing id returns false at line 362. No changes made.
- All 50 added/changed localization values in the WPF commit already match in
  `CCP.Core/Localization/Languages/{de,en,es,fr,it,ja,ko,pt-BR,ru,zh-CN}.json`.
  Examples: `en.json:2214` (Strict note), `en.json:8117` (own-preset intro),
  `it.json:8219` (translated Strict note). Verified by comparing the added JSON
  values from `git show df219f44d` against the parsed Core language files.

Blocked-by-host:

- The Avalonia head has no `PersonalityStudio` dialog, draft form, or studio
  mount. `CCP.Avalonia/Views/Tabs/CompanionTabView.axaml.cs:12` explicitly leaves
  the PersonalityStudio mount to a coordinated host port.
- `CCP.Avalonia/Views/Controls/Companion/MakeHerYoursView.axaml.cs:115` routes
  the current personality actions to the separate prompt editor; line 295
  opens that editor through the shell. It is not the studio's named-preset
  draft/save workflow.
- Therefore single-line studio name sizing, own-preset identity matching,
  in-place save, Delete confirmation, and studio prompt invalidation cannot
  be applied to an existing studio. Creating that host would exceed the
  listed fix commits. These behaviors remain unported.

### 8e1e7f830 — blocked-by-host

- The same absent studio prevents applying the custom-prompt override guard
  (save a copy rather than overwrite an owned preset), and the shared footer
  row for Delete and Cancel/Save. There are no corresponding studio controls
  to amend. The prompt editor is a different workflow and is left intact.

## Verification limits

- No behavior was added or fixed, so no new behavior test, production-line
  revert demonstration, tester-plan row, or PanicSurfaces registration applies.
  Existing behavior is cited above without changing it, as required by the unit.
- The ledger and `docs/avalonia-parity.md` are unchanged.
- SDK: .NET 10.0.100. Channel installation failed on the `ci.dot.net`
  discovery endpoint (403); pinned installation from the standard build feed succeeded.
- `dotnet build <project> -c Release --nologo`: exit 0, zero errors for Core,
  Avalonia, and all 12 Linux workflow test projects:
  `CCP.Core.Tests`, `CCP.Core.Settings.Tests`, `CCP.Core.Content.Tests`,
  `CCP.Avalonia.Tests`, `CCP.Avalonia.CatalogueStartup.Tests`,
  `CCP.Avalonia.Consent.Tests`, `CCP.Avalonia.GeneralSettings.Tests`,
  `CCP.Avalonia.Language.Tests`, `CCP.Avalonia.Migration.Tests`,
  `CCP.Avalonia.Report.Tests`, `CCP.Avalonia.Scheduler.Tests`, and
  `CCP.Avalonia.Snapshot.Tests` (under `Tests/`, each with its matching csproj).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML
  references checked). The first attempt could not create temporary files in
  the read-only sandbox; rerunning with filesystem permission passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on this
  base. No ledger row is added or promoted by this documentation-only change.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Core.Tests -c Release
  --nologo`: exit 1; 3,952 passed, 1 failed, 5 skipped, total 3,958.
  The failure is `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls`
  (`Expected: Loops`, `Actual: Clips`). The five Vosk tests skip without their
  model. Production and test sources are identical to the base; no unrelated
  fix or test weakening was attempted.
- With the same environment, `dotnet test Tests/CCP.Core.Tests -c Release
  --no-build --filter FullyQualifiedName~CompanionCoreTests --nologo`: exit 0;
  4 passed, 0 failed, 0 skipped. These are existing companion tests, not proof
  of the absent studio workflow.
- Avalonia tests compiled but were not run, following the job's explicit
  cloud font-creation limitation; GitHub must run the headless tests.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release --nologo -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0;
  557 warnings, 0 errors. WPF tests and Windows UI/native code were compiled
  but not executed on Linux.
- Localization comparison: exit 0, all 50 changed values match the WPF commit.
- `git diff --check`: exit 0.

## Assumptions

- Follow the explicit blocked-by-host rule instead of inventing the missing
  studio or importing its prerequisite sibling lanes.
- Treat the existing prompt editor as a separate surface, not a studio host.
