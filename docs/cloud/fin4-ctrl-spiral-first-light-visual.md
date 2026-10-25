- Port WPF's independent 12-second block-wait dispatcher timer, with navigation/finish cleanup; ambient frames still park while minimised.
- Strengthen the shell regression: wait for the deadline while minimised without ticking visual frames.
- Include the otherwise unreachable first-light canvas in `--render-all`, sampled at 2.5 seconds.
- No trunk code imported. Read-only reference fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; existing head files win.

## WPF gap audit and assumptions

Reference: base `e33ebf892c032e4a3213e28e495513608f2aa323`.
Drawing, copy suppression, reduced-motion snapshot, hold, error fallback and navigation teardown already exist.
The independent minimised-window timeout was missing and is fixed here.
Block-in-hand completion, arrival releasing the hold and subsequent withdrawal repaint require
DescentService.Current/BlockChanged. Ordinary room selection also requires
DescentMigrationService.SpiralWithheld. Both services are WPF-head-only.
Their account/Descent host integration includes authenticated reads, refresh coalescing,
sign-in/logout, progression hand-off, migration offers, sync and stage notifications.

The reveal's production caller is dead in WPF: DescentShowDirector.cs:420-425 explicitly
states ceremony retirement on 2026-10-06 and capture-rig-only access. Proof:
`git grep -n RunFirstLightReveal e33ebf892c032e4a3213e28e495513608f2aa323 -- ConditioningControlPanel`
returns only its declaration at line 428. BeginFirstLight is reached only through the main
window entry called by that director. Assumption: preserve this retired entry instead of
introducing a new user path. Apply P122: pinned grep plus successful render-all (225/225, including the explicit canvas) closes this retired production row as wired, never verified. The account/Descent integrations listed above remain outside that retired production scope.
No new surface or panic hook: WPF has neither for this in-tab reveal.

## Verification limits

- .NET SDK 10.0.402. `dotnet build -c Release` passed (exit 0, zero errors) for CCP.Core, CCP.Avalonia and all 11 Linux test projects named in build.yml: CCP.Avalonia.CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Report.Tests, Scheduler.Tests, Snapshot.Tests, CCP.Avalonia.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests and CCP.Core.Tests. Existing warnings remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; 2,822 passed, 5 Vosk model-dependent skips, no failures. Vosk model/native speech was not exercised.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~SpiralRoomShellTests|FullyQualifiedName~SpiralFirstLightVisualTests'`: initial focused run exit 0; 7 passed, no skips.
- Before-fix proof: rebuilt the original SpiralTabView from the base, with the new test unchanged. Filter `FullyQualifiedName~FirstLightHoldsForTheBlockThenTimesOutToTheWaitingRoom` exited 1 at the fog visibility assertion (expected false, actual true) after waiting while minimised. Fixed source restored in a finally block; final green rerun exit 0, 7 passed, no skips (37 seconds).
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all <output-dir>` with an isolated profile: exit 0, 225/225 rendered; first-light PNG visually inspected. Render output is disposable, not committed.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed; 424 XAML type references checked. `git diff --check`: exit 0.
- `test-plan-coverage.sh` is absent on this base; tester coverage was added anyway.
- WPF test cross-build with EnableWindowsTargeting=true and ValidateExecutableReferencesMatchSelfContained=false: exit 0, 2 warnings, zero errors after reclaiming generated Avalonia binaries to complete output copying. Windows desktop/native browser code was not run on Linux.
- Environment recovery: original checkout metadata was read-only, so used the authorized fallback clone. Initial overlapping builds collided on an output file; retried. Copied native binaries exhausted disk; rebuilt tests sequentially while reclaiming generated outputs. The environment briefly disconnected near its memory limit, then recovered. No tests were weakened or deleted.
