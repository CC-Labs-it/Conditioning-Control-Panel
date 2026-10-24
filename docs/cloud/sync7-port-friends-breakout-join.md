# Sync 7: Breakout friend-invite Join

- Port the Breakout portion of WPF `0375913bab6a21d76a0ae443218c27d3f82a9be8`: knock/Inbox copy names Breakout, and accounts without full access can Join the existing demo station.
- Reuse `GameWindow.Launch` (single live game window and existing `PanicSurfaces` registration, `CCP.Avalonia/Views/Windows/PanicSurfaces.cs:38`). No new surface.
- Keep Join unavailable for full-access accounts until the full Breakout host exists; never substitute a demo and report Joined for that missing destination.
- Add focused routing tests and numbered manual checks in `docs/avalonia-test-plan.md`. No parity or sync ledger edits.
- Read-only reference fetched: `parity/trunk` at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`. No trunk code imported; behavior adapted directly from the specified WPF commit.

## Commit disposition

| WPF commit | Status and evidence |
| --- | --- |
| `0375913ba` | **blocked-by-host (partial port)**: Breakout naming at `CCP.Avalonia/Platform/FriendsLanding.cs:146`, demo eligibility at `:265`, launch at `:292`. Tests: `FriendsBreakoutJoinTests.BreakoutKnockNamesTheStation`, `FreeAccountJoinsTheExistingDemoHost`, `EntitledAccountDoesNotJoinADemoInPlaceOfTheMissingFullHost`. The full station is absent from `CCP.Avalonia/Views/Games/GameWindow.cs:39` (only `breakoutdemo` is registered); porting a new full-game host is outside this unit. |

Already covered on the base: `CCP.Core/Services/Friends/FriendsApi.cs:211` polls as desktop and `:382` parses platform marks; `FriendsModel.cs:170` accepts Breakout; `FriendsLandingRules.cs:250` selects its art; `CCP.Core/Localization/Languages/en.json:155` and the other shipped languages supply the copy. These were left unchanged.

## Assumptions

- This slug owns the Breakout Join portion of the listed commit. Platform-mark rendering and race routing remain sibling-lane work, rather than being duplicated here.
- The explicit missing-host rule takes precedence over full parity: accounts entitled to the full station retain the existing no-Join behavior until that host lands. Free accounts use the WPF demo route.

## Verification limits

- .NET SDK 10.0.402. `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0. `dotnet build CCP.Avalonia -c Release --no-restore -p:BuildProjectReferences=false`: exit 0, 0 errors (Core was built separately).
- `dotnet build Tests/CCP.Avalonia.Tests -c Release`: exit 0, 0 errors. All other Linux workflow test projects built successfully: Core Settings, Core Content, Avalonia Consent, Language, CatalogueStartup, Migration, Scheduler, Report, GeneralSettings and Snapshot. Retry builds used `-p:BuildProjectReferences=false` against the separately built heads, and removed completed generated outputs to fit the container disk limit.
- With `DiffEngine_Disabled=true`, `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~FriendsBreakoutJoinTests`: exit 0, 3 passed. These pure routing tests open no windows and need no font creation. Broader window/font tests were not run because of the documented environment limitation. No real browser, desktop notifications, game window or user profile was used.
- Regression proof: temporarily remove the production Breakout destination-name arm and launch call, rebuild the head and rerun the same compiled tests: exit 1, 2 failed / 1 passed. Restore those lines, rebuild: exit 0; rerun: 3 passed. The negative failures were the wrong “Back Room” copy and the absent demo launch.
- `bash .github/scripts/core-guards.sh`: exit 0, “All Core guards passed.” The initial sandboxed invocation could not create temporary files; the permitted retry passed. `test-plan-coverage.sh` does not exist on this base. No ledger row was added or promoted.
- `git diff --check`: exit 0.
- Windows code was not run. WPF test-project build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` failed with MSB4166 (child build node exited prematurely); this Linux environment cannot validate that head. No WPF source was modified.
- Initial overlapping builds produced an Avalonia AVLN9999 error; subsequent standalone builds passed. Disk exhaustion also invalidated the first Core test run; generated build outputs were cleaned before rerunning.
- Final `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0, 3,953 passed / 5 Vosk skips / 0 failed (3,958 total). A prior clean-space run had one `DtrhRemoteMediaTests.Take_ColdCacheFetches_LoopsDryBacksOff_CopiesBecomeAssetUrls` failure (“Expected: Loops; Actual: Clips”); it passed in isolation and in the final full rerun. No tests were changed, weakened or excluded.

BLOCKED: The full-access Breakout Join route requires the missing full Breakout station host. The available demo route and destination copy are ported; no new host was invented.
