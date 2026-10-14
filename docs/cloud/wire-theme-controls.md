# Shared rounded Deeper controls

- Promoted the two real hub button themes to the application dictionary and added the WPF creator-lock toggle theme (6px corners, checked accent plate, hover opacity). All 15 WPF Controls.xaml keys now have a shared resource.
- Editor/player plain buttons and creator toggle use the shared themes; local explicit themes and templates retain precedence. Removed duplicated hub themes and Fluent approximations. Existing routes from the Deeper hub remain unchanged; no window/timer/panic surface is added.
- Three focused headless tests exercise actual pointer hover/press/disabled/checked input, caller colors/padding/font size, and real editor/player/hub resource use. The fixture uses production font setup and an isolated profile.
- Sources are WPF on BASE `avalonia-port/goongame-core-c` and the existing Avalonia hub themes. Read-only `parity/trunk` was fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk code imported.
- Ledger remains **stub**, named follow-up **wire-theme-controls-b**: Action/Secondary/Outline 80ms press + 150ms eased release (currently instantaneous); Secondary/Pink opt-in HudPlank drop/sheen/bevel/travel and Plank/Chunky state wiring. The shared-key slice does not claim these unfinished effects.

## Verification limits

- `dotnet build -c Release` of CCP.Core, CCP.Avalonia and every Linux test csproj named by `.github/workflows/build.yml`: **all 13 projects succeeded, 0 errors**. Flags: `DOTNET_PROCESSOR_COUNT=2`, `-m:1 -p:UseSharedCompilation=false`, and `-p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true`. Untouched baseline warnings remain.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --filter FullyQualifiedName~SharedDeeperThemeTests` (build flags above): **3 passed, 0 failed, 0 skipped**, 10 seconds. These tests use actual pointer input and real Deeper view consumers; no negative baseline run is claimed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release --no-build`: **2816 passed, 0 failed, 0 skipped**, 2m14s. No Core code changed.
- `bash .github/scripts/core-guards.sh`: all guards passed (424 XAML references, 1591 head types, 1880 Core types).
- `dotnet run --project CCP.Avalonia -c Release --no-build -- --render-all /tmp/ccp-worker1-theme-render`: **224/224 views rendered**. `DeeperEditorWindow.png` was visually inspected; it shows the shared rounded buttons and creator-lock plate. `EnhancementPlayerWindow.png` and `DeeperTabView.png` show the other consumers. Local PNGs are not committed.
- Windows desktop and WPF runtime were not run. The optional WPF test cross-build on this same unchanged BASE had previously been stopped after about 15 minutes without a completed build; it was not repeated for this theme-only slice. No successful WPF build is claimed. Remaining HudPlank/timed press-release behaviour is not implemented or tested by this branch.
