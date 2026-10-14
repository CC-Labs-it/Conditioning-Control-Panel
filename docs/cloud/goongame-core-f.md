# refactor(core): move the Goon Game media, share-card, avatar and transfer stack into CCP.Core

Unit **goongame-core-f**, stacked on `avalonia-port/quiz-core-a`. Core move only, no Avalonia UI.

Already in CCP.Core for this area before this unit: `GoonContracts`, `GoonDraft`, `GoonMatchTypes`,
`GoonOpenTablesApi`, `GoonRng`, `GoonScoring`, `GoonWire`, `TransferCacheStore` / `TransferCacheIndex`,
and the Fyp online stack (`FypOnlineCoordinator`, `RemoteMediaFormats`). None of them duplicated a rule
moved here, so every move below is the only copy.

## What changed

- **`GoonOnlineMedia` + `GoonOnlineMediaRules` + `GoonNoiseSets`**: `git mv` to `CCP.Core/Services/GoonGame/`.
  The WPF `RemoteMediaCache` calls become two seams (`Materialize`, `ReleaseTempFile`), `App.EffectiveAssetsPath`
  becomes `CorePaths.EffectiveAssets`, `App.Logger` becomes Serilog `Log`. Adapted from trunk `2d3523b2b`.
- **`GoonShareCard`**: the pure half (frame parse, PNG magic, size caps, file-name squash) is in Core. The WIC
  decode, clipboard and save dialog stay in WPF as `GoonShareCardHandler` (git shows this as a rename of the
  WPF file). Adapted from trunk `bc101bb84`. Trunk's `PixelsOk` was not imported: nothing on this tree calls it.
- **`GoonAvatarCache`**: `git mv`; `App.Discord` becomes the `OwnDiscordProvider` seam,
  `UpdateService.AppVersion` becomes `CoreReleaseContent.AppVersion`. Adapted from trunk `a7e958635`.
- **`TransferInboxStore`**: pure `git mv`, no edits (it already used Serilog and Core's `TransferCacheStore`).
  Trunk `5628b4ecc` routes its URL base through a seam; not needed until the Avalonia head serves `ccp.cache`.
- **`GoonCacheBridge`**: `git mv`. Its page post was a `ChaosWebViewHost`; it is now an `Action<object>`
  (WPF passes `_host.Post`) marshalled through `CoreDispatch.Post` instead of `Application.Current.Dispatcher`.
  Re-attach compares delegates with `Equals` so the same page's `Post` is still a no-op re-attach.
  `App.Settings` becomes `CoreSettings.Current`. Adapted from trunk `5628b4ecc`.
- **What the bridge needs came along** (it calls `TransferCompressionService.Instance` throughout):
  `TransferCompressionService`, `AssetCompressionPlanner` and `StillCompressLane` are `git mv`ed to
  `CCP.Core/Services/Media/Transfer/` (Core already references SkiaSharp 2.88.8, so the still lane moves as is).
  `App.Settings?.Current` becomes `CoreSettings.Service?.Current` (same null behaviour). The planner gets
  two seams: `ActivePool` (WPF seeds `DtrhAssetManifest.EnumerateActive`) and `IsAnimatedWebp`
  (WPF seeds `AnimatedWebp.IsAnimated`), so the webp rule keeps one copy.
- **`VideoTranscodeLane`** is split. Core holds the targets (bitrate clamp, 720p fit, preview size and the
  12%-in preview window) and three engine seams (`Probe`, `Transcode`, `Preview`); unseeded, a probe is
  "unknown" and a transcode is `no-decoder`. The WinRT `MediaTranscoder` engine stays in WPF as
  `WinRtVideoTranscoder` (git shows a rename of the WPF file) and now reads the preview window from Core's
  `PreviewWindow` instead of computing it again. Seams shaped after trunk `5628b4ecc` and `c113bca7b`.
- **WPF wiring**: `App`'s static constructor seeds the seams (hook wiring only, no service touch).
  `GoonHostService` calls `GoonShareCardHandler.Handle` and `GoonCacheBridge.Attach(_host.Post)`.
- **Tests**: `GoonOnlineMediaTests` and `GoonShareCardTests` `git mv` to `Tests/CCP.Core.Tests` unchanged;
  the CleanSubs case moves from WPF `GoonHostRulesTests` into the Core `GoonOnlineMediaTests`
  (`PeerFetchAllowed` stays in WPF with its host). New `GoonTransferCoreTests` cover the seams: the preview
  window (fail-proven: dropping the clamp turns the 2100 ms case red), the unseeded video engine,
  the planner walking only the head's pool, an unlinked head never fetching an avatar, and the bridge
  posting through `CoreDispatch` with an equal-delegate re-attach being a no-op (fail-proven:
  `ReferenceEquals` turns it red). No `Thread.Sleep`/`Task.Delay` in any moved or new test.

Added lines vs `origin/avalonia-port/quiz-core-a`: **561** (this file included) (cap 1000).

## Next units

- **goongame-avalonia-transfer**: seed the transfer seams on the Avalonia head (Skia 3.x still lane, which
  must not use the `SKFilterQuality` resize overload; a video engine seam; the `ccp.cache` URL base for the
  loopback server; `ActivePool` from the Avalonia asset manifest).
- **goongame-avalonia-media**: seed `GoonOnlineMedia.Materialize`/`ReleaseTempFile` and
  `GoonAvatarCache.OwnDiscordProvider` from the Avalonia account and temp-cache code.
- **goongame-avalonia-sharecard**: the Avalonia clipboard/save half of `share-card` (with an IHDR pixel cap
  like trunk's `PixelsOk`).
- **goongame-core-g**: the rest of `Services/GoonGame` that is still WPF-only (`GoonHostService`,
  `GoonMatchService`, the transports, `GoonSuddenDeath`, `MatchClock`, `Rounds/`).

## Verification limits

- Built on Linux (net10.0, SDK 10.0.112): `CCP.Core`, `CCP.Avalonia`, and every Linux test project in
  `.github/workflows/build.yml` (Core, Core.Settings, Core.Content, Avalonia.Consent, .Language,
  .CatalogueStartup, .Scheduler, .Report, .GeneralSettings, .Snapshot, CCP.Avalonia.Tests): all succeeded.
- The WPF head and `Tests/ConditioningControlPanel.Tests` **compile** here with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, but were not run:
  the WPF app, `WinRtVideoTranscoder` (WinRT `MediaTranscoder`), the clipboard and save dialog in
  `GoonShareCardHandler`, and the WPF test suite are Windows-only.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests` (`DiffEngine_Disabled=true`): 2861 passed, 6 skipped, 2 failed. The two
  failures (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`,
  `LoopbackOAuthTests.GoodState_OnEitherLoopback...([::1])`) fail the same way on the base branch with this
  change stashed: the sandbox runs as root (a read-only folder still deletes) and has no IPv6 loopback.
  The moved and new classes (`GoonOnlineMediaTests`, `GoonShareCardTests`, `GoonTransferCoreTests`,
  plus `TransferCacheStoreTests`) pass: 93/93.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "Game|Arcademy"`: 22 passed, 1 skipped. No Avalonia test
  references the moved types; the Avalonia head does not seed any of the new seams yet (see Next units),
  so on Avalonia the transfer stack runs unseeded: empty pool, no video engine, no Discord avatar.
- `StillCompressLane` is compiled against Core's SkiaSharp 2.88.8 and only runs on the WPF head here; the
  Avalonia head's SkiaSharp 3.x was not exercised against it.
