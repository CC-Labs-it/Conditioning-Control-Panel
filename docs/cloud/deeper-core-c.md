# deeper-core-c: EnhancementEngine + EnhancementHostService move to CCP.Core

Base: `avalonia-port/emi-line-engine`. Imports adapted from parity/trunk `0f7262df2`
(feat(parity deeper): rule engine fires in the player…) — Core half only; the Avalonia
player/dispatcher half of that commit is a later unit.

Already in Core for this area before this unit (earlier units): DeeperConfig, DeeperLocalLibrary,
EnhancementAutoTagger/Capabilities/Fetcher/ImportRules/LibraryEntry/LibraryFilter/MediaBundler/
Resolver/Serializer/Validator, HtMetadataFetcher, MandatoryVideoEnhancementScanner, UrlSafety.
None of them had a second copy of what moves here.

## What changed
- `git mv` to `CCP.Core/Services/Deeper/` (show as renames under `git diff -M`):
  - `EnhancementEngine.cs` (R095): `App.Logger` → Serilog `Log`; `App.Haptics` → `CoreHaptics.Service`;
    WPF `Application.Current.Dispatcher` marshal → `CoreDispatch.Post`; `System.Windows.Point/Rect`
    → `PlaybackPoint/PlaybackRect`; `WebcamTrackingService` → `IEnhancementWebcam`;
    `App.Overlay/Flash/Subliminal` Stop-time cleanup → `IEnhancementRunCleanup` on the dispatcher (trunk 0f7262df2).
    The "webcam rules but no tracker" diagnostic no longer names `App.Webcam` (core-guards).
  - `EnhancementHostService.cs` (R088): `DispatcherFactory` / `WebcamProvider` seams; unseeded =
    dry-run `LoggingActionDispatcher`, no webcam (trunk 0f7262df2).
  - `IPlaybackTimeSource.cs` (R097): `GetVideoRect()` returns `PlaybackRect`.
- `IActionDispatcher.cs` split: context, `IActionDispatcher`, `LoggingActionDispatcher`,
  `RecordingActionDispatcher` go to Core (trunk 0f7262df2); the WPF file is renamed (R074) to
  `RealActionDispatcher.cs` and keeps only the production dispatcher.
- New `CCP.Core/Services/Deeper/EnhancementSeams.cs` (trunk 0f7262df2): `PlaybackRect`, `PlaybackPoint`,
  `IEnhancementWebcam`, `IEnhancementRunCleanup`.
- **Fix over trunk:** `RecordingActionDispatcher` forwards `IEnhancementRunCleanup` to its inner
  dispatcher. The host always wraps the real dispatcher in the recorder, so with trunk's version the
  engine's `_dispatcher as IEnhancementRunCleanup` never matched and Stop never reset overlay bands or
  cancelled one-shot flashes/subliminals. Test proves it (fails with trunk's file, passes now).
- WPF head keeps the same behaviour:
  - `RealActionDispatcher : IEnhancementRunCleanup` → `App.Overlay.ResetDeeperOverlayBands`,
    `App.Flash.StopOneShotFlashes`, `App.Subliminal.StopOneShotSubliminals` (what the engine called before).
  - `EnhancementWebcamAdapter` (new, 26 lines) adapts `WebcamTrackingService` (gaze `Point` → x,y);
    App caches one per tracker so the gaze forwarder is added once.
  - `App` ctor seeds `EnhancementHostService.DispatcherFactory = new RealActionDispatcher()` and
    `WebcamProvider` (lazy, like the other Core providers).
  - `VideoServiceTimeSource`, `BrowserVideoTimeSource`, `EnhancementAudioPlayerTimeSource` return
    `PlaybackRect` (WPF `Rect.Empty` → `PlaybackRect.Empty`).
- Tests: `Tests/CCP.Core.Tests/EnhancementEngineTests.cs` (6): time_reached fires once; blink through the
  webcam seam fires and Stop unsubscribes; Stop cleanup through the recorder (with and without one-shots);
  host Bind uses the head's dispatcher + webcam; unseeded host dry-runs and reports no tracker.
  There were no existing WPF tests of these files to move.

Deliberate differences (WPF): a dry-run editor preview (`LoggingActionDispatcher`) no longer pokes the
live overlay/flash/subliminal services on Stop — it never showed anything. `CoreDispatch.Post` runs a
webcam event in place when the WPF dispatcher has started shutdown, where the old code dropped it;
the engine's `_running` guard and try/catch cover that window.

Added lines vs base: 490 (+ this doc).

## Next units
- deeper-core-d: Avalonia head wiring from trunk 0f7262df2 — `RealActionDispatcher` (Avalonia overlays),
  `EnhancementPlayerWindow.Engine.cs` binding a time source, panic surface `deeper-player`.
- deeper-core-e: remaining pure pieces of `ConditioningControlPanel/Services/Deeper`
  (`SpeakPromptSession` rules, `MediaDurationCache`/`AudioWaveformCache` if WPF-free, `EnhancementLibrary`).
- Time sources / bridges (`VideoServiceTimeSource`, `BrowserVideoTimeSource`, `*EnhancementBridge`,
  `BrowserAutoDiscovery`) stay per-head (they wrap LibVLC/WebView2 windows).

## Verification limits
- `dotnet build`: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, Tests/CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.{Tests,Consent,Language,CatalogueStartup,
  Scheduler,Report,GeneralSettings,Snapshot}.Tests — all succeeded.
- `Tests/ConditioningControlPanel.Tests` (and so the WPF head) **builds** on Linux with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`, but
  cannot **run** here (no Microsoft.WindowsDesktop.App runtime). WPF behaviour (adapter, App seeding,
  RealActionDispatcher cleanup, time-source rects) is compiled, not run.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1432 passed, 5 skipped, 2 failed —
  `LoopbackOAuthTests…(host: "[::1]")` and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted…`,
  both fail identically on the base branch in this container (no IPv6 loopback; runs as root).
  New `EnhancementEngineTests`: 6/6 pass; with trunk's `IActionDispatcher.cs` the two Stop-cleanup tests fail.
- `dotnet test Tests/CCP.Avalonia.Tests --filter DeeperHubTests|CoreStandInTests|DeeperRowsTests|PanicSurfacesTests|CatalogueShellTests`: 24/24 passed.
