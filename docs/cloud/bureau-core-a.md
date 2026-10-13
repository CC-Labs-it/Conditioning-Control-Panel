# bureau-core-a: Bureau rules move to CCP.Core

Base: `avalonia-port/emi-line-engine`. Unit A of the Bureau port; no Avalonia UI.

- `git mv ConditioningControlPanel/Services/Bureau/BureauIndexService.cs -> CCP.Core/Services/Bureau/BureauIndexService.cs`
  (shows as a rename under `git diff -M`). The evidence index (sha256 of plaintext -> installed pack
  file, `bureau_hash_index.json` cache, per-pack counts) is now one copy for both heads.
  - App seams: `App.ContentPacks` -> three provider Funcs (`InstalledPacksProvider`,
    `ImageFilesProvider`, `OpenFileProvider`) that default to Core's `ContentPackStore.Current`
    (the Avalonia head's store); `App.UserDataPath` -> `CorePaths.UserData`; `App.Logger` -> Serilog `Log`.
  - The WPF head seeds the three providers with its own `ContentPackService` in `App`'s static
    constructor, next to the other Core seams, so WPF reads the same packs as before.
- New `CCP.Core/Services/Bureau/BureauHostRules.cs`: the pure half of `BureauHostService` — endpoints
  and constants, `TryParseTarget` ("<64-hex>:<frame>"), `BuildBatch` (cap 6, locker scoping, certify
  proposals), `NextRequest`/`SubmitRequest`/`SubmitReply`/`GoldRequest`, `Locker` (hide video-only and
  empty installed packs), `ZipMatchesPack`, `BuildRequest` (X-Auth-Token / x-admin-token), watchdog timings.
  WPF `BureauHostService` now calls these instead of its inline copies (no rule is left in two places).
- GDI+ frame decoding (`DecodeFrame`, Windows-only) moved out of the index into the WPF head as
  `ConditioningControlPanel/Services/Bureau/BureauFrameDecoder.cs`, body unchanged.
- Tests: new `Tests/CCP.Core.Tests/BureauCoreTests.cs` (12 cases: index mapping + non-image skip, cache
  hit without reopening files, no pack service = ready/empty, target parsing, batch cap/skip/scope/
  proposals, lockers rule, zip match, submit shapes, request headers). There were no WPF Bureau tests to
  move, and no WPF source-scan test reads these files, so `SourceRoots` needed no change.
- Trunk credit: the Bureau source on `parity/trunk` is identical to our base (`git diff HEAD
  origin/parity/trunk -- ConditioningControlPanel/Services/Bureau` is empty); behaviour originates in
  trunk commits `0e199bc61` (client host), `78847aee0` (evidence lockers, drag-drop install),
  `73c664300` (amend on submit), `6382a5db2` (certify mode). Nothing was imported in this unit.

## Next units

- **bureau-core-b**: a cross-platform frame decoder in Core or the Avalonia head (SkiaSharp; decode on a
  worker, hand back the JPEG data URI only), matching GDI+ GIF frame compositing so frame indices line up.
- **bureau-host-a**: Avalonia Bureau window on the shared GameWindow / WebKitGTK string carrier — init,
  next, submit (amend), inbox, stats, gold (admin env only), packs + open-catalogue, heartbeat watchdog,
  relaunch-once, exit watchdog, panic close; Contract D on 401 like the other online surfaces.
- **bureau-host-b**: pack drop install. WebKitGTK has no WebView2 `AdditionalObjects`, so the dropped
  zip path needs another route (file picker or a native drop target), then `InstallPackFromLocalZipAsync`
  + `BureauIndexService.RebuildAsync`.
- **bureau-door**: Lab card / launcher tile. Note trunk closed the WPF door again in `f59458ddf`; follow it.

## Verification limits

- `dotnet build CCP.Core` and `CCP.Avalonia`: succeeded, 0 errors.
- `dotnet build` of every Linux test project in build.yml (Core.Settings, Core.Content, Avalonia.Tests,
  Avalonia.Consent/Language/CatalogueStartup/Scheduler/Report/GeneralSettings/Snapshot): all succeeded.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`: succeeded, 0 errors (this compiles the WPF
  app, so the WPF edits below are compiled, not run).
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1438 passed, 5 skipped, 2 failed. The
  two failures (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` and
  `LoopbackOAuthTests.GoodState_OnEitherLoopback...([::1])`) fail identically on the untouched base in
  this container (runs as root, so a read-only folder still deletes; no IPv6 loopback). `BureauCoreTests`: 12/12.
- Compiled but NOT run: the WPF app and its tests (Windows-only runtime) — `BureauHostService.cs`,
  `BureauFrameDecoder.cs` and the `App.xaml.cs` seams; the GDI+ decoder and WebView2 host could not be exercised. No Avalonia test touches the Bureau, so
  none were run for it.
