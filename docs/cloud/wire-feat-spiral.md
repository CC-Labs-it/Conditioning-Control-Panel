# Spiral randomization slice

- Shared WPF's #641/#1053 image pool and personal/system-folder root refusal through `CCP.Core` (`SpiralPool`, `PersonalFolderRoots`). WPF delegates to these copies; the Avalonia overlay honors the existing randomize switch. Bare Desktop/Pictures/Downloads/profile/drive/OneDrive roots fall back to the explicit Spirals library; dedicated subfolders remain allowed. Image extensions match WPF, and multiple candidates avoid the previous pick.
- Kept a run's random selection across off-thread decode and opacity/monitor repaints. Existing stop, pause, panic and disable paths release the selection for the next start. Explicit file changes refresh live. Randomize-switch changes apply on the next start. No new surface or panic registration is needed.
- Added `SpiralPoolTests` for root refusal, selection, filtering, empty/single pools and no-repeat; `SpiralRandomizeTests` covers actual headless overlay decode/repaint/stop/restart and next-start switch behavior. Tests use the existing `CCP_USERDATA_DIR` sandbox and temporary files.
- Existing spiral headless test builders now register the shipped Inter/font options/collections, matching production while retaining headless dispatch and all assertions. This fixes the observed missing-Segoe-font baseline failure.
- Only `feat-spiral` changes in the ledger; it stays **stub**. No trunk code imported. Read-only `parity/trunk` tip inspected: `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; implementation follows the frozen WPF source on the brief's base.

## Verification limits

- SDK 10.0.402: `/tmp/ccp-dotnet`; `DiffEngine_Disabled=true`. Serial builds use `-m:1 /p:UseSharedCompilation=false` and hardlink copy properties for OutputDirectory, CopyLocal and AdditionalFiles to bound shared-worker storage use.
- `dotnet build CCP.Core/CCP.Core.csproj`: passed, 48 warnings, 0 errors.
- `bash .github/scripts/core-guards.sh`: passed (forbidden API and XAML guards).
- `dotnet test Tests/CCP.Avalonia.Tests --filter "FullyQualifiedName~SpiralRandomizeTests|FullyQualifiedName~SpiralOverlayTests"`: **16 passed, 0 failed, 0 skipped** after faithful font registration. Earlier interrupted-XAML and missing-Segoe-font failures were resolved; no assertions were removed or weakened.
- `dotnet build CCP.Avalonia/CCP.Avalonia.csproj` and every Linux-compatible test csproj in `.github/workflows/build.yml` completed with 0 errors: CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot, Avalonia.Tests, Core.Content, Core.Settings, Core.Tests. WPF cross-build result follows below.
- Initial overlapping invocations were stopped before tests ran. A subsequent focused build failed package copying with `MSB3021` / `No space left on device`; Core and Avalonia compilation completed. Those invocations are not test evidence; complete reruns passed after deduplicating build inputs/outputs.
- Negative proof: restored the original-base `SpiralOverlay.cs` and ran `Randomize_picks_once_per_run_and_the_next_run_avoids_the_previous_image`; it failed (1 failed, 0 passed) at `Assert.NotEqual` because the configured first GIF was returned on both starts. Fixed source was then restored before the final run.
- Dependency setup: repository-pinned Vosk download succeeded at `/tmp/worker1-vosk/vosk-model-small-en-us-0.15`. Rootless Debian package extraction supplies VLC under `/tmp/ccp-vlc`; native `libvlc_new` initialized with dummy audio/video output (3.0.23). System installation was unavailable (uid 1000, no sudo).
- Full `dotnet test Tests/CCP.Core.Tests --no-build`, with `CCP_REQUIRE_VOSK=1` and the pinned model: **2818 passed, 0 failed, 1 skipped, 2819 total**. The existing BoardTiles frame-budget performance fact was skipped by its existing Debug guard (a Release-only benchmark); tests were not weakened or skipped by this change.
- WPF test cross-build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: **passed, 509 warnings, 0 errors** with `DOTNET_GCHeapHardLimit=0x80000000` (2 GiB) on the bounded retry. The initial compiler was killed with exit 137 (`MSB6006`); no source diagnostic was produced. WPF and its test assembly were compiled, not executed.
- `dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker1-spiral-render`: **224/224 views rendered, exit 0**. `SpiralFeatureControl.png` was inspected; the real randomize control, settings and library are visible. No binding errors were observed. Static PNGs do not prove random selection; the behavioral headless tests do.
- Ancillary workflow test assemblies were compiled but not executed; the full Core suite and relevant Avalonia classes were executed.
- Real Windows/WPF execution and compositor/desktop behavior cannot run on this Linux environment. Native platform checks remain bypassed only in existing headless-test seams.

## Follow-up lane: wire-feat-spiral-b

The complete remaining scope exceeds the 1000-added-line cap. Still required: video spiral playback, session-scheduled opacity/ramp integration, Deeper ramp/pulse holds, Loom launch/live-save integration, and the Spiral web-state bridge. Existing feature-card layout/art divergences described in source remain; this slice changes overlay selection behavior without changing card layout.
