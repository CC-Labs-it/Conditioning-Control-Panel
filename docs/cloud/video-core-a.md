# video-core-a: ConditioningControlPanel/Services/Video, Core move unit A

Base: `avalonia-port/feature-day-log-core`. Branch: `avalonia-port/cloud-video-core-a`.

## Inventory (before this unit)

Already in `CCP.Core/Services/Video` (no WPF copy left, nothing to route): `MandatoryVideoScheduler`,
`NoVideosReason`, `VideoMetadataCache`. Every file under `ConditioningControlPanel/Services/Video` was
still WPF-only. `origin/parity/trunk` has not moved any of the files in this unit, so nothing is
imported from trunk here and no trunk commits are credited.

## What changed

- `git mv` `Services/Video/VideoSurfaceHealth.cs` -> `CCP.Core/Services/Video/VideoSurfaceHealth.cs`
  (per-surface multi-monitor rules: frame watchdog, abort, browser failure/sweep, deadline arming,
  surface diagnostic line). Seam: `VideoSurfaceHealth.DiagSink` (`Action<string,string>`) replaces the
  direct `VideoDiag.Log` call; WPF `App` sets it to `VideoDiag.Log` next to
  `BugReportService.DiagnosticTailProvider`. Serilog half unchanged.
- `git mv` `Services/Video/Browser/BrowserSinkLabel.cs` -> `CCP.Core/Services/Video/Browser/`
  (#938 sink-label rule). `App.Settings?.Current` -> `CoreSettings.Current`.
- `git mv` `Services/Video/Browser/BrowserUnsafeVideoCache.cs` -> `CCP.Core/Services/Video/Browser/`
  (browser-unsafe clip list, already on `CorePaths.UserData`). `App.Logger` -> Serilog `Log`
  (WPF sets `Log.Logger = App.Logger`, so the same sink).
- Namespaces are unchanged, so WPF callers (`VideoService`, `VideoService.Browser`,
  `BrowserVideoEngine`, `BrowserVideoGate`, `BubbleCountService`, `BubbleCountWindow`,
  `ChaosWebViewHost`) compile as before through `InternalsVisibleTo("ConditioningControlPanel")`.
- No embedded resources moved, so no core-guards baseline entry.
- Tests:
  - `git mv` `Tests/ConditioningControlPanel.Tests/MultiMonitorVideoSurfaceTests.cs` ->
    `Tests/CCP.Core.Tests/`. The `OverlayService.ResolveVideoAnchor` cases (WPF) moved out to the new
    `Tests/ConditioningControlPanel.Tests/OverlayVideoAnchorTests.cs`, unchanged.
  - New `Report_HandsTheSurfaceLineToTheHeadsVideoTrace` covers the `DiagSink` seam (and that a
    throwing or missing sink never throws).
  - `BrowserSinkLabelTests`: the pure resolver cases moved to `Tests/CCP.Core.Tests/BrowserSinkLabelTests.cs`,
    plus `Resolve_Live_ReadsTheCoreSettingsSeam`. The WPF file keeps only the source-scan wiring tests
    (player.js, engine, BubbleCountWindow, surface, gate).
  - New `Tests/CCP.Core.Tests/BrowserUnsafeVideoCacheTests.cs` (size-keyed entries, stable pack key,
    blank input), in the assembly's `CCP_USERDATA_DIR` sandbox.
  - `StrictMediaKeyLockTests` stays in the WPF suite (most of it scans WPF sources); its
    `VideoSurfaceHealth` calls now resolve to Core.

## Next units (not in this branch)

- **video-core-b**: `VideoDiag`. Split the portable writer (redacted file sink, tail, `UiScope`) into
  Core and leave the WPF `Dispatcher` heartbeat and hooks in the head. Then the `DiagSink` seam can go away.
- **video-core-c**: `BrowserVideoGate`. Needs an `IsAvailable`/`BuildPageUrl` provider seam for the
  WebView2 engine. The rule itself (extension list, #938 device demotion, unsafe-cache check) is pure.
- **video-core-d**: pure rules inside `VideoService.cs` (9k lines): clip guards, grace pause, verdict
  replay, extension filter (see `VideoExtensionFilterTests`, `VideoGracePauseTests`, `VideoRunGuardTests`,
  `VideoTriggerGuardTests`, `VideoFeedDeferNoticeTests`). Check first what the Avalonia
  `MandatoryVideoOverlay` already duplicates and route both heads to one copy.
- **video-core-e**: `AttentionTargets.AttentionSpawnExpiry` (pure) apart from the WPF visuals.
- Stays WPF (OS/WebView2/LibVLC/WPF windows): `BrowserVideoEngine`, `BrowserVideoSurface`,
  `BrowserVideoWindow`, `DualMonitorVideoService`, `ScreenMirrorService`, `VideoHostBlackFill`,
  `LibVlcSilence`, `WallpaperService`, `VideoService.Browser`.

## Verification limits

- `dotnet build` succeeded (Linux, .NET SDK 10.0.112): CCP.Core, CCP.Avalonia, CCP.Core.Tests,
  CCP.Core.Settings.Tests, CCP.Core.Content.Tests, CCP.Avalonia.Tests and every CCP.Avalonia.*.Tests
  project in build.yml.
- WPF head + `Tests/ConditioningControlPanel.Tests` COMPILE with `-p:EnableWindowsTargeting=true
  -p:ValidateExecutableReferencesMatchSelfContained=false`. They were NOT run: the
  Microsoft.WindowsDesktop.App runtime does not exist on Linux (test host aborts). So
  `OverlayVideoAnchorTests`, the trimmed WPF `BrowserSinkLabelTests` and `StrictMediaKeyLockTests` are
  compiled but not run, and the WPF `App` seam line (`VideoSurfaceHealth.DiagSink = VideoDiag.Log`) is not
  exercised at runtime.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1189 passed, 5 skipped, 2 failed. Both failures are from this
  container and not this diff: `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")` (the
  container has no IPv6) and `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (the tests
  run as root, so the folder can always be deleted). Neither touches the moved files.
- Moved and new Core tests (`MultiMonitorVideoSurfaceTests`, `BrowserSinkLabelTests`,
  `BrowserUnsafeVideoCacheTests`): 52/52 passed.
- `Tests/CCP.Avalonia.Tests` `VideoTargetMonitorTests|MandatoryVideoOverlayTests`: 8 passed, 1 skipped
  (no Avalonia code changed in this unit).
