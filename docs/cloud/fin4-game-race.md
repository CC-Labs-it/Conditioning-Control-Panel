# fin4-game-race

- Base verified: `73b1ccf0d793bb3fde6091c18a9fa6b5c692964a`. The supplied checkout differed and was read-only; used the explicitly authorized independent clone fallback.
- Adapted directly from WPF at that SHA: CaucusHostService native audio payload filtering/clamps, sfx, fullscreen acknowledgement, owned report, favorites, live Loom list, ownership watcher, mandatory-video pause/resume, kept Escape, track stop on run-end/exit and payout meta snapshot.
- Reused the existing audio-web pack service and EMI usage store. Added Core video-end notification (subscriber failures cannot prevent teardown) and the WPF bubble-batch counter/unlock/save rule. The head credits all crossed sparkle milestones, one milestone popup and quests.
- Kept the race and its report under the existing `games` panic registration. Loom URLs expose only validated GIF names, with existing symlink containment checks; profile JSON remains unserved.
- Replaced duplicated historical ledger prose with one current `stub` row and numbered manual steps. No trunk code imported. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; WPF on the pinned base was the implementation source.

## Remaining WPF gaps

The starting row's historical claims about no window, launcher, charting or player were superseded by existing code. Remaining gaps at inspection were cloud levels, Back Room access/handoff, native audio/sfx, favorites/Loom, report/fullscreen, video pause, ownership watcher, payout meta, bubble credit, SeasonRecap, kept Escape, pack request, EMI usage, creator media and graceful panic.

After this change the missing list is:

- BambiCloud: the WPF RaceCloudWindow (519 lines), its injected remote-player bridge, plus CaucusHostService cloud clocks/chart download, ownership refusals and next-track swap handling. Those paths alone span over 1000 WPF lines before adaptation/tests. Porting just a browser opener would advertise levels without their timing/ownership guarantees. `settings.cloud` stays false.
- Back Room: mystery card, shared web-host navigation and return callback. The Avalonia Games catalogue has no Back Room game/room host; this needs the room host and its bridge, not a race-only callback.
- Creator mods: DtrhModContent init/media origins and mod cue override resolution. Stock media and downloaded web audio use the existing server.
- SeasonRecapService.TrackFeature: only recap models/views and FeatureDayLog exist here, not the season rollover/statistics host service. A direct settings increment would bypass its rollover rules.
- Graceful panic: existing `games` panic closes immediately. Normal page exit and ownership loss use their watchdog paths. The immediate safety teardown remains deliberate (`ponytail`); no claim of full parity.

## Verification limits

- .NET 10 installed successfully (channel metadata first returned 403; a fixed SDK install succeeded, and the channel install subsequently completed).
- `dotnet test Tests/CCP.Core.Tests -c Release` with `DiffEngine_Disabled=true`: exit 0; 3955 passed, 5 existing Vosk-model tests skipped, 0 failed. No test was weakened or disabled.
- Focused Core fail proof: temporarily neutralized only the new video-end notification and bubble batch implementation, ran the two regressions: exit 1, both failed their behaviour assertions. Restored implementation before the full passing run.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~GameWindowTests.Race_`: exit 1; all 15 tests stopped in existing shell font setup (`Could not create glyphTypeface`, Segoe UI Black). Their race assertions did not execute; UI fail-before/pass-after proof remains blocked. The updated suite compiles with 0 errors.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (429 XAML references, 1597 app types, 2144 Core types). The initial restricted-shell invocation could not create its temporary directory; rerun with writable storage passed.
- `bash .github/scripts/test-plan-coverage.sh`: absent on this pinned base; numbered tester steps added anyway.
- `git diff --check`: exit 0.
- Initial bulk builds exhausted temporary storage because each head test copies large assets. Deleted only generated test output directories and rebuilt sequentially, removing each generated test output after its result. Final results are listed below. All UI tests use the existing isolated CCP_USERDATA_DIR harness. No real desktop or profile was used. Windows-specific runtime behavior cannot be exercised on Linux. The row remains stub; no claim of live verification.

## Assumptions

- Preserve the established immediate full-panic teardown, while implementing first-Escape pause and normal graceful ownership loss.
- Reuse head services and WPF ordering; retain unavailable cloud/room controls as unavailable instead of exposing incomplete entry points.
- Leave the absent larger hosts and creator/season integration explicitly missing; their complete implementation plus behavioural tests cannot fit the unit cap alongside the visible host fixes.

## Final builds

Every command used `dotnet build <project> -c Release`. WPF tests additionally used `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`. All finished with zero errors; WPF code was compiled but not run. Existing warnings remain.

```text
CCP.Core/CCP.Core.csproj: exit 0
CCP.Avalonia/CCP.Avalonia.csproj: exit 0
Tests/CCP.Avalonia.CatalogueStartup.Tests/CCP.Avalonia.CatalogueStartup.Tests.csproj: exit 0
Tests/CCP.Avalonia.Consent.Tests/CCP.Avalonia.Consent.Tests.csproj: exit 0
Tests/CCP.Avalonia.GeneralSettings.Tests/CCP.Avalonia.GeneralSettings.Tests.csproj: exit 0
Tests/CCP.Avalonia.Language.Tests/CCP.Avalonia.Language.Tests.csproj: exit 0
Tests/CCP.Avalonia.Migration.Tests/CCP.Avalonia.Migration.Tests.csproj: exit 0
Tests/CCP.Avalonia.Report.Tests/CCP.Avalonia.Report.Tests.csproj: exit 0
Tests/CCP.Avalonia.Scheduler.Tests/CCP.Avalonia.Scheduler.Tests.csproj: exit 0
Tests/CCP.Avalonia.Snapshot.Tests/CCP.Avalonia.Snapshot.Tests.csproj: exit 0
Tests/CCP.Avalonia.Tests/CCP.Avalonia.Tests.csproj: exit 0
Tests/CCP.Core.Content.Tests/CCP.Core.Content.Tests.csproj: exit 0
Tests/CCP.Core.Settings.Tests/CCP.Core.Settings.Tests.csproj: exit 0
Tests/CCP.Core.Tests/CCP.Core.Tests.csproj: exit 0
Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj: exit 0
```
