# fin5 — shell file dialogs

- Base: `4617df7458550045ccec381a0f9d9fe43c0aa07d`; local snapshot initially differed and was reset to the required commit after fetching the base.
- Adapt WPF `MainWindow.Assets.cs:842-951` pack-thumbnail LRU: 50 entries, 50 MiB estimated BGRA storage, hits promote recency, failures retry. Reuse the existing four-load gate, image stream decoder, Windows video thumbnailer and temp-file cleanup. Cached bitmaps remain valid for visible views after eviction.
- Isolate caches by content-store instance, with weak ownership; retain WPF's `packId:obfuscatedName` keys within each store. An oversized individual image is displayed without retaining it.
- Add folder-revisit decrypt-count/bitmap-identity regression and focused cache eviction, promotion, failure retry and ownership tests. No new surface; existing `asset-preview` panic registration and test remain unchanged.
- No code imported from trunk. Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; WPF on the pinned base is the implementation source.

## WPF comparison and remaining gaps

The single `shell-file-dialogs` row remains **stub**. Its cited WPF files were inspected alongside the head paths. Existing paths cover session create/edit/export and preset export, file/folder/media choices, pack browsing/selection/preview, enhancement schema filtering, deduplication, copy, remembered folder, notifications and delayed reveal. `MonitorTargetPicker` is monitor selection/live-surface refresh rather than a native file picker; `DeeperHub` provides filtering/selection rather than the playback backend.

Gaps at the start of this unit:

1. Pack thumbnail LRU (`MainWindow.Assets.cs:842-951`) — implemented here.
2. Deeper native audio decode/play/pause/resume/replay/seek/volume, waveform peaks/scrubbing, playback-time publishing and rule-engine binding — remain missing. WPF requires `EnhancementAudioPlayer` (208 lines), `AudioWaveformCache` (158), `EnhancementHostService` (309) and `EnhancementEngine` (1,261), plus time-source/action implementations and editor/player integration. Core currently has enhancement models, serializer, library, validation and import helpers, but no equivalent engine/audio/host pipeline. The engine alone exceeds this unit's 1,000-added-line cap; importing only the host would leave it uncallable. WPF host binding also connects webcam/action services. These are live WPF behaviours, so P122 does not apply.
3. Browser video page zoom and fullscreen bridge/reparenting — remain missing in Deeper editor/player. `NativeWebView` lacks WPF WebView2's zoom factor, document-created script hook and fullscreen-element signal. Existing script evaluation is insufficient to reproduce page zoom across navigation and fullscreen; substituting CSS zoom would change behaviour. Requires browser backend capabilities and associated fullscreen host.
4. Foreground-aware owner reactivation on non-Windows — remains missing. WPF reads the foreground process; the existing Windows seam does that. Other desktops have no implemented portable equivalent (Wayland focus restrictions also preclude a universal process query). Preserve the current conservative fallback.

The remaining dependencies cannot fit with complete behaviour/tests inside the cap. This is a partial implementation, not a dependency-only NOOP. Hidden pack-card administration remains under `views-tab-assets`.

## Tester steps

1. WPF `ConditioningControlPanel/MainWindow/MainWindow.Assets.cs:842-951` Rows: `shell-file-dialogs` — Open Library, visit an installed pack's image folder, switch away, then revisit. Images and check states remain correct; the revisit reuses decoded thumbnails without decrypting again. Visit more than 50 distinct pack files; revisit the least recently used file to reload it. Successful Windows video thumbnails also reuse the cache; failed thumbnails retry on the next visit. Without a video thumbnailer, pack clips remain placeholders without temp extraction.

## Verification limits

Checks use .NET 10 (10.0.100 bootstrap; channel installation ultimately succeeded with 10.0.402 for final checks) and `DiffEngine_Disabled=true`:

- `dotnet build CCP.Core/CCP.Core.csproj -c Release`: exit 0, zero errors.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj -c Release`: exit 0, zero warnings/errors on final incremental build.
- Workflow Linux test projects built with `dotnet build <project> -c Release`: Core, Core.Settings, Core.Content; Avalonia, Avalonia.CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot. All exit 0, zero errors; existing warnings remain on initial builds.
- `bash .github/scripts/core-guards.sh`: exit 0, all guards passed (3 baselined Core APIs; 424 XAML references checked).
- `dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 2,809 passed, 2 failed, 5 skipped (2,816 total). Existing `SyncedEventAndScheduledFireBothHopThroughCoreDispatch` and `A_frame_fits_the_budget` failed under concurrent build load (null scheduling handle and 17.70 ms frame respectively). Isolated `--no-build --filter` retry: exit 0, both passed. The suite's five Vosk cases skipped because the model is unavailable; no tests were changed or disabled.
- Final focused `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~PackThumbnailCacheTests|FullyQualifiedName~FileDialogPlatformTests'`: exit 0, 8 passed, zero failed/skipped.
- Pre-fix `AssetsTabTests.InstalledPacksHaveTypedFoldersCountsSelectionsAndPreviews` probe: exit 1 on font creation in `PremiumSpark.BuildLabel`, before assertions. Therefore no red/green behavioural claim for the folder-revisit integration regression. The shell suite was compiled, not rerun after the known font failure; GitHub must execute it.
- WPF `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: exit 0, zero errors; Windows reference head/tests compiled, not run on Linux.
- Test-plan coverage script is absent on this base. `git diff --check` passes.

 Native GTK/portal file pickers, Windows COM thumbnail extraction, desktop focus behaviour and media playback require desktop testing; no Windows code was executed on Linux. No new Windows code is added.

## Assumptions

- Prioritize the self-contained thumbnail gap and retain honest stub status for the larger playback/browser/platform dependencies.
- Store-scoped cache ownership prevents decrypted content crossing independent store/profile lifetimes; oversized images should not violate the retained-byte cap.
- No existing test-plan file or coverage script exists on this base; add a numbered row-linked tester step in the new plan and here.
