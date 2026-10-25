# fin5-shell-spiral-room

- Adapted the Core server block reader from read-only trunk commit `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038` (`CCP.Core/Services/Descent/DescentService.cs`). Existing files were merged, never replaced from trunk.
- Connected sign-in, logout and successful sync to the reader. Offline/missing credentials never fetch; read failures retain the last block; account reset discards in-flight replies; the shared refresh gate coalesces requests.
- Connected the visible Spiral room to block changes and the existing embed handshake. Initial state, subsequent updates, withdrawal, hidden/detached teardown and re-entry now use server data. Never fabricate a block from local XP.
- Added spiral-state-only entry notification through the existing bark engine and lifetime rule, without another latch. Added panic teardown for the room browser, audio, clocks and subscriptions.
- Added reader and headless room behavior tests and numbered manual tester steps. Kept the ledger `stub`: migration ownership is still missing.

## WPF gap audit and scope

Reference pinned at base `76c341cb5a963901a215f9ca233a7b0a3746e510`:

1. `MainWindow/MainWindow.SpiralRoom.cs:76` is a no-op after the navigation rework. Existing fuse-chip navigation remains the fog-era door.
2. `Views/Tabs/SpiralTabView.xaml.cs:248-333` subscribes to DescentService.BlockChanged, forwards block updates and recomputes hasBlock. Implemented here, including symmetric unsubscribe.
3. `Views/Tabs/SpiralTabView.xaml.cs:432-449` reads DescentMigrationService.SpiralWithheld. Persisted settings already work on the base; live offers and holds remain missing.
4. `Views/Tabs/SpiralTabView.xaml.cs:361-383` announces entry only in the spiral state and lets the bark rule own the once-ever latch. Implemented here.
5. `MainWindow/MainWindow.SpiralRoom.cs:96` navigates before first-light reveal, but it has no live WPF user path. At this pinned SHA, `git grep -n -E 'RunFirstLightReveal|BeginSpiralFirstLight|BeginFirstLight\(' -- ConditioningControlPanel` finds the definitions and the internal helper chain only. `DescentShowDirector.cs:399-426` explicitly documents ceremony retirement and the scratch demo rig as the only caller. No new ceremony or automatic fullscreen reveal should be introduced.
6. `DescentMigrationService.IsCeremonyOpen` is always false; offers restore silently. The remaining active dependency is the migration host, not the retired ceremony window.

Remaining work is **DescentMigrationService and its sync integration**: server offer parsing, depth-counted holds, silent restore, keepsakes, watermark clearing, durable pending submit and acknowledgement ordering, chrome restore, and daily stage-drip delivery. Core has arithmetic and acknowledgement helpers, but no offer owner. `SyncPush.cs` deliberately does not advertise descent_auto. Enabling that flag without the complete local-first transaction and submit would accept offers this head cannot fulfill. This unit retains that fail-closed contract.

The WPF migration host alone is 448 source lines, before adapting its sync body/offer callers and adding transaction/restart/account-switch tests. Together with this unit's reader, room wiring and behavior coverage, that exceeds the 1,000-added-line cap. The user-visible block feed and welcome are delivered first. No claim of complete parity or P122 promotion is made; the row still has a live missing dependency. The first-light render demo remains the pre-existing soft fallback.

## Verification limits

- .NET 10 SDK installed; verification uses `DiffEngine_Disabled=true` and `DOTNET_ROOT` for xUnit subprocess discovery.
- `dotnet build CCP.Core/CCP.Core.csproj -c Release` and the Avalonia head: exit 0, zero errors. The initial concurrent build outputs were discarded and Core rebuilt cleanly before the passing test run.
- Every Linux-compatible test csproj named by `.github/workflows/build.yml` compiled with zero errors: Core, Core.Settings, Core.Content, Avalonia, Avalonia.CatalogueStartup, Avalonia.Consent, Avalonia.GeneralSettings, Avalonia.Language, Avalonia.Migration, Avalonia.Report, Avalonia.Scheduler, Avalonia.Snapshot. Later test-project builds used `-p:BuildProjectReferences=false` against already-built references.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: exit 0; **4,124 passed, 5 skipped, 0 failed**. The existing five Vosk skips are model-dependent; no tests were disabled or changed to skip.
- Focused `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~SpiralLiveBlockTests|FullyQualifiedName~SpiralRoomShellTests|FullyQualifiedName~SpiralMigrationGateTests|FullyQualifiedName~SpiralEmbedViewTests|FullyQualifiedName~PanicSurfacesTests'`: exit 0; **16 passed, 0 skipped, 0 failed**. Fonts work on this snapshot.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors. Compiled only; Windows UI tests were not run.
- Fail-proof: temporarily restoring the base's `hasBlock: false` gate made `SpiralLiveBlockTests` fail at the expected spiral-state assertion (exit 1, one failed test). The source was restored unconditionally, then rebuilt: final head and test builds exit 0, and the final focused run passed all 16 tests (exit 0).
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed. `git diff --check`: exit 0. The test-plan coverage script is absent on this base, so that optional gate could not run.

Windows UI behavior cannot run on this Linux host. No real browser, user profile, notification or device is used by the new tests; they use the suites' CCP_USERDATA_DIR sandboxes and fake HTTP/embed seams.

## Assumptions

- Follow current WPF silent migration behavior, rather than restoring its retired ceremony merely because older ledger prose names it.
- Preserve trunk's explicitly limited progression handoff (curve-epoch adoption); cross-device XP adoption is outside this room row and is not newly claimed here.
- Existing native embed availability and deployment remain prerequisites for seeing the remote map; the waiting-room fallback remains authoritative when unavailable.

Added lines against the required base: **730** before this accounting note; the final git measurement is recorded in the task result.
