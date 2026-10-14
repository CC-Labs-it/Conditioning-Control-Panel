# wire-feat-subliminal

- Wire the existing Advanced → Steals Focus option to subliminal card presentation: passive show first, then the explicit focus request, while keeping mouse click-through.
- Adapt presentation and focused headless tests from read-only `parity/trunk` commit `90cdc112083834751fc0cf73717b96172b371de9`; preserve this base's Core card rules, screen targeting, first-frame backstop and whisper handling.
- Extend the existing native seam: Windows separates TRANSPARENT from NOACTIVATE and preserves focus opt-in through Avalonia style rebuilds; X11 override-redirect cards request focus directly. Unsupported/headless backends refuse the native request safely.
- Retain the existing Panic desktop-overlays registration and Core RunStateChanged Stop closure. Tests close active, passive and refused-focus cards through that same CloseAll path.
- Keep only ledger row `feat-subliminal` at **stub**. The full remaining renderer plus mandatory-video lifecycle cannot be responsibly completed in this branch's 1000-line cap.

## Verification limits

Commands used the .NET 10.0.402 SDK and `source /tmp/ccp-shared-native-env.sh` (sandboxed tests, real Vosk model and rootless LibVLC). Build flags: `-m:1 /p:UseSharedCompilation=false /p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true /p:CreateHardLinksForCopyLocalIfPossible=true /p:CreateHardLinksForAdditionalFilesIfPossible=true`.

- `dotnet build CCP.Core/CCP.Core.csproj` and `dotnet build CCP.Avalonia/CCP.Avalonia.csproj`: passed.
- `dotnet build` of every Linux test project in `.github/workflows/build.yml`: passed (`CCP.Core.Tests`, `.Settings.Tests`, `.Content.Tests`; `CCP.Avalonia.Tests`, `.Consent.Tests`, `.Language.Tests`, `.CatalogueStartup.Tests`, `.Scheduler.Tests`, `.Report.Tests`, `.GeneralSettings.Tests`, `.Snapshot.Tests`).
- WPF test project build with `-p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false`: passed, 509 warnings, 0 errors. Its Windows runtime/tests were not run on Linux.
- `bash .github/scripts/core-guards.sh`: passed (424 XAML references checked). No Core source or Core tests changed.
- `CCP_REQUIRE_VOSK=1 dotnet test Tests/CCP.Core.Tests --no-build`: final full run passed 2815, failed 0, skipped 1 existing opt-in `BoardTilesTests.A_frame_fits_the_budget` performance check. Initial full run had 1 unchanged BASE timing failure in `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch`; isolated rerun passed 12/12, then the full rerun passed. No test assertion or Core implementation was changed for that failure.
- Regression proof: reinstating the pre-fix passive presentation and coupled Win32 TRANSPARENT/NOACTIVATE policy made both new tests fail (0 passed, 2 failed); original code was restored before the final green run.
- Focused `dotnet test Tests/CCP.Avalonia.Tests --filter 'FullyQualifiedName~SubliminalStealsFocusTests|FullyQualifiedName~SubliminalWhisperShowTests|FullyQualifiedName~TextOverlayTests|FullyQualifiedName~OverlayBackendDispatchTests|FullyQualifiedName~PanicSurfacesTests'`: passed 25/25. Initial bare headless bootstraps failed font resolution; related TextOverlay/Panic fixtures now configure the shipped bundled fonts without X11, preserving all assertions. An intermediate X11-builder attempt missed the existing closure deadline; the final raw headless builder passed.
- `CCP_USERDATA_DIR=/tmp/worker3-render-profile dotnet run --project CCP.Avalonia --no-build -- --render-all /tmp/worker3-render`: passed, 224/224 views. Inspected `SubliminalOverlayWindow.png`: centred bold magenta text with white outline; `SubliminalFeatureControl.png` shows the existing card/settings. A screenshot does not prove foreground activation.
- Initial redundant builds were cancelled; a later output copy ran out of shared disk. After deduplicating only identical checked-in assets and removing only this worker's generated bin outputs, final hardlink builds completed successfully.

Native X11 focus/compositor behavior and Windows activation were compiled but have not been run on a real desktop. Headless tests substitute activation and verify presentation/style semantics. No real user profile or desktop was used.

## Follow-up lane: wire-feat-subliminal-b

- Port shared solid-mode host/compositor ownership, per-monitor coverage fallback, newest-card replacement, first-frame/fade lifetime and release on Stop/panic/one-shot completion. The current head uses separate click-through windows even when solid mode is selected.
- Port mandatory-video deferred Reset: only audible Freeze arms it; video end consumes it once, applies the 90% roll and 1–2 second gap; Stop cancels pending reset and cards. The current head always schedules an audible Freeze's reset after 4–8 seconds.
- Seed MarkWhisperAudio only once the Avalonia bark engine is present (the existing ledger calls this unavailable).
