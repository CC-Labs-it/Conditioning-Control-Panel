# fin2-blocked-migration-input

- Audited base `e52e7db8844d5263db4d0cb4a6d7a7acada3b1eb`. There is exactly one
  `blocked-migration-input` row, already `resolved`, not `stub`. Its original request
  for a committed scrubbed 6.11.x profile was superseded by the explicit local-only
  decision in `docs/avalonia-decisions.md`, dated 2026-10-14.
- No application changes or new surfaces: the row is a test-input blocker, not a WPF
  dialog or input control. No panic registration or new behavior tests are needed.
  Preserve its resolved status rather than manufacture a new runtime parity claim.
- Existing migration implementation: `54cb8d601` (real-profile test and three loss
  fixes), with sandbox hardening in `0018aac9f`. Read-only `parity/trunk` was fetched
  at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no code was imported from it.

## WPF comparison and remaining gaps

The row cites `RealProfileMigrationTests.cs` and the `settings-migration-test` entry
in `docs/avalonia-progress.md`, rather than WPF feature files. The latter records
768 settings keys and 9568 files surviving a real-profile startup and shutdown with
zero failures. This is historical evidence, not a run performed by this cloud job.

Relevant reference paths inspected at the pinned base:

- `ConditioningControlPanel/App.xaml.cs`: constructs SettingsService before readers,
  runs the flash migration before services, and calls SaveImmediate before exit sync.
- `CCP.Core/Services/Settings/SettingsService.cs`: WPF and Avalonia share settings load,
  migration and save behavior; no second migration implementation is necessary.
- `Tests/CCP.Avalonia.Migration.Tests/RealProfileMigrationTests.cs`: copies inputs to
  an isolated CCP_USERDATA_DIR, excludes secrets/browser data/cache/logs, runs actual
  Avalonia desktop startup and shutdown headlessly, checks settings key by key and
  copied files by hash, and narrowly permits documented expected transforms. The
  synthetic CI variant follows the same assertions and also checks secret exclusion.

No unimplemented WPF behavior remains identified for this row. The three historical
losses (unvisited Companion drawer state, preset action identities, and bundled Circe
neutral overrides) are already fixed and covered on the base. P122 is not applicable:
this is neither a dead WPF surface nor a claim based on absence of callers.

The only input unavailable to this independent cloud snapshot is a private real WPF
profile for a fresh local-only rerun. Do not request, inspect, create a substitute
claim for, or commit that profile. No other unported feature is a dependency. There
is no reason to alter application code or duplicate the ledger row.

## Verification limits

Verification results are recorded below after the checks complete. No private-profile
run, Windows execution, real desktop interaction, Vosk download or render-all is
claimed. The existing real-profile test intentionally skips without its local input.

## Assumptions

- The actual pinned base takes precedence over the task's stale description of the
  row as stub. Preserve `resolved` for an already resolved test-input blocker.
- Honor the recorded local-only decision; fresh real-profile evidence remains a
  local-input dependency, while CI can exercise the synthetic round trip.

### Results

- Checkout verification: exact expected base, clean `work` snapshot; created only
  `avalonia-port/cloud-fin2-blocked-migration-input` for delivery.
- .NET channel install encountered HTTP 403; explicit SDK 10.0.100 installation
  from the supported Microsoft builds endpoint succeeded (exit 0).
- `dotnet build <project> -c Release --nologo -v quiet`: exit 0, zero errors for
  CCP.Core, CCP.Avalonia, and every Linux test project named in build.yml:
  CCP.Core.Tests, CCP.Core.Settings.Tests, CCP.Core.Content.Tests,
  CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests,
  CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests,
  CCP.Avalonia.Language.Tests, CCP.Avalonia.Migration.Tests,
  CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests,
  CCP.Avalonia.Snapshot.Tests. Existing compiler/analyzer warnings remain.
- `bash .github/scripts/core-guards.sh`: exit 0, both guards passed (429 XAML
  references checked). Initial read-only attempt could not create temporary files;
  rerun with temporary-file access passed without source changes.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests/CCP.Core.Tests.csproj
  -c Release --nologo -v quiet`: exit 0; 3958 passed, 5 skipped, 0 failed.
  Vosk-dependent coverage remains limited by the unavailable model.
- Avalonia behavioral tests were compiled but not run, per the supplied cloud-font
  environment limitation. Relevant existing classes are RealProfileMigrationTests
  (separate migration testhost), CompanionTabVisibilityTests and
  CirceNeutralOverridesShippedTests. No tests were modified or weakened.
- Private input, Windows runtime behavior and render-all were not exercised.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj
  -c Release --nologo -v quiet -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, 0 errors,
  554 existing warnings; WPF code compiled but was not run on Linux.
- `git diff --cached --check`: exit 0. Added lines versus the pinned base: 86
  (documentation only; below the 1000-line cap).
