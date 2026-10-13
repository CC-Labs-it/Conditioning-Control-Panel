# refactor(core): move Deeper playback/dispatch contracts into CCP.Core (deeper-core-b)

Already in `CCP.Core/Services/Deeper` before this unit: DeeperConfig, DeeperLocalLibrary,
EnhancementAutoTagger, EnhancementCapabilities, EnhancementFetcher, EnhancementImportRules,
EnhancementLibraryEntry, EnhancementLibraryFilter, EnhancementMediaBundler, EnhancementResolver,
EnhancementSerializer, EnhancementValidator, HtMetadataFetcher, MandatoryVideoEnhancementScanner,
UrlSafety.

This unit moves the next coherent group, the contracts the engine talks through:

- `IPlaybackTimeSource.cs` is a `git mv` to `CCP.Core/Services/Deeper`. `System.Windows.Rect` becomes
  the Core `PlaybackRect` record struct (keeps WPF's semantics: only negative extents are `Empty`,
  a 0x0 laid-out rect is not). Adapted from trunk `0f7262df2` (its `EnhancementSeams.PlaybackRect`),
  kept next to the interface that returns it instead of a separate seams file.
- `IActionDispatcher.cs` is split: `EnhancementDispatchContext`, `IActionDispatcher`,
  `LoggingActionDispatcher` and `RecordingActionDispatcher` go to `CCP.Core/Services/Deeper/IActionDispatcher.cs`
  (`App.Logger` replaced with Serilog `Log`). `RealActionDispatcher` (App.Audio/Overlay/Haptics/...) stays in WPF
  and the old file is renamed to `ConditioningControlPanel/Services/Deeper/RealActionDispatcher.cs`
  (shows as a rename in `git diff -M`). One copy of each rule; WPF uses the Core types directly.
- WPF time sources (`VideoServiceTimeSource`, `BrowserVideoTimeSource`, `EnhancementAudioPlayerTimeSource`)
  return `PlaybackRect`; `VideoServiceTimeSource.FitContain` keeps working in WPF `Rect` and converts at the
  boundary. `EnhancementEngine.HitsRect` takes `PlaybackRect` (same X/Y/Width/Height arithmetic).
- New `Tests/CCP.Core.Tests/DeeperDispatchContractTests.cs` (5 tests): PlaybackRect emptiness, dry-run line
  format + event, ramp-update and cancelled dispatches dropped, 50-entry cap, recording decorator forwards
  updates unrecorded and records even when the inner dispatcher throws. These types did not exist in Core
  on the base, so the tests cannot compile there.
- No seams, embedded resources or core-guards baseline entries were needed.

## Next units (not moved here)

- deeper-core-c: `EnhancementEngine` (needs the `IEnhancementWebcam` / `IEnhancementRunCleanup` seams and a
  `PlaybackPoint`, trunk `0f7262df2`), then `EnhancementHostService`.
- deeper-core-d: `EnhancementLibrary` (App.UserDataPath, App.Settings recent files/demo seeding, Dispatcher
  marshalling -> CorePaths/CoreSettings/provider Funcs), `MediaDurationCache` and `AudioWaveformCache`
  (LibVLC / NAudio behind seams).
- Stays in WPF (head-specific): `RealActionDispatcher`, `SpeakPromptSession`, `EnhancementAudioPlayer`,
  `BrowserAutoDiscovery`, `BrowserEnhancementBridge`, `VideoEnhancementBridge`, and the three time sources.

## Verification limits

- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests -c Release`: 1376 passed, 5 skipped, 2 failed. The 2 failures
  (`LoopbackOAuthTests.GoodState_OnEitherLoopback...("[::1]")`, no IPv6 loopback in the container; and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...`, the container runs as root) fail the
  same way on the unmodified base `origin/avalonia-port/game-pbp`.
- `--filter DeeperDispatchContractTests`: 5/5 passed.
- `Tests/CCP.Avalonia.Tests --filter Deeper`: 8/8 passed (no Avalonia code changed).
- Built with 0 errors: CCP.Core, CCP.Avalonia, CCP.Core.Settings/Content.Tests, all CCP.Avalonia.*.Tests,
  and on Linux with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`
  the WPF head `ConditioningControlPanel.csproj` and `Tests/ConditioningControlPanel.Tests`.
- Compiled but not run: the WPF head and its tests (Windows-only runtime); the WPF time-source changes
  (WebView2 / LibVLC window rect) are untested at runtime.
