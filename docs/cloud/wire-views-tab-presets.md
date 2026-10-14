# wire-views-tab-presets: Presets tab stub -> wired

Ledger row `views-tab-presets` goes from stub to wired. These are the items the row still listed as missing:

- **Corner-GIF option panel** (WPF `MainWindow.Presets.cs:515-636`): the panel is shown for `HasCornerGifOption` sessions and carries the session's localized `corner_gif_desc`. Picking a row resets it to unticked. The toggle reveals the settings. Select GIF uses the StorageProvider (opens in the images folder) and labels the button `📁 <file>`. The size and opacity labels show `{n}px` / `{n}%`; their `{loc:Str}` bindings are removed so a language switch no longer overwrites them. Start writes the option into the session (`StartSession :1586`, via `MainShellWindow.StartSession`). The #474 live edits reach a running session.
  - Corner picks apply at once.
  - Size and opacity changes wait for the 400 ms debounce. This is a `ponytail:` shortcut: WPF applies opacity instantly, but here every live edit re-shows the overlay.
- **Session-scoped corner GIF**: ported into Core `SessionRunner` from WPF `SessionEngine` (`:198/356/531/575/862-896/1731-1797/2126-2320`). It covers:
  - the admission check at start, on every tick and through `RefreshCornerGifPolicy`;
  - the delayed start and end minutes;
  - hiding it on pause;
  - handing the corner back on the terminal close;
  - the #474 live edit (`UpdateCornerGif`).

  `RefreshCornerGifPolicy` is called from three places: the Spiral card's "allow session corner GIF" switch (now live), a standalone-slot refresh, and the fullscreen spiral closing. Our one change from WPF: a delayed start is not re-raised after its end minute (WPF flickers open and shut every tick there).
- **Admission rules moved to Core**: `CornerGifPlanner.AllowSessionCornerGif`, `SessionCornerArtIsSpiral` and `AllowStandaloneCornerGif`. WPF `CornerGifMedia` now forwards to them, so there is one copy.
- **`CornerGifOverlay` session slot**: drawn through the new `CoreCornerGif.SessionShowHandler` / `SessionCloseHandler` seams.
  - It is not staggered and never falls back to the Spiral Library pick (as WPF).
  - It stays outside the standalone restore sentinel and outside `HasActiveOverlays`.
  - Standalone slots stand down while it is up and come back on the handback.
  - Panic closes it through the existing `corner-gif` surface (`CornerGifOverlay.StopAll`), so `PanicSurfaces` needs no new line.
- **Selected-card sheen** (WPF `OnPresetsTabVisibilityChanged` / `RefreshCardSheen` / `FindSelectedCard`): `CardSheenAdorner` sits on the selected preset chip or rack row. It shows only while all of these hold: the tab is shown, the window is active and not minimised, and ambient loops are allowed. It is re-seated after every rail or rack rebuild.
- **Rack row hover reveal** (WPF `SdRackActions`): action buttons appear only under the pointer. At rest they are transparent and unhittable, so the row's right edge does not shift.
- **Catalogue chip drag-over icons (📥/❌)**: nothing to port. WPF's `SessionDropZone_DragEnter/Over/Leave/Drop` are never subscribed; `PresetsTabView.xaml:914` hooks only `MouseLeftButtonUp`.
- **Share**: was already live (`SharePresetToCatalogueAsync` / `ShareSessionToCatalogueAsync`). Only the ledger note was stale.
- **Takeaway shelf rows**: still owned by row `shell-takeaway` (they need JustDropOrdersService). This tab paints WPF's empty state.
- **Ledger**: the `views-tab-presets` row appeared twice (duplicate lines). It is collapsed into one row and set to wired.
- **Trunk credit**: `parity/trunk` 7bb4351d8 (session-scoped corner GIF, live Spiral switch) and 7fdf53167 (session detail corner GIF option) cover the same ground. This branch was written against our tree with the same design (a Core runner seam plus a `CornerGifOverlay` session slot). I added its `RefreshCornerGifPolicy` call sites (Spiral switch, fullscreen spiral close, standalone refresh) after comparing.

## Verification limits

- `dotnet test Tests/CCP.Core.Tests`: 2789 passed, 6 skipped, 2 failed. Neither failure involves this change:
  - `LoopbackOAuthTests.GoodState_OnEitherLoopback…("[::1]")`: the container has no IPv6 loopback.
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted…`: running as root defeats the undeletable-folder setup.
- `SessionRunner*` Core tests: 25 passed, 0 failed (the new `SessionRunnerCornerGifTests` has 6 facts plus a 7-case admission theory).
- Avalonia headless, filtered on `CornerGif|PresetsTab|PresetsSessionCatalogue|SessionRunWire|StudioPresetsWire|CoreStandIn|PanicSurfaces|CatalogueShell|SessionIoTests|AssetSubmitWire|ProgramsRunLifecycle|Spiral|AmbientBeatLoops`: 75 passed, 0 failed. That includes the new `PresetsTabCornerGifWireTests` (2).
- Fail-proven by mutation; each mutation turned a test red:
  - hover class removed;
  - sheen refresh removed;
  - option panel forced hidden;
  - no apply at start;
  - no standalone stand-down;
  - pause keeps the overlay;
  - no end-minute guard;
  - no handback on stop;
  - no policy close.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- Builds with 0 errors: CCP.Core, CCP.Avalonia, every Linux test project in build.yml, and `Tests/ConditioningControlPanel.Tests` (with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`).
- Compiled, not run:
  - the WPF `CornerGifMedia` forwarders (Windows only);
  - the corner overlay on a real X11 desktop. The headless tests use `SkipPlatformChecksForTests`, and the StorageProvider GIF picker cannot open headless, so the test feeds the picked path through `SetCornerGifPath`.
- `dotnet run --project CCP.Avalonia -- --render-all`: 221/221 views rendered. `PresetsTabView.png` shows the rack rows at rest without their action buttons. render-all does not open the Gamer Girl detail, so no PNG shows the option panel.
