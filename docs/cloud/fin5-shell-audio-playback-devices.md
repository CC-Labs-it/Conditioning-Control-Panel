- Added Windows active playback endpoint enumeration using WPF's NAudio.Wasapi 2.2.1 dependency; reused existing settings/dashboard selection and live-player routing.
- Added a Windows WASAPI seam behind LibVlcAudio's serialized duck lifecycle. Sweeps all active render endpoints, disposes native wrappers, excludes own/system/WebView processes, preserves original gain, handles silent late streams, and restores by PID or restarted process name. Focused tests use fake sessions without desktop/profile access.
- Corrected stale ledger no-caller claims: base 43827ca298e9c35e5557d04d5581d961d6f666ec already has PopQuizHost, BubbleCountHost, GradedIntakeTabView, MainShellWindow.SessionRun, ChaosRunHost, and AvatarTube chat/bark paths. No new window/surface; existing panic and force-unduck paths are reused.
- WPF references reviewed: AudioService.cs endpoint enumeration, ApplyDuckSweep, RescanSweep and RestoreDuckedVolumes; Audio/LayeredAudioService.cs layer duck/restore; MainWindow.HomeAudio.cs canonical-control mirroring. Read-only parity/trunk fetched at a4ee52d40cd347d288dcf7fdaa8445b79f8e5038; no trunk code imported.

- Added Windows crash persistence/startup recovery and five-second deferred restore retries for three minutes, with tests. Recovery stores only PID/gain values in the profile and skips own/system sessions.

## Remaining gaps

- Browser-video playback/session hosting is unported: the mandatory-video IsBrowserSessionActive OR has no equivalent in the LibVLC-only video host. Setting-based browser exclusion works on both audio backends. Browser video requires the alternate video-engine host, lifecycle and mandatory playback integration; that is a separate feature too large for the remaining portion of this 1000-line unit. The row remains **stub** with that explicit dependency.

## Assumptions

- Keep localized System default and existing LibVLC routing; enumerate active Windows render endpoints in native order, as WPF does.
- Keep stub status until the browser video dependency is ported.

## Verification limits

- SDK: .NET 10.0.402.
- `dotnet build <project> -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: Core, Avalonia and all 11 Linux-compatible workflow test projects succeeded with zero errors (Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, CatalogueStartup, Consent, GeneralSettings, Language, Report, Scheduler, Snapshot).
- WPF test cross-build with the same flags: exit 0, 512 warnings, 0 errors, 5m08s. Compiled only; never run on Linux.
- `bash .github/scripts/core-guards.sh`: exit 0, All Core guards passed. Test-plan coverage script is absent on this base. `git diff --check`: exit 0.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Core.Tests -c Release`: exit 1; 2815 passed, 5 Vosk-model skips, 1 board-render performance-budget failure (17.40 ms under concurrent builds). Isolated rerun with `--no-build --filter 'FullyQualifiedName~AudioDuckRescanRulesTests|FullyQualifiedName~BoardTilesTests.A_frame_fits_the_budget'`: exit 0, all 6 passed.
- `DiffEngine_Disabled=true dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter 'FullyQualifiedName~WindowsAudioTests|FullyQualifiedName~CoreAudioTests|FullyQualifiedName~AudioDuckRescanTests|FullyQualifiedName~AudioSettingsTests'`: exit 1, 16 passed (including all 8 new WindowsAudioTests), 2 AudioSettingsTests failed at shell construction because Segoe UI Black glyphTypeface is unavailable. These two UI behaviors remain unvalidated here; no tests were weakened or skipped.
- Separate `dotnet test Tests/CCP.Avalonia.Tests -c Release --no-build --filter FullyQualifiedName~WindowsAudioTests` with `DiffEngine_Disabled=true`: exit 0, 8 passed, 0 failed, 0 skipped.
- Windows native code compiled on Linux but not executed. No physical sink, browser sink or gaplessness measurement. New tests use fake sessions and sandboxed temporary recovery files; no new surface requires panic registration.
