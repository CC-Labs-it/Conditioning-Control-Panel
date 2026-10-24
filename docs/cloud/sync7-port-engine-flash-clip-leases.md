# sync7-port-engine-flash-clip-leases

- Audited WPF commits `73438ce6e8333acc04e87476bf76dfcd238f50d9` and
  `175d692fedfa54c0a93cca3f1ae6498d5ff191c0` against base
  `39260f198fad67d90bc988c63a00480830e5fefc` using `git show` and `git show --stat`.
- Both are **blocked-by-host**, not already covered. No production behavior was
  changed. No ledger or parity rows were edited.
- Fetched read-only `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`.
  No code was imported from that branch.

## Commit disposition

### `73438ce6e` — blocked-by-host

WPF adds live clip leases (8 total, 5 shared by non-flash clips), oldest-evictable
replacement, idempotent release, missing-file rejection, and player acquire /
release / refusal diagnostics. Release is immediate while native teardown is deferred.

Missing host: an Avalonia silent flash clip player and its callers. The current
flash loader selects local files at `CCP.Avalonia/Views/Overlays/FlashOverlay.cs:450`,
then decodes each to one bitmap at line 478. The window accepts that bitmap at
`CCP.Avalonia/Views/Overlays/FlashOverlayWindow.cs:212`, assigns it to an Image at
line 223, and disposes it on close at line 235. Neither head nor Core has
`FlashClipPlayer`, `ClipSlotBudget`, or lease consumers. Existing full video/audio
players are separate surfaces and are not the silent flash player this commit fixes.

Test: no port test exists because there is no executable clip lease behavior on
this head. Adding an uncalled budget solely to test it would violate the unit's
no-unused-import rule. A future host must test all lease release paths, reservation
and eviction order, and absence of acquisition for a missing clip.

### `175d692fe` — blocked-by-host

WPF adds a limit of 8 in-flight native teardowns, allocation-free budget scans,
poster upgrade / eviction / linger decisions, lending during gentle fade, and
remote-file ownership transfer with finally cleanup and deletion outside the lock.

Missing host: the same silent player, plus the remote flash clip pool and
poster-to-clip heartbeat. A search of `CCP.Core` and `CCP.Avalonia` finds no
`RemoteClips`, `ClipRefusedAtCap`, `FlashClipUpgrade`, or flash clip player.
`CCP.Avalonia/Views/Overlays/FlashOverlay.cs:450` is the local bitmap load path;
its leaving-image path at line 375 has no player to mark evictable. There is no
remote clip ownership or poster fetch transaction to repair.

Test: no production change to revert for a red/green proof. The WPF commit's
`FlashClipSlotTests` remain reference tests; transplanting pure decisions without
an actual caller would not prove this head's behavior. A future host must test
teardown refusal and retry, eviction followed by linger, and cancellation/stale
remote fetch cleanup before these fixes can be considered ported.

## Assumptions

- Applied the explicit blocked-by-host rule to both commits. Building the missing
  online flash media/player pipeline would exceed this two-commit unit.
- No new surface means no new PanicSurfaces registration. No ledger promotion
  means no new tester step. Existing bitmap flashes are not clip parity.

## Verification limits

- `bash .github/scripts/core-guards.sh`: exit 0; all Core guards passed.
  The first attempt could not create temporary files in the read-only sandbox;
  rerunning with write access passed without source changes.
- `dotnet test Tests/CCP.Core.Tests -c Release -p:BuildProjectReferences=false`
  with `DiffEngine_Disabled=true` and isolated `CCP_USERDATA_DIR`: exit 0;
  3,953 passed, 0 failed, 5 skipped (3,958 total). The existing Vosk speech tests
  skip without their model. `bash .github/scripts/fetch-vosk-model.sh` returned
  exit 22 / HTTP 403, so speech recognition is not verified here.
- Avalonia tests are compiled only; execution is deferred to the supervisor's
  GitHub check per the supplied font-creation environment limitation. No fix
  for that limitation is present in the relevant flash test app builders.
- No Windows behavior was run on Linux. No interactive desktop was opened.
- No new behavior, regression tests, or production-line reversion proof: both
  commits require the missing host described above.
- The base has no `.github/scripts/test-plan-coverage.sh` (attempt: exit 127).
  No ledger rows were added or promoted.
- SDK: .NET 10.0.402. The channel lookup initially reported HTTP 403; the explicit
  SDK download succeeded.
- `git diff --check`: exit 0.

Build commands: `dotnet build <project> -c Release --nologo`.
All Linux builds exited 0 with 0 errors (existing warnings remain).

| Project | Exit |
| --- | --- |
| `CCP.Core/CCP.Core.csproj` | 0 |
| `CCP.Avalonia/CCP.Avalonia.csproj` | 0 |
| `Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj` | 0 |
| `Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj` | 0 |
| `Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj` | 0 |
| `Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj` | 0 |
| `Tests/CCP.Core.Tests/CCP.Core.Tests.csproj` | 0 |

WPF cross-compile: `dotnet build
Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release
-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false
--nologo`: exit 0; 557 warnings, 0 errors. Compiled only, not run.

BLOCKED: Both requested commits need the absent Avalonia silent flash clip player,
remote clip pool, and poster upgrade lifecycle; the host is outside this unit.
