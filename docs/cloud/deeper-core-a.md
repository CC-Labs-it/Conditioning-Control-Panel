# refactor(core): move EnhancementLibrary and MediaDurationCache to CCP.Core (deeper-core-a)

Base: `avalonia-port/feature-day-log-core`. Unit A of moving `ConditioningControlPanel/Services/Deeper` into `CCP.Core/Services/Deeper`.

## Inventory of `ConditioningControlPanel/Services/Deeper` (before this unit)

Already in Core before this unit (not touched): `DeeperConfig`, `DeeperLocalLibrary`, `EnhancementAutoTagger`, `EnhancementCapabilities`, `EnhancementFetcher`, `EnhancementImportRules`, `EnhancementLibraryEntry`, `EnhancementLibraryFilter`, `EnhancementMediaBundler`, `EnhancementResolver`, `EnhancementSerializer`, `EnhancementValidator`, `HtMetadataFetcher`, `MandatoryVideoEnhancementScanner`, `UrlSafety`.

None of the 15 files still in the WPF folder had a Core copy, so nothing needed routing. Classified:

| File | Head-only because | Unit |
|---|---|---|
| `EnhancementLibrary` | App.UserDataPath/Settings/Logger, WPF DispatcherTimer | **moved here** |
| `MediaDurationCache` | AudioWaveformCache.CacheFolder, App.Logger; probes use NAudio + LibVLC | **store/format/key moved here**, probes stay in WPF |
| `IPlaybackTimeSource` | `System.Windows.Rect` in `GetVideoRect` | next |
| `EnhancementHostService` | App.Webcam, Dispatcher | next |
| `IActionDispatcher` | App.Audio/Bubbles/Flash/Overlay/…, DispatcherTimer | later (needs several seams) |
| `EnhancementEngine` (1261 lines) | App.Flash/Haptics/Overlay/Subliminal/Webcam, Dispatcher, `Point` | later, after the two above |
| `SpeakPromptSession` | App.Autonomy/AvatarWindow/Speech, Dispatcher | later |
| `AudioWaveformCache`, `EnhancementAudioPlayer`, `EnhancementAudioPlayerTimeSource` | NAudio decode/playback | stays head-side (needs a decoder seam) |
| `BrowserAutoDiscovery`, `BrowserEnhancementBridge`, `BrowserVideoTimeSource`, `VideoEnhancementBridge`, `VideoServiceTimeSource` | WebView2 / LibVLC players | stay head-side |

## What changed

- `git mv` `ConditioningControlPanel/Services/Deeper/EnhancementLibrary.cs` -> `CCP.Core/Services/Deeper/EnhancementLibrary.cs`. Seams: `CorePaths.UserData` (library folder), `CoreSettings.Current`/`Save` (recent files, last directory, demo-seeded flag), Serilog `Log`. The 300 ms watcher debounce was a WPF `DispatcherTimer`; it is now a `System.Threading.Timer` whose tick raises `LibraryChanged` through `CoreDispatch.Post`, so WPF subscribers still get it on the UI thread (WPF already sets `CoreDispatch.PostProvider` in `App` startup, before the library is built). Same namespace, so no WPF call site changed.
- `git mv` `ConditioningControlPanel/Services/Deeper/MediaDurationCache.cs` -> `CCP.Core/Services/Deeper/MediaDurationCache.cs`, keeping `Format`, `IsProbeable`, `TryGetCached`, `Remember`, `KeyFor`, `ParseStore` and the JSON store. It now owns `CacheFolder` (`<user data>/deeper-cache`); WPF `AudioWaveformCache.CacheFolder` reads it, so the folder name exists once.
- New `ConditioningControlPanel/Services/Deeper/MediaDurationProbe.cs`: the MediaFoundation (NAudio) + shared-LibVLC `ProbeAsync`, moved verbatim out of the cache. `MainWindow.DeeperHub.cs` calls `MediaDurationProbe.ProbeAsync`.
- Avalonia `EnhancementPlayerWindow.FormatTime` was a hand copy of `MediaDurationCache.Format`; it now calls the Core rule, as the WPF player already did (also covers Infinity, which the copy did not).
- Comments in the Avalonia head that said `EnhancementLibrary` was WPF-only now point at the Core file.
- Tests: `git mv` `Tests/ConditioningControlPanel.Tests/DeeperLibraryDurationTests.cs` -> `Tests/CCP.Core.Tests/` (namespace only). New `Tests/CCP.Core.Tests/EnhancementLibraryCoreTests.cs` pins the seams: folder under `CorePaths.UserData`, `Save` writes the recent list and last directory through `CoreSettings`, `PromoteToLibrary` dedupes with ` (2)`, and a burst of three file writes raises `LibraryChanged` exactly once, through `CoreDispatch`.
- No embedded resources moved (demo seeding reads `Resources/DeeperDemos` from `AppContext.BaseDirectory`, which the head ships), so no core-guards baseline entry.
- Trunk: `parity/trunk` still keeps both files in the WPF head, so nothing was imported from it and there is no trunk commit to credit.

## Next units

- **deeper-core-b (Avalonia wiring):** one app-level `EnhancementLibrary` instance on the Avalonia head; delete the inlined file-ops region in `DeeperEditorWindow.axaml.cs` (FileSuffix, LibraryFolder, LastDirectory, SuggestedFileName, Save/TouchRecent) and the inlined `CreateBlank` in `MainShellWindow.DeeperTab.cs`; enable import (`FindDuplicateOf`/`PromoteToLibrary`) and the `LibraryChanged` refresh.
- **deeper-core-c:** `IPlaybackTimeSource` (replace `System.Windows.Rect` with a Core rect) and `EnhancementHostService` (webcam seam).
- **deeper-core-d:** `IActionDispatcher` behind provider seams for audio/bubbles/flash/overlay/haptics/subliminal.
- **deeper-core-e:** `EnhancementEngine`, then `SpeakPromptSession`.
- Head-side, not Core: the NAudio waveform/audio player and the WebView2/LibVLC time sources and bridges.

## Verification limits

- `dotnet build` (Linux, .NET SDK 10.0.112 from the Ubuntu archive; dot.net was blocked by the egress policy): CCP.Core, CCP.Avalonia, CCP.Avalonia.Tests, CCP.Avalonia.{CatalogueStartup,Consent,GeneralSettings,Language,Report,Scheduler,Snapshot}.Tests, CCP.Core.Content.Tests, CCP.Core.Settings.Tests: all succeeded. `Tests/ConditioningControlPanel.Tests` (which builds the WPF app) with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: succeeded.
- `bash .github/scripts/core-guards.sh`: All Core guards passed.
- `dotnet test Tests/CCP.Core.Tests`: 1170 passed, 5 skipped, 2 failed. Both failures (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted_KeepsThePackInstalledButInactive`, `LoopbackOAuthTests.GoodState_OnEitherLoopback_ServesSuccessPage_AndReturnsCode(host: "[::1]")`) fail identically on the base branch in this container (runs as root, so permissions can't block a delete; no IPv6 loopback). The moved and new tests (33) all pass.
- `dotnet test Tests/CCP.Avalonia.Tests --filter DeeperHubTests|DeeperRowsTests|CatalogueShellTests|CoreStandInTests|DeeperEditor*|EnhancementPlayer*`: 19 passed.
- Compiled but not run: the WPF head (Windows only), including `MediaDurationProbe` (MediaFoundation/LibVLC), the WPF UI-thread delivery of `LibraryChanged` through the WPF `CoreDispatch.PostProvider`, and the WPF test project.
