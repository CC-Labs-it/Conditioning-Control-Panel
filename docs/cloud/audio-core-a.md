# refactor(core): move the audio-sync download + chunk pipeline to CCP.Core (audio-core-a)

Unit A of the `ConditioningControlPanel/Services/Audio` Core move. No Avalonia UI in this unit.

## Inventory of `ConditioningControlPanel/Services/Audio` (before this unit)

| File | State |
|------|-------|
| `AudioAnalyzer.cs` | already in Core (`CCP.Core/Services/Audio`, moved in 640b0b883, on our base) |
| `VideoDownloader.cs` | pure: **moved here** |
| `ChunkManager.cs` | pure except the MediaFoundation decode: **moved here**, decode left in WPF |
| `LayeredAudioService.cs` | NAudio mixer + `App.Settings`/`App.Audio`; next unit |
| `AudioService.Playback.cs` | NAudio WaveOut + MMDevice watcher; next unit |
| `WaveOutTeardown.cs` | NAudio WinMM teardown; stays in WPF (Windows-only by nature) |

## What changed

- `git mv` `ConditioningControlPanel/Services/Audio/VideoDownloader.cs` -> `CCP.Core/Services/Audio/VideoDownloader.cs`
  (with `DownloadProgressEventArgs`, `AudioSyncException`). Only edit: `App.GetMediaTempPath()` -> `CorePaths.MediaTemp()`.
- `git mv` `ConditioningControlPanel/Services/Audio/ChunkManager.cs` -> `CCP.Core/Services/Audio/ChunkManager.cs`
  (with `ChunkState`, `AudioChunk`, `ChunkProgressEventArgs`). `App.Logger` -> Serilog `Log` (WPF sets
  `Log.Logger = App.Logger`, so the same sink). The Windows-only decode is now a constructor argument
  `Func<string, TimeSpan, TimeSpan, CancellationToken, float[]>`, run on `Task.Run` exactly as before.
- New `ConditioningControlPanel/Services/Audio/MediaFoundationRangeReader.cs`: the old
  `ExtractAudioRangeAsync` body verbatim (NAudio `MediaFoundationReader`, mono mix, same logs, empty on
  any failure). `AudioSyncService` passes `MediaFoundationRangeReader.Read` to `ChunkManager`.
- `CorePaths.MediaTemp()`: the `App.GetMediaTempPath` rule (assets/.temp, else system temp) moved to Core;
  WPF `App.GetMediaTempPath()` now delegates to it (one copy; the WPF seam
  `CorePaths.EffectiveAssetsProvider` was already set in `App`'s static ctor).
- `Tests/CCP.Core.Tests/AudioChunkPipelineTests.cs` (new, 12 cases): MediaTemp rule + fallback, URL
  heuristics, progress percent, loopback download into MediaTemp, chunk planning, first/seek chunk
  decode ranges with a single download reused, cached video deleted on Dispose, empty decode leaves the
  chunk not ready. There were no pure WPF tests of these files to move.
- No embedded resources moved, so no core-guards baseline entry.
- Trunk credit: none imported. `origin/parity/trunk` has no commits on these files beyond 640b0b883,
  which is already on our base.

## Next units

- **audio-core-b**: `LayeredAudioService` rules (track filtering/missing-file skip, per-track volume,
  loop) vs `CCP.Avalonia/Platform/LayeredAudio.cs`: route both heads to one Core rule, NAudio mixer stays in WPF.
- **audio-core-c**: `AudioService.Playback` failure-streak / output-cooldown state machine to Core; WaveOut,
  MMDevice endpoint watcher and `WaveOutTeardown` stay in WPF.
- **audio-core-d**: `Services/AudioSyncService.cs` orchestration to Core (needs a dispatch seam instead of
  `System.Windows`), plus an Avalonia decoder (LibVLC) for `ChunkManager` so audio-synced haptics run on Linux.

## Verification limits

Run on Linux with the .NET SDK 10.0.112 from Ubuntu's archive (dot.net's install host is blocked by this
environment's proxy):

- `dotnet test Tests/CCP.Core.Tests`: 1170 passed, 5 skipped, 2 failed. Both failures also fail on the
  unchanged base `origin/avalonia-port/first-show` and are caused by this container:
  `LoopbackOAuthTests.GoodState_OnEitherLoopback...(host: "[::1]")` (no IPv6 loopback) and
  `ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` (runs as root, so the folder can be deleted).
  The new `AudioChunkPipelineTests` pass 12/12.
- `dotnet test Tests/CCP.Avalonia.Tests --filter "LayeredAudio|Haptics"`: 6 passed, 1 skipped.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- `dotnet build` succeeded for CCP.Core, CCP.Avalonia and the Linux test projects in `.github/workflows/build.yml`
  (CCP.Core.Settings/Content.Tests, CCP.Avalonia.Tests and its Consent/Language/CatalogueStartup/
  Scheduler/Report/GeneralSettings/Snapshot siblings). `Tests/ConditioningControlPanel.Tests` (and so the
  WPF head it references) also builds with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- Compiled but not run: `MediaFoundationRangeReader` and the `AudioSyncService` wiring (Windows-only
  MediaFoundation). WPF tests were not run: they need Windows.
