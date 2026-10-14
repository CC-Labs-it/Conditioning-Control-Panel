# wire-shell-spiral-room

- BASE: `avalonia-port/goongame-core-c`. Claims/publication use the user-selected **ObviouslyNotMich** GitHub connection; only this claimed cloud branch is updated, with a fast-forward.
- Saved unanswered Descent migration offers now keep the Spiral Room in fog, including when the countdown fuse is dark. A valid queued answer or completed acknowledgement releases the hold, matching WPF's `DescentMigrationService`.
- Moved that pure rule into existing Core `SpiralRoom`; WPF delegates to the shared implementation. The visible Avalonia tab observes the three persisted gates and settings-instance replacement after cloud restore, and releases both subscriptions on hide/detach. Queued callbacks cannot restart a detached room.
- Added three headless regressions and ten Core rule cases. The existing shell test now uses the shipped `Program.BuildAvaloniaApp()` bootstrap, retaining its behavioral assertions and loading the app's font fallback.
- Only ledger row `shell-spiral-room` is changed, retaining **stub**. Follow-up **wire-shell-spiral-room-b** owns live offer/ceremony ownership, DescentService Current/BlockChanged/hasBlock and block embedding, and BeginSpiralFirstLight/DescentShowDirector caller integration. These cannot be claimed as wired by this persisted-state slice.
- WPF is the source: `MainWindow/MainWindow.SpiralRoom.cs` and `Services/Descent/DescentMigrationService.cs`. Fetched `parity/trunk` read-only; no code imported from that branch.

## Verification limits

All commands ran from this worktree with .NET SDK10.0.402 (`PATH=/tmp/ccp-dotnet:$PATH`, `DOTNET_ROOT=/tmp/ccp-dotnet`, `DiffEngine_Disabled=true`). Builds use `-c Release -m:1` and all three `CreateHardLinksFor*` properties to conserve disk. Auxiliary test builds use `BuildProjectReferences=false` after Core/head builds.

- Before the production fix, `dotnet test Tests/CCP.Avalonia.Tests --filter FullyQualifiedName~SpiralMigrationGateTests`: **3 failed,0 passed**, because the room ignored persisted unanswered offers. Log `/tmp/ccp-spiral-failproof.log`.
- After the fix, `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~SpiralMigrationGateTests|FullyQualifiedName~SpiralRoomShellTests'`: **4 passed,0 failed,0 skipped**. Log `/tmp/ccp-spiral-ui-final.log`.
- `bash .github/scripts/core-guards.sh`: **pass**, checked424 XAML files,1591 head types and1880 Core types. Log `/tmp/ccp-spiral-guards.log`.
- `dotnet build` of Core, Avalonia and every Linux test csproj in `.github/workflows/build.yml`: **all13 passed**, exit0. Per-project results `/tmp/ccp-spiral-build-results.json` and `/tmp/ccp-spiral-build-*.log`. Optional WPF test cross-build with `EnableWindowsTargeting=true`, `ValidateExecutableReferencesMatchSelfContained=false`, `UseSharedCompilation=false` reached the **360-second bound**, exit124; final Windows app/test compile was not established, and Windows execution was not attempted.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: **2820 passed,1 failed,5 existing optional Vosk-model skips**, total2826. Failure: unrelated existing `BoardTilesTests.A_frame_fits_the_budget` measured19.97ms under parallel load; its isolated rerun also failed. No tests were changed or disabled. Full log `/tmp/ccp-spiral-core-final.log`, isolated log `/tmp/ccp-spiral-board-isolated.log`. Focused `--filter FullyQualifiedName~SpiralWithholdRulesTests`: **10 passed,0 failed,0 skipped**, log `/tmp/ccp-spiral-core-focused.log`.
- `CCP_USERDATA_DIR=/tmp/ccp-spiral-profile dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-spiral-render`: **224/224 views rendered**, exit0. Inspected `SpiralTabView.png`: the default unoffered profile correctly shows the waiting panel. The changed unanswered-offer fog path is exercised by headless tests, not by that unseeded PNG. Log `/tmp/ccp-spiral-render.log`.
- `git diff --check`: pass. `git diff -M --numstat origin/avalonia-port/goongame-core-c` totals **227 added lines**, below1000.

Windows runtime, network-backed blocks, live migration offers/ceremonies, and the full First Light ceremony cannot be exercised by this Linux persisted-state slice. No real profile, notifications, or desktop was used; headless tests use the suite's isolated profile and rendering uses `CCP_USERDATA_DIR=/tmp/ccp-spiral-profile`.
