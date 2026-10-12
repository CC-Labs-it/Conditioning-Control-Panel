# media-core-a: TransferCacheStore moves to CCP.Core

Unit A of the `ConditioningControlPanel/Services/Media` Core move.

## Inventory (Services/Media vs CCP.Core/Services/Media, on `avalonia-port/first-show`)

| WPF file | State |
|---|---|
| CornerGifPlanner, MediaHistoryPreviewRules, MediaHistoryService, SpiralFrames, Transfer/TransferCacheIndex | already in Core (moved earlier, e.g. 640b0b883) |
| Transfer/TransferCacheStore.cs | **moved here** |
| Transfer/AssetCompressionPlanner.cs | WPF: needs seams for DtrhAssetManifest.EnumerateActive, AnimatedWebp.IsAnimated, SKCodec frame count, VideoTranscodeLane.ProbeAsync |
| Transfer/StillCompressLane.cs | WPF: SkiaSharp decode/encode (Core has no SkiaSharp) |
| Transfer/TransferCompressionService.cs | WPF: depends on the planner and both lanes |
| Transfer/VideoTranscodeLane.cs | WPF: WinRT MediaTranscoder |
| AnimatedWebp.cs, CornerGifMedia.cs, FlashClipPlayer.cs | WPF UI (WPF imaging, Dispatcher, LibVLC to WriteableBitmap): stay in the head |

## What changed

- `git mv ConditioningControlPanel/Services/Media/Transfer/TransferCacheStore.cs -> CCP.Core/Services/Media/Transfer/` (one copy, shows as a rename).
  App.* was swapped for the Core seams that already exist: `App.Logger?.` becomes Serilog `Log.` (WPF sets `Log.Logger` from `App.Logger`), and
  `App.Settings?.Current?` becomes `CoreSettings.Current` (WPF and Avalonia both seed `CoreSettings.ServiceProvider`). Paths were already `CorePaths`.
  Namespace unchanged (`ConditioningControlPanel.Services.Transfer`), and `InternalsVisibleTo("ConditioningControlPanel")` exists, so
  no WPF consumer (GoonCacheBridge, GoonHostService, TransferInboxStore, TransferCompressionService) needed an edit.
- Adapted from parity/trunk 5628b4ecc (that commit copied the store into Core). Here it is a move, not a copy. Trunk's
  `TransferPlatform.UserDataRoot` seam was not taken, because `CorePaths.UserData` already honours the `CCP_USERDATA_DIR` sandbox.
- New `Tests/CCP.Core.Tests/TransferCacheStoreTests.cs` (9 cases; the WPF suite had no store tests to move): the LRU order
  (stale failures, then coldest unprotected, never pinned or busy, protected last with `overCapProtected`), the cap clamp read
  through CoreSettings, a rel's stale entry retired on upsert, the index round trip, a vanished artifact dropped on load, and
  preset protection read through CoreSettings.
- No embedded resources moved, so there is no core-guards baseline change.

## Next units

- **media-core-b**: AssetCompressionPlanner. Needs seams for the active pool (DtrhAssetManifest), the GIF frame count and the video probe. Move the pure WebP VP8X header check out of AnimatedWebp.IsAnimated into Core rather than copying it (trunk 5628b4ecc `TransferPlatform`).
- **media-core-c**: StillCompressLane rules (constants, ScaledSize) into Core, with the Skia encode behind a head encoder (trunk `CCP.Avalonia/Platform/TransferStillEncoder.cs`).
- **media-core-d**: TransferCompressionService plus a VideoTranscodeLane seam (WinRT engine stays in WPF; trunk c113bca7b).
- AnimatedWebp, CornerGifMedia and FlashClipPlayer stay in the head (WPF imaging and UI-thread types); Avalonia already has its own players.

## Verification limits

- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1167 passed, 5 skipped, 2 failed. Both failures are environmental
  and outside this diff: `LoopbackOAuthTests ... host: "[::1]"` (the container has no IPv6 loopback) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (it runs as root, so the chmod-denied folder can still be deleted).
  `TransferCacheStoreTests`: 9/9 passed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build` passed for CCP.Core, CCP.Avalonia, CCP.LinuxSmoke and the Linux test csprojs in build.yml.
- `dotnet build Tests/ConditioningControlPanel.Tests -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  passed, so the WPF head and its test project compile against the moved store. They were compiled but not run (that needs Windows).
- SDK: 10.0.112 from the Ubuntu archive (the egress policy blocks builds.dotnet.microsoft.com).
