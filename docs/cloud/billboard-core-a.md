# billboard-core-a: Tonight Board's pure board rules move to CCP.Core

Unit A of moving `ConditioningControlPanel/Services/Billboard` into `CCP.Core/Services/Billboard`.

## Inventory (Services/Billboard on base `avalonia-port/first-show`)

Already in Core before this unit (WPF already routes to them, nothing to do):
`BillboardContract`, `BillboardCards` (incl. `TipCards`), `BillboardAppProviders` (Waiting/Resume/Event
providers + `BillboardProviderBase`), `HouseProvider`, `CardText`, `EventCards`, `ResumeCards`,
`WaitingCards`, `Showcase/*` (Provider, Manifest, Cache, Rules, State), `DashboardBillboard`.

Still WPF-only before this unit: `Board/*` (Wire, FxMath, Picture, Raster, Service, Service.Demo,
Provider, ArtRegistration), `Providers/*` (LiveProvider + LiveCards, TipProvider, BillboardProviders),
`BillboardWiring`, `BillboardArt`, `Showcase/ShowcaseArtRegistration`.

## What changed

- `git mv` to `CCP.Core/Services/Billboard/Board/` (namespace unchanged, so WPF callers
  `BoardService`, `BoardProvider`, `Controls/Billboard/BoardTileView` compile unchanged):
  `BoardWire.cs` (wire parser + `BoardRules` + `BoardLinks`), `BoardFxMath.cs` (effect curves,
  `BoardScene`, `BoardFxSet`, `BoardRipple`), `BoardRaster.cs` (tile layout + renderer),
  `BoardPicture.cs`. The first three are byte-identical renames.
- `BoardPicture.Decode` lost its WPF imaging: Core now calls a `PngDecoder` seam
  (`Func<byte[], (int[] Pixels, int Width, int Height)?>`), adapted from parity/trunk
  `6230edd65` (feat(board): Tonight Board Core). The size cap became `BoardPicture.FitsStrip`,
  one rule used by Core and by the decoder (checked before the pixel copy, as 7.1.5 did).
- WPF: new `Services/Billboard/Board/BoardPng.cs` holds the old WPF imaging decode verbatim;
  `App` installs it next to the other Core seams. Behaviour is unchanged.
- Tests: `BoardTilesTests` and `BoardGlowTests` moved to `Tests/CCP.Core.Tests/Board/` (renames).
  The tests that need WPF or not-yet-moved types (BoardService download/cache, BoardProvider card,
  WPF PNG decode through the seam, the BoardTileView lifecycle, BoardTouch) moved unchanged into a
  new `Tests/ConditioningControlPanel.Tests/BoardServiceTests.cs`, which installs `BoardPng` the
  way App startup does. `BoardGlowTests`' off-by-default desk-check `SavePng` now writes the PNG
  itself (zlib + CRC, no codec in Core).
- No embedded resources moved, so no core-guards baseline entry is needed. No Avalonia UI.

## Next units

- **billboard-core-b**: `BoardService` (+ `.Demo`) and `BoardProvider`/`BoardArtData` to Core
  (seams: `CorePaths` user-data dir, Serilog, `CoreDispatch.PostProvider` for `PostToUi`, `Loc`);
  then their tests move from `BoardServiceTests` to Core.
- **billboard-core-c**: `Providers/LiveProvider.cs` pure half (`LiveCards`) to Core (needs the
  Lobby snapshot types in Core), `TipProvider` to Core; `BillboardProviders` keeps only the WPF
  `BillboardHead` wiring.
- **billboard-core-d**: `BillboardWiring`'s provider list/`CurrentTier`/`Context` to Core behind a
  tier provider seam (App.Patreon); WPF keeps the art registration and the back-room door.
- Stays WPF by design: `BillboardArt` (FrameworkElement registry), `BoardArtRegistration`,
  `ShowcaseArtRegistration`, `BoardPng`, `Controls/Billboard/BoardTouch`.
- **Avalonia**: board tile view + a Skia `BoardPicture.PngDecoder` (trunk `CCP.Avalonia/Controls/Billboard/BoardTileView.cs`).

## Verification limits

- Run here (Linux, .NET 10.0.112): `dotnet test Tests/CCP.Core.Tests -c Release` (CI's config):
  1239 passed, 5 skipped, 2 failed; both failures are sandbox-only and fail identically on the
  base branch: `LoopbackOAuthTests...(host: "[::1]")` (no IPv6 loopback here) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the lock
  is not enforced). Board filter: 81/81 pass in Release.
- `A_frame_fits_the_budget` measures 12-13 ms a frame in a Debug build on this 4-vCPU sandbox
  (limit 12 ms) and passes in Release; CI runs Release. The test was not changed.
- `bash .github/scripts/core-guards.sh`: all guards pass.
- Built (Release): CCP.Core, CCP.Avalonia, Tests/CCP.Avalonia.Tests and every Linux test project
  in build.yml (Core.Settings, Core.Content, Avalonia.Consent/Language/CatalogueStartup/Scheduler/
  Report/GeneralSettings/Snapshot): 0 errors.
- `Tests/CCP.Avalonia.Tests` Billboard classes (BillboardShowcaseTests, DashboardFoldBillboardTests,
  BillboardAppProvidersTests): 6/6 pass. No Avalonia code changed.
- Compiled but not run: the WPF head and `Tests/ConditioningControlPanel.Tests` build here with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  (0 errors), so `BoardPng.cs`, the `App` seam line and `BoardServiceTests` compile; they cannot
  run on Linux (WPF imaging / STA), so the WPF decode path and the residual WPF tests are unrun.
