# deeper-core-d: AudioWaveformCache rules into CCP.Core

Base: `avalonia-port/companion-core-b`.

## Already in Core (nothing to move)
- `EnhancementLibrary` (CorePaths/CoreSettings recent files, demo seeding) and `MediaDurationCache`
  were already in `CCP.Core/Services/Deeper` on this base; WPF already routes to them. No second copy exists.

## What changed
- `git mv ConditioningControlPanel/Services/Deeper/AudioWaveformCache.cs -> CCP.Core/Services/Deeper/AudioWaveformCache.cs`.
  Core now owns the DPK2 peaks cache (folder shared with `MediaDurationCache`, key = path + size + last write)
  and the bucket rule, extracted unchanged into `AudioWaveformCache.BucketPeaks(channels, rate, duration, read)`
  (6 peaks a second, clamped 64..4096, loudest |sample| of any channel per bucket).
- Decode seam: `AudioWaveformCache.Decoder` (`Func<string, AudioWaveformResult>`). `App.Logger` -> Serilog `Log`.
- WPF: the NAudio MediaFoundation decode moved into the head's existing decoding file,
  `MediaDurationProbe.DecodeWaveform`, which feeds samples through `BucketPeaks`; `App` registers it next to
  the other Deeper providers at startup. Callers (`EnhancementPlayerWindow`, `DeeperEditorWindow`) are unchanged.
- Tests: `Tests/CCP.Core.Tests/AudioWaveformCacheTests.cs` (bucket count clamp, per-bucket max across channels,
  short-stream tail, flat strip on unknown duration, decode-once then disk cache, re-decode on file change,
  wrong-magic cache ignored, missing file / missing decoder throw). Runs in the CCP_USERDATA_DIR sandbox.
- No trunk code imported. Reference only: trunk `d47e51981` (Avalonia `DeeperWaveform`, LibVLC -> WAV decode)
  shows the same DPK2 format and bucket rule.

## Next units
- deeper-head-waveform: Avalonia waveform for the player/editor (the two `ponytail:` notes) — adapt trunk
  `d47e51981`'s LibVLC->WAV decode as an `AudioWaveformCache.Decoder` for the Avalonia head, reusing the Core cache
  instead of trunk's duplicate `DeeperWaveform` cache.
- deeper-core-e: remaining WPF-independent bits of `ConditioningControlPanel/Services/Deeper`
  (`IPlaybackTimeSource`, `IActionDispatcher`, `SpeakPromptSession` rules, `EnhancementEngine` scheduling) behind seams.

## Verification limits
- `dotnet test Tests/CCP.Core.Tests`: 2185 passed, 6 skipped, 2 failed. The 2 failures
  (`ContentPackStoreTests.Uninstall_WhenTheFolderCannotBeDeleted...` — the container runs as root so the
  "undeletable" folder deletes; `LoopbackOAuthTests...(host: "[::1]")` — no IPv6 loopback) fail identically on the
  untouched base. New `AudioWaveformCacheTests`: 10/10 passed.
- `dotnet test Tests/CCP.Avalonia.Tests --filter Deeper`: 8/8 passed.
- `bash .github/scripts/core-guards.sh`: all Core guards passed.
- Built: CCP.Core, CCP.Avalonia, CCP.LinuxSmoke, CCP.VR, CCP.Core.Settings/Content tests, all CCP.Avalonia.*
  test projects, and `Tests/ConditioningControlPanel.Tests` (pulls in the WPF app) with
  `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`.
- Compiled but not run: the WPF decoder (`MediaDurationProbe.DecodeWaveform`, NAudio MediaFoundation) and its
  startup registration — Windows only.
