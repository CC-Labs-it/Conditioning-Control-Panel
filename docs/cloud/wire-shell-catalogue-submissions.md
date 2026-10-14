# wire-shell-catalogue-submissions

- Custom installed mods now expose Share in Mod Manager. The button is disabled during confirmation/submission and restored afterwards; the list refreshes its submission-status badge.
- The creator/name, tags, affirmation and valid MEGA link gate the POST. The catalogue receives metadata rather than the archive; successful shares show WPF's dedicated mod success toast. Built-in mods cannot share and signed-out requests open no dialog/send nothing.
- Extracted the `ccp-mod/v1` metadata envelope and best-effort installed-folder size into Core `ModCatalogueAsset`; WPF and Avalonia both call it. Avalonia encodes optional JPEG previews at 160 pixels wide and at most 64 KiB.
- Catalogue approval notifications now use WPF's keyed sticky behavior: no expiry timer, once per key, persisted manual dismissal. Programmatic/action dismissal leaves the condition eligible to recur. Existing preset/session share paths remain live.
- Only ledger row `shell-catalogue-submissions` changes to wired. Existing ordinary mod/confirmation windows do not introduce a WPF panic-controlled surface.
- Read-only `parity/trunk` fetched at `a4ee52d40cd347d288dcf7fdaa8445b79f8e5038`; no trunk files imported. Behavior was adapted from the WPF reference on base `0e1aa0473e7a5b6120544923e94bb04cc62a7e50`.

## Changed files

- `CCP.Avalonia/Helpers/NotificationService.cs`
- `CCP.Avalonia/Views/Dialogs/ModManagerDialog.axaml.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.CatalogueStatus.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.CatalogueSubmissions.cs`
- `CCP.Avalonia/Views/Windows/MainShellWindow.ModCatalogue.cs`
- `CCP.Core/Services/Catalogue/ModCatalogueAsset.cs`
- `ConditioningControlPanel/MainWindow/MainWindow.ModCatalogue.cs`
- `Tests/CCP.Avalonia.Tests/AssetSubmitWireTests.cs`
- `Tests/CCP.Avalonia.Tests/CatalogueShellTests.cs`
- `Tests/CCP.Avalonia.Tests/ModPreviewThumbTests.cs`
- `Tests/CCP.Avalonia.Tests/StickyNotificationTests.cs`
- `Tests/CCP.Core.Tests/ModCatalogueAssetTests.cs`
- `docs/avalonia-parity.md`
- `docs/cloud/wire-shell-catalogue-submissions.md`

## Verification limits

Commands used SDK 10.0.402 via `PATH=/workspace/.dotnet:$PATH`, `DiffEngine_Disabled=true`. Headless tests/rendering used a temporary `FONTCONFIG_FILE` selecting installed DejaVu/Liberation/Noto fonts and mapping Segoe UI to DejaVu: this host's default fontconfig selected an unsupported OpenAI Sans WOFF2 font. Tests use the repository's isolated temporary user-data profiles and fake HTTP handlers.

- Before the sticky fix, `dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter FullyQualifiedName~MinePollMarksThePresetApprovedToastsOnceAndPaintsItsPill` failed: the accepted toast carried an enabled 15-second DispatcherTimer, while the new assertion requires no expiry timer.
- Final `dotnet test Tests/CCP.Avalonia.Tests --no-build --no-restore --filter 'FullyQualifiedName~CatalogueShellTests|FullyQualifiedName~AssetSubmitWireTests|FullyQualifiedName~StickyNotificationTests|FullyQualifiedName~NotificationServiceTests|FullyQualifiedName~ModPreviewThumbTests|FullyQualifiedName~CatalogueSubmitWireTests'`: **15 passed, 0 failed, 0 skipped**. This exercises the actual mod Share button, cancellation, MEGA validation, payload, record, repaint, sticky lifecycle, preview encoding, and existing preset/session/catalogue paths.
- `dotnet test Tests/CCP.Core.Tests`: **2811 passed, 1 failed, 6 skipped**. The untouched `GoonClockAndRoundsTests.SyncedEventAndScheduledFireBothHopThroughCoreDispatch` fails `Assert.NotNull` at line 80; a focused rerun combining that test and `ModCatalogueAssetTests` produced **2 passed, 1 failed**. Both new Core envelope tests pass. Five existing Vosk tests self-skip without the model; the frame-budget test self-skips in Debug. No tests were changed to suppress those results.
- `dotnet build` passed for Core, Avalonia and every Linux test project in `.github/workflows/build.yml`: Core.Tests, Core.Settings.Tests, Core.Content.Tests; Avalonia.Tests, CatalogueStartup.Tests, Consent.Tests, GeneralSettings.Tests, Language.Tests, Report.Tests, Scheduler.Tests and Snapshot.Tests.
- Builds used `-m:1 -p:CreateHardLinksForCopyFilesToOutputDirectoryIfPossible=true -p:CreateHardLinksForCopyLocalIfPossible=true -p:CreateHardLinksForCopyAdditionalFilesIfPossible=true` after this shared environment ran out of disk space. An intermediate test run was invalidated by rewriting its hardlinked assembly during a concurrent build; the final result above was rerun against stable assemblies without rebuilding.
- `dotnet build Tests/ConditioningControlPanel.Tests/ConditioningControlPanel.Tests.csproj -m:1 -p:EnableWindowsTargeting=true -p:ValidateExecutableReferencesMatchSelfContained=false` with the copy hardlink flags plus `-p:CreateHardLinksForAdditionalFilesIfPossible=true`: **Build succeeded, 0 errors**. The first attempt compiled WPF but failed resource copies when the shared disk filled; the incremental retry with the additional-files flag succeeded. WPF application and test assemblies are compiled, not run; Windows behavior cannot be executed on this Linux host.
- `bash .github/scripts/core-guards.sh`: **All Core guards passed.**
- `CCP_USERDATA_DIR=/tmp/worker2-render-profile dotnet run --project CCP.Avalonia --no-build --no-restore -- --render-all /tmp/worker2-render`: **224/224 views rendered**. `AssetSubmitDialog.png` shows the MEGA-link/affirmation surface now reached from Mod Manager; `ModManagerDialog.png` renders the default built-in selection, where Share remains hidden. Both were inspected.
- No live catalogue/MEGA upload, real account, real desktop notification, or WPF runtime was exercised. Only the relevant Avalonia classes were run, not its entire suite. The complete Core suite is not green for the unrelated failure above.
