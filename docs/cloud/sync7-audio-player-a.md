# sync7-audio-player-a

- Port WPF `aeb8467ba` and `6a1ff2326`: move the unchanged playlist contract, loop enum and pure order/shuffle/skip rules into CCP.Core. WPF calls that same copy.
- Add an independent Avalonia LibVLC playlist transport and service, initialized by App, with master-scaled volume, local list/level/loop/shuffle settings, pause/resume/seek, bounded missing-file skips and natural-end progression.
- Panic/watchdog immediately mute and queue the stop; panic, ordinary engine Stop and application exit all stop the independent player without clearing its playlist. Register the new panic surface and keep its safety test.
- Expose the service on the Deeper page through file selection and transport/list/seek/volume/loop/shuffle controls. Preserve existing ledger lanes for the complete WPF card presentation and enhancement bridge.
- Sources: the two WPF commits above and current WPF files on the brief's base. `parity/trunk` was fetched read-only; no trunk implementation was imported.

## Verification limits

Verification used .NET SDK 10.0.402 and Release configuration. Final full-Core/Avalonia test runs set `DOTNET_PROCESSOR_COUNT=2` and `DiffEngine_Disabled=true`. Build sweeps use `-m:1 -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForAdditionalFilesIfPossible=true` to limit repeated build artifacts.

- `dotnet build CCP.Core -c Release`: succeeded, 0 warnings, 0 errors.
- `bash .github/scripts/core-guards.sh`: passed both guards.
- `dotnet test Tests/CCP.Core.Tests -c Release --filter FullyQualifiedName~AudioPlaylistRulesTests`: 22 passed, 0 failed, 0 skipped.
- `dotnet test Tests/CCP.Core.Tests -c Release --no-build`: 2868 passed, 0 failed, 5 skipped (2873 total). The existing Vosk speech tests skipped because their external model is absent; no tests were changed or disabled to obtain this result.
- `dotnet test Tests/CCP.Avalonia.Tests -c Release --filter 'FullyQualifiedName~AudioPlaylistServiceTests|FullyQualifiedName~PanicSurfacesTests|FullyQualifiedName~IconGuardTests'`: 15 passed, 0 failed, 0 skipped. The new service tests exercise missing-file skip, loop-one/manual-next, pause/resume, seek clamping, previous restart, live master scaling, settings writes, removal while playing, natural end, the Deeper Play gesture, ordinary engine Stop and panic/watchdog stop using a silent device in the suite's isolated profile.
- `dotnet run --project CCP.Avalonia --no-build -c Release -- --render-all /tmp/ccp-worker6-render`: exit 0, 226/226 views rendered. Inspected `AudioPlaylistCard.png` and `DeeperTabView.png`; these show the empty playlist on the render path. The render used separate temporary CCP_USERDATA_DIR and XDG_CONFIG_HOME directories.

- Explicit `dotnet build CCP.Avalonia -c Release`: succeeded, 0 warnings, 0 errors on the final incremental check.
- `dotnet build -c Release` succeeded for every Linux test project named by `.github/workflows/build.yml`: CCP.Avalonia.Tests, CCP.Avalonia.CatalogueStartup.Tests, CCP.Avalonia.Consent.Tests, CCP.Avalonia.GeneralSettings.Tests, CCP.Avalonia.Language.Tests, CCP.Avalonia.Report.Tests, CCP.Avalonia.Scheduler.Tests, CCP.Avalonia.Snapshot.Tests, CCP.Core.Tests, CCP.Core.Content.Tests and CCP.Core.Settings.Tests. Apart from the full Core suite and the selected Avalonia classes above, these suites were compiled but not executed.
- `dotnet build Tests/ConditioningControlPanel.Tests -c Release -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: restore succeeded; the conditional WPF cross-build did not finish within 11 minutes 46 seconds and was canceled. No WPF compilation success or Windows test pass is claimed. The log ended during the temporary WPF app assembly build, without compiler diagnostics establishing a pass or failure.

- One intermediate .NET process exited 135 (SIGBUS); the clean retry above completed successfully. Earlier compile errors introduced during adaptation were fixed before the passing runs.
- Native LibVLC playback, live audio devices, the operating-system file picker, a real desktop and Windows execution were not run here. The environment has no installed libvlc. The native adapter compiled; silent-device tests prove service behavior, not device/codec behavior.
- Cloud settings backup/restore remains deliberately unwired on this Avalonia head (existing decisions in MainShellWindow.CloudBackup.cs). Playlist saves suppress cloud backup; the existing WPF backup exclusion/local-restore behavior remains intact.

## Remaining lanes

- Full WPF card row-hover tools, MoreFold presentation, time labels, and the playlist-to-enhancement bridge remain in the existing audio-player ledger lanes (including rows 64, 69, 76, 79 and 92); they are not claimed as ported by this unit. The new minimal controls make the service reachable.
- No PR was opened. Only the claimed `avalonia-port/cloud-sync7-audio-player-a` branch may be pushed.
