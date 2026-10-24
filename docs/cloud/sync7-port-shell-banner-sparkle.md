# sync7-port-shell-banner-sparkle

- Audited WPF commit `4d040ff1c9a06de197aefee44b6def7a97ef5c80`
  (`fix(shell): ignore stale banner sparkle completions`) against base
  `39260f198fad67d90bc988c63a00480830e5fefc`.
- **blocked-by-host**: Avalonia has no shell banner sparkle layer, sprite run,
  clear operation, or animation completion handler to guard. The existing header
  is `CCP.Avalonia/Views/Windows/MainShellWindow.axaml:941` (text beats) and
  `:1012` (sheen only). The sheen's separate BeatLoop lifecycle is in
  `CCP.Avalonia/Views/Windows/MainShellWindow.ChromeFx.cs:102` and `:131`.
  Searching CCP.Avalonia and CCP.Core finds no `BannerSparkleLayer`,
  `BannerFxRules`, or `ClearBannerSparkles`; program-card sparkles and the wallet
  are different surfaces.
- WPF spec: `ConditioningControlPanel/MainWindow/MainWindow.BannerFx.cs:34`
  allows removal only for the current run, outside clearing, with the sprite
  still present. `:402` checks it in the completion callback and logs failures;
  `:424` invalidates the run before clearing and resets the clearing flag in
  finally. `Tests/ConditioningControlPanel.Tests/BannerSparkleLifetimeTests.cs:7`
  covers live, stale, clearing, and absent sprites.
- No production code or test was added: an unused predicate would not fix a
  running surface, and importing the missing banner FX host would re-port
  sibling work outside this unit. No new surface needs PanicSurfaces
  registration; no ledger row was changed or promoted. No behavior change
  exists for a fail-before-fix demonstration.
- Import reference: fetched `parity/trunk` read-only. No trunk code was imported;
  the only scoped source commit is the WPF hash above.

## Verification limits

- `dotnet build <project> -c Release --nologo`: exit 0 for CCP.Core,
  CCP.Avalonia and all 12 Linux test projects listed in build.yml:
  CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests,
  CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests,
  CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Snapshot.Tests. Every build ended with `0 Error(s)`.
- `dotnet test Tests/CCP.Core.Tests -c Release --nologo`, with
  `DiffEngine_Disabled=true`: exit 0; Failed: 0, Passed: 3953, Skipped: 5,
  Total: 3958. Existing Vosk tests self-skipped because the model is unavailable;
  no tests were changed, disabled, or removed.
- `bash .github/scripts/core-guards.sh`: exit 0; `All Core guards passed.`
  The first read-only sandbox attempt could not create temporary files; rerun
  with writable temporary storage passed.
- `bash .github/scripts/test-plan-coverage.sh`: exit 127; script absent on base.
  No ledger changes require test-plan coverage.
- Avalonia tests were compiled, not run, per the task's measured font-creation
  limitation; no landed cloud-test-fonts commit was found. Existing banner
  coverage is in HomeMarqueeTests; a sparkle lifetime class cannot exercise a
  host that does not exist. No desktop/profile interaction was performed.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release --nologo -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0;
  557 warnings, `0 Error(s)`. WPF application and tests compiled but cannot run
  on Linux.
- `git diff --check`: exit 0. Final diff: 64 added lines against the base,
  below the 1000-line cap.

## Assumptions

- Treat the missing banner sparkle lifecycle as blocked-by-host, as required by
  the unit, rather than expanding scope to build it. Apply the completion guard
  when that host lands, with a headless lifecycle regression test proving a stale
  completion cannot remove sprites after clear/restart.
