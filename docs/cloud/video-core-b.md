# video-core-b: the pure video surface and browser-routing rules move to CCP.Core

Base: `avalonia-port/game-pbp`. Branch: `avalonia-port/cloud-video-core-b`.

## Already in CCP.Core before this unit

`CCP.Core/Services/Video/`: `MandatoryVideoScheduler.cs`, `NoVideosReason.cs`, `VideoMetadataCache.cs`.

## What changed

- **Moved (`git mv`, so they show as renames)** from `ConditioningControlPanel/Services/Video/` to `CCP.Core/Services/Video/`:
  - `VideoSurfaceHealth.cs`: the multi-monitor frame-watchdog, abort, browser-failure and frame-sweep rules. `Report()` called the WPF-only `VideoDiag.Log`. It now calls a new `VideoSurfaceHealth.TraceSink` seam, which the WPF `App` sets to `VideoDiag.Log` right after `VideoDiag.Start` (`App.xaml.cs`). Serilog output is unchanged.
  - `LibVlcSilence.cs`: the #1260 rule that a silent LibVLC player uses `:no-audio` and never touches Mute or Volume. The file is unchanged.
  - `Browser/BrowserSinkLabel.cs`: the #938 sink-label resolver. `App.Settings?.Current` became `CoreSettings.Service?.Current`, so a missing settings service still resolves to "no routing".
  - `Browser/BrowserUnsafeVideoCache.cs`: the persisted list of browser-unsafe clips, keyed by a stable identity plus file size. `App.Logger?.` became Serilog `Log.`; the WPF `App.Logger` is the same logger as `Log.Logger` (`App.xaml.cs:2187`). It already used `CorePaths.UserData`.
- **One copy of the rule:** the Avalonia head's five `":no-audio"` literals now use `LibVlcSilence.NoAudioOption` (`ClipArtView`, `MiniPlayerWindow`, `MediaHistoryWindow`, `BlinkTrainerSession`, `BlinkTrainerTabView`). `BillboardShowcaseTests` now checks for the constant instead of the literal.
- **Tests moved to `Tests/CCP.Core.Tests`:**
  - `MultiMonitorVideoSurfaceTests` (renamed). Its `OverlayService.ResolveVideoAnchor` section depends on WPF, so it stays in the WPF suite as `MultiMonitorVideoAnchorTests`.
  - `BrowserSinkLabelTests`, the pure resolver part. The source-read wiring checks against WPF files and `player.js` stay in the WPF suite as `BrowserSinkLabelWiringTests`.
  - `LibVlcSilenceTests.TheSilentOptionIsLibVlcsNoAudio` moved to `LibVlcSilenceCoreTests`. The WPF Mute/Volume source scan stays where it was.
- **New Core tests:**
  - `LibVlcSilenceCoreTests.TheAvaloniaHeadSilencesThroughTheCoreRuleNotALiteral`: it fails before the routing change (checked by reverting `ClipArtView.cs`: "Use LibVlcSilence.NoAudioOption instead of a \":no-audio\" literal: ClipArtView.cs").
  - `BrowserUnsafeVideoCacheTests`: remember / not-remember, a re-encode gets a fresh chance, a pack clip keeps its stable key across temp paths, empty paths. They run in the assembly's CCP_USERDATA_DIR sandbox.
  - `MultiMonitorVideoSurfaceTests.Report_SendsTheSurfaceLineToTheHeadsTraceSink`: covers the new seam.
- No embedded resources moved, so there is no core-guards baseline change.
- Trunk credit: none. `parity/trunk` still has all four files under `ConditioningControlPanel/` and has no Core copy, so nothing was imported.
- Added lines vs base: 370 (323 code and tests plus this doc), within the 1000 cap. The renamed files count only their edited lines.

## Next units (Services/Video, not yet in Core)

1. **video-core-c**: split the pure half of `Browser/BrowserVideoGate.cs` (the extension list, plus the settings and device-pin checks) from its `BrowserVideoEngine.Instance` / `BuildPageUrl` calls. Engine availability goes in as a provider Func.
2. **video-core-d**: the pure rules inside `VideoService.cs` (9k lines): selection, cooldown, the strict-lock policy and the retire/wedge ladder decisions. Extract each into a Core rules class with its tests.
3. **video-core-e**: the pure parts of `AttentionTargets.cs` (target placement and timing). The rest is WPF Canvas/visual work and stays in the head.
4. These stay in the WPF head: `VideoDiag` (Dispatcher hooks), `VideoHostBlackFill`, `ScreenMirrorService`, `WallpaperService` (Win32), `DualMonitorVideoService` and the WebView2 `BrowserVideoEngine` / `Surface` / `Window`, plus `VideoService.Browser.cs`. Each would need a Windows seam rather than a move.
5. Parity follow-up (Avalonia): `ClipArtView` still builds its player with `Mute = true`, which the #1260 rule forbids. `BillboardShowcaseTests` pins that line, so this unit left it alone.

## Verification limits

- `dotnet build`, all succeeded: CCP.Core, CCP.Avalonia, Tests/CCP.Core.Settings.Tests, Tests/CCP.Core.Content.Tests, Tests/CCP.Avalonia.{Consent,Language,CatalogueStartup,Scheduler,Report,GeneralSettings,Snapshot}.Tests, Tests/CCP.Avalonia.Tests. The WPF test project (and with it the WPF app) also built with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- `bash .github/scripts/core-guards.sh`: "All Core guards passed."
- `dotnet test Tests/CCP.Core.Tests` (DiffEngine_Disabled=true): 1425 passed, 5 skipped, 2 failed. Both failures are outside this diff and come from the container:
  - `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_...`: the sandbox runs as root, which bypasses the read-only folder the test relies on.
  - `LoopbackOAuthTests.GoodState_OnEitherLoopback_...(host: "[::1]")`: the container has no IPv6 loopback ("No data available ([::1])").

  The moved and new classes all pass (54 of 54).
- `dotnet test Tests/CCP.Avalonia.Tests --filter` on BlinkTrainerSession, BillboardShowcase, MiniPlayerPreview, MiniPlayerVideo, MediaHistoryWindow, BlinkTrainerTabLive, BlinkStop, PanicSurfaces and HelpLoopScenes: 53 passed, 1 skipped, 0 failed.
- Compiled but not run: the WPF test classes this unit touched (`MultiMonitorVideoAnchorTests`, `BrowserSinkLabelWiringTests`, `LibVlcSilenceTests`, `StrictMediaKeyLockTests`) and the WPF `App.xaml.cs` seam line. The Linux test host has no Microsoft.WindowsDesktop.App runtime.
